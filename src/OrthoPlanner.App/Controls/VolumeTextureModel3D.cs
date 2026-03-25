using System.Windows;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.SharpDX.Model.Scene;

namespace OrthoPlanner.App.Controls
{
    /// <summary>
    /// Custom wrapper for HelixToolkit's VolumeTextureNode since it lacks a native WPF wrapper in 3.1.2.
    /// Used for Direct Volume Rendering.
    /// </summary>
    public class VolumeTextureModel3D : Element3D
    {
        public static readonly DependencyProperty VolumeNodeProperty =
            DependencyProperty.Register(
                nameof(VolumeNode),
                typeof(SceneNode),
                typeof(VolumeTextureModel3D),
                new PropertyMetadata(null, (d, e) => ((VolumeTextureModel3D)d).OnVolumeNodeChanged()));

        public SceneNode? VolumeNode
        {
            get => (SceneNode?)GetValue(VolumeNodeProperty);
            set => SetValue(VolumeNodeProperty, value);
        }

        private readonly GroupNode _groupNode = new GroupNode();

        protected override SceneNode OnCreateSceneNode()
        {
            return _groupNode;
        }

        private void OnVolumeNodeChanged()
        {
            _groupNode.Clear();
            if (VolumeNode != null)
            {
                _groupNode.AddChildNode(VolumeNode);
            }
        }
    }
}
