using System.Windows;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class LightingWindow : Window
{
    // ponytail: DWM SourceInitialized + P/Invoke removed — App.OnWindowLoaded handles all windows globally
    public LightingWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
