using System.Windows;

namespace OrthoPlanner.App;

/// <summary>
/// Interaction logic for SplashWindow.xaml
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public string Status
    {
        get => StatusText.Text;
        set => StatusText.Text = value;
    }
}
