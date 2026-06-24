using System.Windows.Media;

namespace OrthoPlanner.App.Controls;

/// <summary>Standard fixed palette for 3D model colouring.</summary>
public static class StandardColorPalette
{
    public static IReadOnlyList<Color> Colors { get; } =
    [
        // Neutrals & bone tones
        Color.FromRgb(255, 255, 255), Color.FromRgb(220, 220, 220), Color.FromRgb(180, 180, 180),
        Color.FromRgb(120, 120, 120), Color.FromRgb( 60,  60,  60), Color.FromRgb( 30,  30,  30),
        Color.FromRgb(245, 245, 230), Color.FromRgb(230, 210, 180), Color.FromRgb(200, 180, 140),
        Color.FromRgb(180, 150, 110), Color.FromRgb(160, 130,  90), Color.FromRgb(140, 110,  75),

        // Reds & oranges
        Color.FromRgb(255,  80,  80), Color.FromRgb(255, 120,  80), Color.FromRgb(255, 160,  80),
        Color.FromRgb(255, 200,  80), Color.FromRgb(255, 100, 150), Color.FromRgb(255,  60, 120),

        // Yellows & greens
        Color.FromRgb(255, 255, 100), Color.FromRgb(220, 255, 100), Color.FromRgb(120, 255, 120),
        Color.FromRgb( 80, 220, 120), Color.FromRgb( 60, 200, 160), Color.FromRgb( 80, 200, 200),

        // Blues & purples
        Color.FromRgb(100, 180, 255), Color.FromRgb( 80, 140, 255), Color.FromRgb( 70, 120, 255),
        Color.FromRgb(140, 100, 255), Color.FromRgb(200, 100, 255), Color.FromRgb(255, 100, 255),

        // Clinical / splint accents
        Color.FromRgb(200, 230, 255), Color.FromRgb(255, 150,   0), Color.FromRgb(255, 215,   0),
        Color.FromRgb(  0, 200, 120), Color.FromRgb( 27, 152, 224), Color.FromRgb(  0, 255, 255),
    ];
}
