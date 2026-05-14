namespace OrthoPlanner.Core.Imaging;

/// <summary>
/// Represents a cephalometric landmark with its definition and placement state.
/// </summary>
public class CephalometricLandmark
{
    /// <summary>Full name of the landmark (e.g., "Nasion").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Standard abbreviation (e.g., "N", "Po-L").</summary>
    public string Abbreviation { get; init; } = string.Empty;

    /// <summary>Anatomical description of the landmark.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Category: Skeletal or Dental.</summary>
    public LandmarkCategory Category { get; init; }

    /// <summary>True for bilateral landmarks (left/right pairs).</summary>
    public bool IsBilateral { get; init; }

    /// <summary>
    /// Position on the DRR image in image-pixel coordinates (X, Y).
    /// Null if not yet placed.
    /// </summary>
    public (double X, double Y)? Position { get; set; }

    /// <summary>
    /// 3D position in physical (mm) coordinates from HelixToolkit hit-test.
    /// Null if placed only in 2D or not placed at all.
    /// </summary>
    public (double X, double Y, double Z)? Position3D { get; set; }

    /// <summary>Whether this landmark has been placed on the image.</summary>
    public bool IsPlaced => Position.HasValue;

    /// <summary>Whether this landmark was placed in 3D (has full X,Y,Z coordinates).</summary>
    public bool IsPlaced3D => Position3D.HasValue;
}

public enum LandmarkCategory
{
    Skeletal,
    Dental
}
