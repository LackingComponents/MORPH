using OrthoPlanner.Core.Imaging.Cephalometry;

namespace OrthoPlanner.App.ViewModels;

/// <summary>One row in the cephalometric analysis results table.</summary>
public sealed class CephMeasurementRowViewModel
{
    public string Abbreviation { get; }
    public string Name { get; }
    public string ValueText { get; }
    public string NormText { get; }
    public string DeviationText { get; }
    public bool IsMissing { get; }
    public bool IsOutOfNorm { get; }
    public string StatusHint { get; }

    private CephMeasurementRowViewModel(
        string abbreviation, string name, string valueText, string normText,
        string deviationText, bool isMissing, bool isOutOfNorm, string statusHint)
    {
        Abbreviation = abbreviation;
        Name = name;
        ValueText = valueText;
        NormText = normText;
        DeviationText = deviationText;
        IsMissing = isMissing;
        IsOutOfNorm = isOutOfNorm;
        StatusHint = statusHint;
    }

    public static CephMeasurementRowViewModel From(CephMeasurementResult result)
    {
        string unitSuffix = result.Unit == CephUnit.Degrees ? "°" : " mm";
        string normText = result.Unit == CephUnit.Degrees
            ? $"{result.NormMean:F0}±{result.NormTolerance:F0}"
            : $"{result.NormMean:F0}±{result.NormTolerance:F0} mm";

        if (result.Status == CephMeasurementStatus.MissingLandmarks)
        {
            var missing = result.MissingLandmarkNames.Count > 0
                ? string.Join(", ", result.MissingLandmarkNames)
                : "landmarks";
            return new CephMeasurementRowViewModel(
                result.Abbreviation, result.Name, "—", normText, "—",
                isMissing: true, isOutOfNorm: false,
                statusHint: $"Missing: {missing}");
        }

        double value = result.Value!.Value;
        string valueText = result.Unit == CephUnit.Degrees
            ? $"{value:F1}°"
            : $"{value:+0.0;-0.0;0.0} mm";

        double dev = value - result.NormMean;
        string devText = result.Unit == CephUnit.Degrees
            ? $"{dev:+0.0;-0.0;0.0}"
            : $"{dev:+0.0;-0.0;0.0}";

        bool outOfNorm = result.IsWithinNorm == false;
        return new CephMeasurementRowViewModel(
            result.Abbreviation, result.Name, valueText, normText, devText,
            isMissing: false, isOutOfNorm: outOfNorm,
            statusHint: outOfNorm ? "Outside normal range" : "Within normal range");
    }
}
