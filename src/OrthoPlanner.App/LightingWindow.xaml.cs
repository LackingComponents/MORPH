using System.Windows;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class LightingWindow : Window
{
    public LightingWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        // Add DWM dark mode interop for this dialog too
        SourceInitialized += (s, e) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int useImmersiveDarkMode = 1;
            try
            {
                DwmSetWindowAttribute(hwnd, 20, ref useImmersiveDarkMode, sizeof(int));
            }
            catch { /* Ignore on older OS */ }
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);
}
