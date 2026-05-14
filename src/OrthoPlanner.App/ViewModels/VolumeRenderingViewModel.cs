using CommunityToolkit.Mvvm.ComponentModel;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    // ÔöÇÔöÇÔöÇ Direct Volume Rendering (Diffused View) ÔöÇÔöÇÔöÇ
    [ObservableProperty] private bool _isVolumeRenderingEnabled;
    [ObservableProperty] private HelixToolkit.SharpDX.Model.Scene.GroupNode? _volumeNode;

    partial void OnIsVolumeRenderingEnabledChanged(bool value)
    {
        if (value && VolumeNode == null && Volume != null)
        {
            SetupVolumeMaterial();
        }
    }

    private void SetupVolumeMaterial()
    {
        if (Volume == null) return;

        int w = Volume.Width;
        int h = Volume.Height;
        int d = Volume.Depth;

        var pixels = new Half[Volume.Voxels.Length * 4];

        for (int i = 0; i < Volume.Voxels.Length; i++)
        {
            float hu = Volume.Voxels[i];
            float val = (hu + 1024f) / 4000f;
            val = Math.Clamp(val, 0f, 1f);

            float alpha = (hu > 200) ? 0.3f : 0f;

            pixels[i*4 + 0] = (Half)val;
            pixels[i*4 + 1] = (Half)val;
            pixels[i*4 + 2] = (Half)val;
            pixels[i*4 + 3] = (Half)alpha;
        }

        var texParams = new HelixToolkit.SharpDX.Model.VolumeTextureParams(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray(),
            w, h, d, SharpDX.DXGI.Format.R16G16B16A16_Float
        );

        // Creates a 1D gradient texture for the lookup map
        var mapPixels = new HelixToolkit.Maths.Color4[] {
            new HelixToolkit.Maths.Color4(0f, 0f, 0f, 0f),       // Black trans
            new HelixToolkit.Maths.Color4(1f, 0.9f, 0.8f, 0.05f), // Pale bone
            new HelixToolkit.Maths.Color4(1f, 1f, 1f, 0.5f)       // Solid bone
        };

        var volumeMaterial = new HelixToolkit.SharpDX.Model.VolumeTextureRawDataMaterialCore
        {
            VolumeTexture = texParams,
            Color = new HelixToolkit.Maths.Color4(1f, 1f, 1f, 1f),
            TransferMap = mapPixels,
            SampleDistance = 0.0015,
            MaxIterations = 1500,
            IterationOffset = 1
        };

        // By default, the VolumeTextureNode renders from [-0.5, -0.5, -0.5] to [0.5, 0.5, 0.5].
        // We scale it up by the CT volume physical dimensions, and translate it by half its size
        // so it starts at (0,0,0) exactly like our CT meshes do.
        float sizeX = (float)(w * Volume.Spacing[0]);
        float sizeY = (float)(h * Volume.Spacing[1]);
        float sizeZ = (float)(d * Volume.Spacing[2]);

        var node = new HelixToolkit.SharpDX.Model.Scene.VolumeTextureNode
        {
            Material = volumeMaterial,
            ModelMatrix = System.Numerics.Matrix4x4.CreateScale(sizeX, sizeY, sizeZ) *
                          System.Numerics.Matrix4x4.CreateTranslation(sizeX / 2f, sizeY / 2f, sizeZ / 2f)
        };

        var group = new HelixToolkit.SharpDX.Model.Scene.GroupNode();
        group.AddChildNode(node);
        VolumeNode = group;
        // Release large managed buffers held during texture upload
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
    }
}
