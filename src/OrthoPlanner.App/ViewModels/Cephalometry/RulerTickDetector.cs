using System;
using System.Linq;

namespace OrthoPlanner.App.ViewModels.Cephalometry;

/// <summary>Result of an automatic ruler tick-period search.</summary>
public sealed record TickDetection(double PeriodPx, double PeakToMedian, int SampleCount);

/// <summary>
/// Finds the tick spacing of a radiographic ruler from intensity sampled along a user-drawn segment
/// that runs along the tick marks: band-averaged profile, detrend, Hann window, spectrum peak, parabolic refinement.
/// </summary>
public static class RulerTickDetector
{
    public const int BandWidthPx = 5;
    public const int DetrendWindow = 25;
    public const int SkippedLowBins = 5;
    public const double MinPeakToMedian = 3.0;
    public const int MinSamples = 64;

    /// <summary>
    /// Detects the tick period in pixels along (x0,y0)-(x1,y1) on a row-major luminance image.
    /// Throws <see cref="InvalidOperationException"/> with a user-readable reason when no reliable period exists.
    /// </summary>
    public static TickDetection Detect(float[] luminance, int width, int height,
                                       double x0, double y0, double x1, double y1)
    {
        ArgumentNullException.ThrowIfNull(luminance);
        if (width <= 0 || height <= 0 || luminance.Length < width * height)
            throw new ArgumentException("Image size does not match the luminance buffer.");

        double length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        int n = (int)Math.Floor(length) + 1;
        if (n < MinSamples)
            throw new InvalidOperationException(
                $"Line too short ({n} px): draw it along at least {MinSamples} px of ruler ticks.");

        double[] profile = SampleProfile(luminance, width, height, x0, y0, x1, y1, n);
        double[] signal = Detrend(profile);
        ApplyHann(signal);
        double[] mag = MagnitudeSpectrum(signal);

        int last = mag.Length - 1;
        if (last <= SkippedLowBins + 1)
            throw new InvalidOperationException("Line too short for a frequency analysis.");

        int peak = SkippedLowBins;
        for (int k = SkippedLowBins; k <= last; k++)
            if (mag[k] > mag[peak]) peak = k;

        double median = Median(mag.Skip(SkippedLowBins).ToArray());
        double ratio = median > 0 ? mag[peak] / median : double.PositiveInfinity;
        if (!(ratio >= MinPeakToMedian))
            throw new InvalidOperationException(
                $"No clear tick pattern (peak only {ratio:F1}x the median, needs {MinPeakToMedian:F0}x). Use the manual method.");
        if (peak <= SkippedLowBins || peak >= last)
            throw new InvalidOperationException("Tick period is at the edge of the measurable range. Use the manual method.");

        // Parabolic interpolation of the peak position between bins
        double a = mag[peak - 1], b = mag[peak], c = mag[peak + 1];
        double denom = a - 2 * b + c;
        double delta = denom != 0 ? 0.5 * (a - c) / denom : 0;
        double binF = peak + Math.Clamp(delta, -0.5, 0.5);

        return new TickDetection(n / binF, ratio, n);
    }

    /// <summary>One sample per pixel along the line, averaged over a band perpendicular to it.</summary>
    private static double[] SampleProfile(float[] lum, int w, int h,
                                          double x0, double y0, double x1, double y1, int n)
    {
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        double ux = (x1 - x0) / len, uy = (y1 - y0) / len;
        double px = -uy, py = ux;
        int half = BandWidthPx / 2;

        var profile = new double[n];
        for (int i = 0; i < n; i++)
        {
            double cx = x0 + ux * i, cy = y0 + uy * i;
            double sum = 0;
            for (int j = -half; j <= half; j++)
                sum += Bilinear(lum, w, h, cx + px * j, cy + py * j);
            profile[i] = sum / (2 * half + 1);
        }
        return profile;
    }

    private static double Bilinear(float[] lum, int w, int h, double x, double y)
    {
        x = Math.Clamp(x, 0, w - 1);
        y = Math.Clamp(y, 0, h - 1);
        int xi = Math.Min((int)x, w - 2 < 0 ? 0 : w - 2), yi = Math.Min((int)y, h - 2 < 0 ? 0 : h - 2);
        double fx = x - xi, fy = y - yi;
        int xj = Math.Min(xi + 1, w - 1), yj = Math.Min(yi + 1, h - 1);
        double top = lum[yi * w + xi] * (1 - fx) + lum[yi * w + xj] * fx;
        double bot = lum[yj * w + xi] * (1 - fx) + lum[yj * w + xj] * fx;
        return top * (1 - fy) + bot * fy;
    }

    /// <summary>Subtracts a centred moving average (window shrinks at the ends).</summary>
    private static double[] Detrend(double[] v)
    {
        int n = v.Length, half = DetrendWindow / 2;
        var prefix = new double[n + 1];
        for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + v[i];

        var outp = new double[n];
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - half), hi = Math.Min(n - 1, i + half);
            outp[i] = v[i] - (prefix[hi + 1] - prefix[lo]) / (hi - lo + 1);
        }
        return outp;
    }

    private static void ApplyHann(double[] v)
    {
        int n = v.Length;
        for (int i = 0; i < n; i++)
            v[i] *= 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
    }

    /// <summary>Magnitude of the discrete Fourier transform, bins 0..n/2 (direct evaluation, exact bin spacing 1/n).</summary>
    private static double[] MagnitudeSpectrum(double[] v)
    {
        int n = v.Length, bins = n / 2 + 1;
        var mag = new double[bins];
        for (int k = 0; k < bins; k++)
        {
            double re = 0, im = 0, w = -2 * Math.PI * k / n;
            for (int i = 0; i < n; i++)
            {
                re += v[i] * Math.Cos(w * i);
                im += v[i] * Math.Sin(w * i);
            }
            mag[k] = Math.Sqrt(re * re + im * im);
        }
        return mag;
    }

    private static double Median(double[] v)
    {
        if (v.Length == 0) return 0;
        Array.Sort(v);
        int m = v.Length / 2;
        return v.Length % 2 == 1 ? v[m] : 0.5 * (v[m - 1] + v[m]);
    }
}
