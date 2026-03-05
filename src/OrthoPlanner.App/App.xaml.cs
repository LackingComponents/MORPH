using System.Windows;
using FellowOakDicom;
using FellowOakDicom.Imaging.NativeCodec;

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
    }
}
