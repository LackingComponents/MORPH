using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using HelixToolkit.Wpf.SharpDX;
using OrthoPlanner.Core;

namespace OrthoPlanner.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Mostra quale acceleratore è stato rilevato (rimuovere dopo il primo test)
        var gpu = GpuContext.Instance;
        MessageBox.Show($"GPU: {gpu.DeviceName}\nGPU disponibile: {gpu.IsGpuAvailable}");

        // Clear any temporary files left from previous sessions
        AppTempStorage.Initialize();

        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GpuContext.Instance.Dispose();
        base.OnExit(e);
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (e.Mode == PowerModes.Suspend)
            {
                foreach (Window window in Application.Current.Windows)
                    ResetViewports(window, true);
                GC.Collect();
            }
            else if (e.Mode == PowerModes.Resume)
            {
                foreach (Window window in Application.Current.Windows)
                    ResetViewports(window, false);
            }
        });
    }

    private void ResetViewports(DependencyObject parent, bool suspend)
    {
        if (parent == null) return;

        if (parent is Viewport3DX vp)
        {
            if (suspend)
            {
                if (vp.EffectsManager != null)
                {
                    vp.EffectsManager.Dispose();
                    vp.EffectsManager = null;
                }
            }
            else
            {
                vp.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
            }
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            ResetViewports(VisualTreeHelper.GetChild(parent, i), suspend);
    }
}