using CommunityToolkit.Mvvm.Input;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    // ÔöÇÔöÇÔöÇ Undo/Redo Stacks ÔöÇÔöÇÔöÇ
    private readonly Stack<StateSnapshot> _undoStack = new();
    private readonly Stack<StateSnapshot> _redoStack = new();

    private class StateSnapshot
    {
        public List<SegmentViewModel> Segments { get; init; } = new();
        public List<MeshViewModel> ImportedMeshes { get; init; } = new();
        public SegmentViewModel? HardTissueModel { get; init; }
        public SegmentViewModel? SoftTissueModel { get; init; }
        public SegmentViewModel? DentalModel { get; init; }

        // Deep copies of vertex arrays at snapshot time. Several commands (BSSO,
        // CleanMerge, AlignDentalScans, EditDentalCasts, NHP bake) mutate Vertices
        // on the SAME object instances held by this snapshot, so keeping only the
        // references would make Undo silently no-op for those operations.
        public Dictionary<SegmentViewModel, float[]?> SegmentVertices { get; init; } = new();
        public Dictionary<MeshViewModel, float[]?> MeshVertices { get; init; } = new();
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
        var snapshot = new StateSnapshot
        {
            Segments = Segments.ToList(),
            ImportedMeshes = ImportedMeshes.ToList(),
            HardTissueModel = HardTissueModel,
            SoftTissueModel = SoftTissueModel,
            DentalModel = DentalModel
        };

        foreach (var s in snapshot.Segments)
            snapshot.SegmentVertices[s] = (float[]?)s.Vertices?.Clone();
        foreach (var s in new[] { HardTissueModel, SoftTissueModel, DentalModel })
            if (s != null && !snapshot.SegmentVertices.ContainsKey(s))
                snapshot.SegmentVertices[s] = (float[]?)s.Vertices?.Clone();
        foreach (var m in snapshot.ImportedMeshes)
            snapshot.MeshVertices[m] = (float[]?)m.Vertices?.Clone();

        return snapshot;
    }

    private void RestoreStateSnapshot(StateSnapshot snapshot)
    {
        Segments.Clear();
        foreach (var s in snapshot.Segments) Segments.Add(s);

        ImportedMeshes.Clear();
        foreach (var m in snapshot.ImportedMeshes) ImportedMeshes.Add(m);

        HardTissueModel = snapshot.HardTissueModel;
        SoftTissueModel = snapshot.SoftTissueModel;
        DentalModel = snapshot.DentalModel;

        // Revert any in-place vertex mutations; rebuild geometry only when needed.
        foreach (var (seg, savedVerts) in snapshot.SegmentVertices)
        {
            if (SameVertices(seg.Vertices, savedVerts)) continue;
            seg.Vertices = savedVerts;
            seg.BuildModel();
        }
        foreach (var (mesh, savedVerts) in snapshot.MeshVertices)
        {
            if (SameVertices(mesh.Vertices, savedVerts)) continue;
            mesh.Vertices = savedVerts;
            mesh.BuildModel();
        }

        RefreshCombinedModel();
    }

    private static bool SameVertices(float[]? a, float[]? b)
        => ReferenceEquals(a, b) || (a != null && b != null && a.AsSpan().SequenceEqual(b));
}
