using System.Configuration;
using System.Data;
using System.Windows;
using OrthoPlanner.Core;
using FellowOakDicom;
using FellowOakDicom.Imaging.NativeCodec;
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
        // Register fo-dicom native codecs so JPEG Lossless (and other compressed
        // transfer syntaxes) are automatically decompressed when reading pixel data.
        new DicomSetupBuilder()
            .RegisterServices(s => s
                .AddFellowOakDicom()
                .AddTranscoderManager<NativeTranscoderManager>())
            .SkipValidation()
            .Build();

        base.OnStartup(e);

        // Register global Loaded handler for Windows to apply dark title bar
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));

        // Clear any temporary files left from previous sessions
        AppTempStorage.Initialize();

        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int useImmersiveDarkMode = 1;
                try
                {
                    DwmSetWindowAttribute(hwnd, 20, ref useImmersiveDarkMode, sizeof(int));
                    DwmSetWindowAttribute(hwnd, 19, ref useImmersiveDarkMode, sizeof(int));
                }
                catch { /* Ignore on older OS */ }
            }
        }
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

