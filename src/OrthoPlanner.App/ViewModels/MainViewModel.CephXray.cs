using CommunityToolkit.Mvvm.ComponentModel;
using OrthoPlanner.App.ViewModels.Cephalometry;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    // ─── Cephalometry image source (DRR from CT, or a real lateral X-ray) ───
    // Owned here so ProjectViewModel can persist it; CephalometryOverlay displays it.
    [ObservableProperty] private CephXrayImage? _cephXray;
    [ObservableProperty] private CephImageSource _cephSource = CephImageSource.Drr;
}
