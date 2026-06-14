using CommunityToolkit.Mvvm.ComponentModel;

namespace OrthoPlanner.App.ViewModels;

/// <summary>A named snapshot of all surgical movement values and the active surgery mode.</summary>
public partial class OcclusionPlanViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "Plan";
    [ObservableProperty] private bool _isSelected;

    // ── Surgery mode ──────────────────────────────────────
    public bool IsMaxillaBasedSurgery  { get; set; }
    public bool IsMandibleBasedSurgery { get; set; }
    public bool IsManualOcclusionSurgery { get; set; }
    public bool IsKeepOcclusionSurgery { get; set; }

    // ── Maxilla movements ─────────────────────────────────
    public double MaxillaLat   { get; set; }
    public double MaxillaAnt   { get; set; }
    public double MaxillaVert  { get; set; }
    public double MaxillaRoll  { get; set; }
    public double MaxillaPitch { get; set; }
    public double MaxillaYaw   { get; set; }

    // ── Mandible movements ────────────────────────────────
    public double MandibleLat   { get; set; }
    public double MandibleAnt   { get; set; }
    public double MandibleVert  { get; set; }
    public double MandibleRoll  { get; set; }
    public double MandiblePitch { get; set; }
    public double MandibleYaw   { get; set; }

    // ── Right Ramus ───────────────────────────────────────
    public double RightRamusLat   { get; set; }
    public double RightRamusAnt   { get; set; }
    public double RightRamusVert  { get; set; }
    public double RightRamusRoll  { get; set; }
    public double RightRamusPitch { get; set; }
    public double RightRamusYaw   { get; set; }

    // ── Left Ramus ────────────────────────────────────────
    public double LeftRamusLat   { get; set; }
    public double LeftRamusAnt   { get; set; }
    public double LeftRamusVert  { get; set; }
    public double LeftRamusRoll  { get; set; }
    public double LeftRamusPitch { get; set; }
    public double LeftRamusYaw   { get; set; }

    // ── Chin ──────────────────────────────────────────────
    public double ChinLat   { get; set; }
    public double ChinAnt   { get; set; }
    public double ChinVert  { get; set; }
    public double ChinRoll  { get; set; }
    public double ChinPitch { get; set; }
    public double ChinYaw   { get; set; }

    // ── Saved jaw backups (for mode-switch zero/restore) ──
    public double SavedMaxillaLat   { get; set; }
    public double SavedMaxillaAnt   { get; set; }
    public double SavedMaxillaVert  { get; set; }
    public double SavedMaxillaRoll  { get; set; }
    public double SavedMaxillaPitch { get; set; }
    public double SavedMaxillaYaw   { get; set; }

    public double SavedMandibleLat   { get; set; }
    public double SavedMandibleAnt   { get; set; }
    public double SavedMandibleVert  { get; set; }
    public double SavedMandibleRoll  { get; set; }
    public double SavedMandiblePitch { get; set; }
    public double SavedMandibleYaw   { get; set; }
}
