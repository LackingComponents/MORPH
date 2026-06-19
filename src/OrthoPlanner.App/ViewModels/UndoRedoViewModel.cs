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

        // NHP transform state (needed so undo after commit reverses correctly)
        public Matrix3D CumulativeNhpMatrix { get; init; }
        public Point3D? VolumePivot { get; init; }

        // NHP committed baseline values
        public double CBaseLat { get; init; }
        public double CBaseAnt { get; init; }
        public double CBaseVert { get; init; }
        public double CBaseRoll { get; init; }
        public double CBasePitch { get; init; }
        public double CBaseYaw { get; init; }

        // Anatomical landmarks (DICOM or NHP space depending on commit state)
        public (double X, double Y, double Z)? LeftCondyleCenter { get; init; }
        public (double X, double Y, double Z)? RightCondyleCenter { get; init; }
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
        clone.BuildModel();
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
            MaxillaOcclusionTransform  = m.MaxillaOcclusionTransform,
            MandibleOcclusionTransform = m.MandibleOcclusionTransform,
            OnVisibilityChanged = RefreshCombinedModel
        };
        clone.BuildModel();
        return clone;
    }

    private void SaveStateForUndo()
    {
        _undoStack.Push(CreateStateSnapshot());
        _redoStack.Clear();
        // Keep at most 5 undo entries to prevent stale mesh data accumulating in memory
        if (_undoStack.Count > 5)
        {
            var kept = _undoStack.ToArray(); // index 0 = newest
            _undoStack.Clear();
            for (int i = 4; i >= 0; i--) _undoStack.Push(kept[i]);
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

            // NHP state
            CumulativeNhpMatrix = _cumulativeNhpMatrix,
            VolumePivot = VolumePivot,
            CBaseLat    = _cLat,  CBaseAnt  = _cAnt,  CBaseVert = _cVert,
            CBaseRoll   = _cRoll, CBasePitch = _cPitch, CBaseYaw = _cYaw,

            // Landmarks
            LeftCondyleCenter  = LeftCondyleCenter,
            RightCondyleCenter = RightCondyleCenter,
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
            foreach (var s in snapshot.Segments) Segments.Add(s);

            ImportedMeshes.Clear();
            foreach (var m in snapshot.ImportedMeshes) ImportedMeshes.Add(m);

            LoadedOcclusions.Clear();
            foreach (var o in snapshot.LoadedOcclusions) LoadedOcclusions.Add(o);

            HardTissueModel = snapshot.HardTissueModel;
            SoftTissueModel = snapshot.SoftTissueModel;
            DentalModel     = snapshot.DentalModel;

            // Restore NHP transform state
            _cumulativeNhpMatrix = snapshot.CumulativeNhpMatrix;
            VolumePivot  = snapshot.VolumePivot;
            _cLat   = snapshot.CBaseLat;   _cAnt   = snapshot.CBaseAnt;
            _cVert  = snapshot.CBaseVert;  _cRoll  = snapshot.CBaseRoll;
            _cPitch = snapshot.CBasePitch; _cYaw   = snapshot.CBaseYaw;
            OnPropertyChanged(nameof(IsNhpDirty));

            // Restore landmarks
            LeftCondyleCenter  = snapshot.LeftCondyleCenter;
            RightCondyleCenter = snapshot.RightCondyleCenter;
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
