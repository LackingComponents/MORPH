using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OrthoPlanner.Core.Imaging.Cephalometry;

namespace OrthoPlanner.App.ViewModels;

/// <summary>
/// Binds the cephalometry sidebar analysis tabs (Steiner / Tweed / Ricketts) and their result tables.
/// </summary>
public partial class CephAnalysisPanelViewModel : ObservableObject
{
    public ObservableCollection<CephMeasurementRowViewModel> SteinerRows { get; } = new();
    public ObservableCollection<CephMeasurementRowViewModel> TweedRows { get; } = new();
    public ObservableCollection<CephMeasurementRowViewModel> RickettsRows { get; } = new();

    [ObservableProperty]
    private string _statusNote = "";

    public void Update(CephAnalysisResult steiner, CephAnalysisResult tweed, CephAnalysisResult ricketts)
    {
        StatusNote = "";
        ReplaceRows(SteinerRows, steiner);
        ReplaceRows(TweedRows, tweed);
        ReplaceRows(RickettsRows, ricketts);
    }

    private static void ReplaceRows(
        ObservableCollection<CephMeasurementRowViewModel> target, CephAnalysisResult result)
    {
        target.Clear();
        foreach (var m in result.Measurements)
            target.Add(CephMeasurementRowViewModel.From(m));
    }
}
