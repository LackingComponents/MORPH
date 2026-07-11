using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.Input;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    // ─── Undo/Redo Stacks ───
    private readonly Stack<StateSnapshot> _undoStack = new();
    private readonly Stack<StateSnapshot> _redoStack = new();

    /// <summary>
    /// When true, the NHP ledger CollectionChanged handlers will apply the
    /// visual delta transform but skip baking cumulative NHP into vertices.
    /// Set during RestoreStateSnapshot and project load to prevent double-bake.
    /// </summary>
    internal bool SuppressLedgerBake = false;

    private class StateSnapshot
    {
        // Deep-cloned segment/mesh collections (vertices are cloned arrays)
        public List<SegmentViewModel> Segments { get; init; } = new();
        public List<MeshViewModel> ImportedMeshes { get; init; } = new();
        public List<MeshViewModel> LoadedOcclusions { get; init; } = new();
        public SegmentViewModel? HardTissueModel { get; init; }
        public SegmentViewModel? SoftTissueModel { get; init; }
        public SegmentViewModel? DentalModel { get; init; }

        // NHP pivot (needed so restored pieces re-pose under the same rotation center).
        // The lazy model keeps the live six sliders uncaptured here: SaveStateForUndo is only called
        // before surgical ops, never before an NHP commit (commit is a flag flip, no vertex bake), so the
        // NHP pose is identical across an undo boundary — and reverting it on undo-of-surgery would
        // "lose NHP along the way" (req c). Restore re-poses via RecomputeAllTransforms instead.
        public Point3D? VolumePivot { get; init; }

        // Anatomical landmarks (DICOM or NHP space depending on commit state)
        public (double X, double Y, double Z)? LeftCondyleCenter { get; init; }
        public (double X, double Y, double Z)? RightCondyleCenter { get; init; }
        public (double X, double Y, double Z)? LeftCondyleHalfExtents { get; init; }
        public (double X, double Y, double Z)? RightCondyleHalfExtents { get; init; }
        public (double X, double Y, double Z)? DentalMidlinePoint { get; init; }

        // Cephalometric 3D coordinates
        public List<CephLandmarkSave> CephLandmarks { get; init; } = new();
    }

    // ─── Deep-Clone Helpers ───

    private static float[]? CloneVertices(float[]? verts)
        => verts == null ? null : (float[])verts.Clone();

    private SegmentViewModel DeepCloneSegment(SegmentViewModel s)
    {
        var clone = new SegmentViewModel
        {
            Label    = s.Label,
            Name     = s.Name,
            Vertices = CloneVertices(s.Vertices),
            ColorR   = s.ColorR, ColorG = s.ColorG, ColorB = s.ColorB,
            IsVisible = s.IsVisible,
            Opacity   = s.Opacity,
            SurgicalTransform = s.SurgicalTransform,
            OnVisibilityChanged = RefreshCombinedModel
        };
        // ponytail: no BuildModel() here — snapshots are never rendered;
        // geometry is built on restore in RestoreStateSnapshot()
        return clone;
    }

    private MeshViewModel DeepCloneMesh(MeshViewModel m)
    {
        var clone = new MeshViewModel
        {
            Name     = m.Name,
            Vertices = CloneVertices(m.Vertices),
            ColorR   = m.ColorR, ColorG = m.ColorG, ColorB = m.ColorB,
            ScanType = m.ScanType,
            IsVisible = m.IsVisible,
            LocalTransform = m.LocalTransform,
            MaxillaOcclusionTransform  = m.MaxillaOcclusionTransform,
            MandibleOcclusionTransform = m.MandibleOcclusionTransform,
            OnVisibilityChanged = RefreshCombinedModel
        };
        return clone;
    }

    private void SaveStateForUndo()
    {
        _undoStack.Push(CreateStateSnapshot());
        _redoStack.Clear();
        // ponytail: cap 3 undo entries (from 5) — each holds cloned vertex arrays
        // that can be hundreds of MB; 3 is enough for normal surgical workflow
        if (_undoStack.Count > 3)
        {
            var kept = _undoStack.ToArray(); // index 0 = newest
            _undoStack.Clear();
            for (int i = 2; i >= 0; i--) _undoStack.Push(kept[i]);
        }
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(CreateStateSnapshot());
        RestoreStateSnapshot(_undoStack.Pop());
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(CreateStateSnapshot());
        RestoreStateSnapshot(_redoStack.Pop());
    }

    private StateSnapshot CreateStateSnapshot()
    {
        // Track which segment VMs are named models to avoid double-cloning
        var namedSet = new HashSet<SegmentViewModel>();
        if (HardTissueModel != null) namedSet.Add(HardTissueModel);
        if (SoftTissueModel != null) namedSet.Add(SoftTissueModel);
        if (DentalModel     != null) namedSet.Add(DentalModel);

        // Deep-clone all segments
        var clonedSegs    = new List<SegmentViewModel>();
        var segCloneMap   = new Dictionary<SegmentViewModel, SegmentViewModel>();
        foreach (var s in Segments)
        {
            var clone = DeepCloneSegment(s);
            clonedSegs.Add(clone);
            segCloneMap[s] = clone;
        }

        // Resolve named model references into their clones (or clone independently)
        SegmentViewModel? hardClone = HardTissueModel == null ? null
            : segCloneMap.TryGetValue(HardTissueModel, out var hc) ? hc : DeepCloneSegment(HardTissueModel);
        SegmentViewModel? softClone = SoftTissueModel == null ? null
            : segCloneMap.TryGetValue(SoftTissueModel, out var sc) ? sc : DeepCloneSegment(SoftTissueModel);
        SegmentViewModel? dentClone = DentalModel == null ? null
            : segCloneMap.TryGetValue(DentalModel, out var dc) ? dc : DeepCloneSegment(DentalModel);

        return new StateSnapshot
        {
            Segments       = clonedSegs,
            ImportedMeshes = ImportedMeshes.Select(DeepCloneMesh).ToList(),
            LoadedOcclusions = LoadedOcclusions.Select(DeepCloneMesh).ToList(),
            HardTissueModel = hardClone,
            SoftTissueModel = softClone,
            DentalModel     = dentClone,

            // NHP pivot (lazy model: live sliders are uncaptured — see StateSnapshot).
            VolumePivot = VolumePivot,

            // Landmarks
            LeftCondyleCenter  = LeftCondyleCenter,
            RightCondyleCenter = RightCondyleCenter,
            LeftCondyleHalfExtents  = LeftCondyleHalfExtents,
            RightCondyleHalfExtents = RightCondyleHalfExtents,
            DentalMidlinePoint = DentalMidlinePoint,

            // Ceph landmarks (records are immutable — safe to copy list)
            CephLandmarks = new List<CephLandmarkSave>(SavedCephLandmarks),
        };
    }

    private void RestoreStateSnapshot(StateSnapshot snapshot)
    {
        // Suppress ledger bake: restored segments already have the correct baked vertices
        SuppressLedgerBake = true;
        try
        {
            Segments.Clear();
            foreach (var s in snapshot.Segments) { s.BuildModel(); Segments.Add(s); }

            ImportedMeshes.Clear();
            foreach (var m in snapshot.ImportedMeshes) { m.BuildModel(); ImportedMeshes.Add(m); }

            LoadedOcclusions.Clear();
            foreach (var o in snapshot.LoadedOcclusions) { o.BuildModel(); LoadedOcclusions.Add(o); }

            HardTissueModel = snapshot.HardTissueModel; HardTissueModel?.BuildModel();
            SoftTissueModel = snapshot.SoftTissueModel; SoftTissueModel?.BuildModel();
            DentalModel     = snapshot.DentalModel;     DentalModel?.BuildModel();

            // Restore NHP pivot (lazy model: live sliders uncaptured — see StateSnapshot).
            // The restored pieces re-pose via UpdateNhpTransform + RefreshCombinedModel below,
            // each composing NhpShared with the restored per-piece LocalTransform.
            VolumePivot = snapshot.VolumePivot;

            // Restore landmarks
            LeftCondyleCenter  = snapshot.LeftCondyleCenter;
            RightCondyleCenter = snapshot.RightCondyleCenter;
            LeftCondyleHalfExtents  = snapshot.LeftCondyleHalfExtents;
            RightCondyleHalfExtents = snapshot.RightCondyleHalfExtents;
            DentalMidlinePoint = snapshot.DentalMidlinePoint;

            // Restore ceph landmarks
            SavedCephLandmarks = new List<CephLandmarkSave>(snapshot.CephLandmarks);
        }
        finally
        {
            SuppressLedgerBake = false;
        }

        // Re-apply current visual delta (restores Transform3D on all objects)
        UpdateNhpTransform();
        RefreshCombinedModel();
        UpdateAllSlices();
    }
}
