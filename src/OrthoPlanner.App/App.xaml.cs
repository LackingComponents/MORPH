using System.Configuration;
using System.Data;
using System.Windows;
using OrthoPlanner.Core;
using System;
using Microsoft.Win32;
using HelixToolkit.Wpf.SharpDX;

namespace OrthoPlanner.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Clear any temporary files left from previous sessions
        AppTempStorage.Initialize();

        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (e.Mode == PowerModes.Suspend)
            {
                // Discard all EffectsManagers safely to avoid DirectX device lock deadlocks during sleep
                foreach (Window window in Application.Current.Windows)
                {
                    ResetViewports(window, true);
                }
                GC.Collect();
            }
            else if (e.Mode == PowerModes.Resume)
            {
                // Restore EffectsManagers after waking up
                foreach (Window window in Application.Current.Windows)
                {
                    ResetViewports(window, false);
                }
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
        
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            ResetViewports(System.Windows.Media.VisualTreeHelper.GetChild(parent, i), suspend);
        }
    }
}

