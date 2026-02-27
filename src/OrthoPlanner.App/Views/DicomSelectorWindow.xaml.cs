using System.Windows;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App.Views;

public partial class DicomSelectorWindow : Window
{
    public DicomSelectorWindow(DicomSelectorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.OnClose = Close;
    }
}
