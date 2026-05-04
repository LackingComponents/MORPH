using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OrthoPlanner.App.ViewModels;

/// <summary>Tree node representing one loaded+aligned occlusion with its surgical plans.</summary>
public partial class OcclusionNodeViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "Occlusion";
    [ObservableProperty] private bool   _isExpanded = true;
    [ObservableProperty] private bool   _isActive;

    /// <summary>The underlying occlusion mesh (lives in MainViewModel.LoadedOcclusions).</summary>
    public MeshViewModel Occlusion { get; set; } = null!;

    public ObservableCollection<OcclusionPlanViewModel> Plans { get; } = new();
}
