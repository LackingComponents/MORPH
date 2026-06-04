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
            // Set the window icon if not already set
            if (window.Icon == null)
            {
                try
                {
                    var iconUri = new Uri("pack://application:,,,/OrthoPlanner;component/Resources/app.png", UriKind.Absolute);
                    window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
                }
                catch { /* Ignore if resource is not found/loaded yet */ }
            }

            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int useImmersiveDarkMode = 1;
                try
                {
                    // 1. Enable immersive dark mode
                    DwmSetWindowAttribute(hwnd, 20, ref useImmersiveDarkMode, sizeof(int));
                    DwmSetWindowAttribute(hwnd, 19, ref useImmersiveDarkMode, sizeof(int));

                    // Default to BgDark color (#0D1117)
                    int colorRef = 0x0D | (0x11 << 8) | (0x17 << 16);

                    if (window.Background is System.Windows.Media.SolidColorBrush brush)
                    {
                        var color = brush.Color;
                        colorRef = color.R | (color.G << 8) | (color.B << 16);
                    }

                    // 2. Set Caption Color to match background (DWMWA_CAPTION_COLOR = 35)
                    DwmSetWindowAttribute(hwnd, 35, ref colorRef, sizeof(int));

                    // 3. Set Window Border Color to match background (DWMWA_BORDER_COLOR = 34)
                    DwmSetWindowAttribute(hwnd, 34, ref colorRef, sizeof(int));

                    // 4. Set Title Text Color to light grey (DWMWA_TEXT_COLOR = 36)
                    int textColorRef = 0xD0 | (0xD8 << 8) | (0xE0 << 16); // #D0D8E0
                    DwmSetWindowAttribute(hwnd, 36, ref textColorRef, sizeof(int));
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

