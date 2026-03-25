using System.Configuration;
using System.Data;
using System.Windows;
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
        
        // Clear any temporary files left from previous sessions
        AppTempStorage.Initialize();
    }
}

