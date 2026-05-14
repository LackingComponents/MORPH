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
        return new StateSnapshot
        {
            Segments = Segments.ToList(),
            ImportedMeshes = ImportedMeshes.ToList(),
            HardTissueModel = HardTissueModel,
            SoftTissueModel = SoftTissueModel,
            DentalModel = DentalModel
        };
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

        RefreshCombinedModel();
    }
}
