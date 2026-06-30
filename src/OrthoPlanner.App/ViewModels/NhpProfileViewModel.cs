using CommunityToolkit.Mvvm.ComponentModel;

namespace OrthoPlanner.App.ViewModels;

/// <summary>Saved Natural Head Position preset (translations + rotations).</summary>
public partial class NhpProfileViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "NHP 1";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isCommitted;
    [ObservableProperty] private bool _isLatest;

    public double Lateral { get; set; }
    public double Anteroposterior { get; set; }
    public double Vertical { get; set; }
    public double Roll { get; set; }
    public double Pitch { get; set; }
    public double Yaw { get; set; }
}
