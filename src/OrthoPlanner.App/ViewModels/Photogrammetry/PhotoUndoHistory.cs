using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace OrthoPlanner.App.ViewModels.Photogrammetry;

/// <summary>
/// Undo / redo for the Photogrammetry section, independent of the 3D history. A snapshot keeps photo REFERENCES
/// (no image bytes), the active photo and, per photo, copies of the annotations plus scale, rotation and overlay.
/// </summary>
public sealed class PhotoUndoHistory
{
    public const int Capacity = 50;

    private sealed record MeasurementCopy(string Name, Point Start, Point End, double DistanceMm);
    private sealed record LineCopy(Point Start, Point End, string Color);
    private sealed record AngleCopy(string Name, Point L1Start, Point L1End, Point L2Start, Point L2End, double AngleDeg);

    private sealed record PhotoState(
        IReadOnlyList<MeasurementCopy> Measurements, IReadOnlyList<LineCopy> Lines, IReadOnlyList<AngleCopy> Angles,
        double PixelsPerMm, bool IsNormalized, double RotationAngle, CephOverlayState? CephOverlay);

    private sealed record Snapshot(
        string Action, IReadOnlyList<PhotoViewModel> Photos, PhotoViewModel? Active,
        IReadOnlyDictionary<PhotoViewModel, PhotoState> States);

    // Newest last; trimmed from the front beyond Capacity.
    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Records the current state before a user change named <paramref name="action"/>; clears redo.</summary>
    public void SaveUndo(PhotogrammetryViewModel vm, string action)
    {
        Push(_undo, Capture(vm, action));
        _redo.Clear();
    }

    /// <summary>Restores the previous state. Returns the status text.</summary>
    public string Undo(PhotogrammetryViewModel vm)
    {
        if (_undo.Count == 0) return "Nothing to undo in Photogrammetry";
        var s = Pop(_undo);
        Push(_redo, Capture(vm, s.Action));
        Restore(vm, s);
        return $"Undo: {s.Action}";
    }

    /// <summary>Re-applies the last undone change. Returns the status text.</summary>
    public string Redo(PhotogrammetryViewModel vm)
    {
        if (_redo.Count == 0) return "Nothing to redo in Photogrammetry";
        var s = Pop(_redo);
        Push(_undo, Capture(vm, s.Action));
        Restore(vm, s);
        return $"Redo: {s.Action}";
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private static void Push(List<Snapshot> stack, Snapshot s)
    {
        stack.Add(s);
        if (stack.Count > Capacity) stack.RemoveAt(0);
    }

    private static Snapshot Pop(List<Snapshot> stack)
    {
        var s = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return s;
    }

    private static Snapshot Capture(PhotogrammetryViewModel vm, string action) =>
        new(action, vm.Photos.ToList(), vm.ActivePhoto, vm.Photos.Distinct().ToDictionary(p => p, CaptureState));

    private static PhotoState CaptureState(PhotoViewModel p) => new(
        p.Measurements.Select(m => new MeasurementCopy(m.Name, m.StartPoint, m.EndPoint, m.DistanceMm)).ToList(),
        p.LineAnnotations.Select(l => new LineCopy(l.StartPoint, l.EndPoint, l.Color)).ToList(),
        p.AngleAnnotations.Select(a => new AngleCopy(a.Name, a.L1Start, a.L1End, a.L2Start, a.L2End, a.AngleDeg)).ToList(),
        p.PixelsPerMm, p.IsNormalized, p.RotationAngle, p.CephOverlay);

    private static void Restore(PhotogrammetryViewModel vm, Snapshot s)
    {
        if (!vm.Photos.SequenceEqual(s.Photos))
        {
            if (vm.ActivePhoto != null && !s.Photos.Contains(vm.ActivePhoto)) vm.ActivePhoto = null;
            vm.Photos.Clear();
            foreach (var p in s.Photos) vm.Photos.Add(p);
        }
        foreach (var (photo, state) in s.States) Apply(photo, state);
        foreach (var p in vm.Photos) p.IsSelected = ReferenceEquals(p, s.Active);
        vm.ActivePhoto = s.Active;
    }

    private static void Apply(PhotoViewModel p, PhotoState st)
    {
        p.Measurements.Clear();
        foreach (var m in st.Measurements)
            p.Measurements.Add(new MeasurementViewModel { Name = m.Name, StartPoint = m.Start, EndPoint = m.End, DistanceMm = m.DistanceMm });
        p.LineAnnotations.Clear();
        foreach (var l in st.Lines)
            p.LineAnnotations.Add(new LineAnnotationViewModel { StartPoint = l.Start, EndPoint = l.End, Color = l.Color });
        p.AngleAnnotations.Clear();
        foreach (var a in st.Angles)
            p.AngleAnnotations.Add(new AngleAnnotationViewModel
            {
                Name = a.Name, L1Start = a.L1Start, L1End = a.L1End, L2Start = a.L2Start, L2End = a.L2End, AngleDeg = a.AngleDeg,
            });
        p.PixelsPerMm = st.PixelsPerMm;
        p.IsNormalized = st.IsNormalized;
        p.RotationAngle = st.RotationAngle;
        p.CephOverlay = st.CephOverlay;
    }
}
