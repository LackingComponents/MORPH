using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using HelixToolkit.Wpf.SharpDX;
using OrthoPlanner.App.ViewModels;
using OrthoPlanner.Core.Geometry;

namespace OrthoPlanner.App;

// ── Shell item (shown in right panel) ─────────────────────────────────────────

public class ShellItem : ObservableObject
{
    private bool _isVisible = true;

    public string Label        { get; set; } = "";
    public int    TriangleCount { get; set; }
    public string TriangleCountText => $"{TriangleCount:N0} triangles";

    /// <summary>Underlying triangle soup for this shell.</summary>
    public List<float[]> Verts { get; set; } = new();

    /// <summary>3-D model displayed in the viewport for this shell.</summary>
    public MeshGeometryModel3D? Model3D { get; set; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value) && Model3D != null)
                Model3D.Visibility = value ? Visibility.Visible : Visibility.Hidden;
        }
    }
}

// ── DentalCastEditorWindow ─────────────────────────────────────────────────────

/// <summary>
/// Interactive dental cast editor.  Allows slicing (clip plane), hole-closing,
/// shell separation and management.  Returns the edited model(s) back to the
/// main viewport on Accept while keeping originals in memory.
/// </summary>
public partial class DentalCastEditorWindow : Window
{
    // ── Public result ──────────────────────────────────────────────────────────

    /// <summary>Set of models that were actually changed and should replace the originals in the main viewport.</summary>
    public Dictionary<string, List<float[]>> EditedMeshes { get; } = new();
    public bool Accepted { get; private set; }

    // ── Model state ────────────────────────────────────────────────────────────

    // One entry per imported scan: name → (original verts, current working verts)
    private readonly List<(string Name, List<float[]> Original, List<float[]> Working, byte R, byte G, byte B)> _models = new();
    private int _currentModelIndex = -1;

    // Working shells for current model
    private readonly ObservableCollection<ShellItem> _shells = new();

    // Clip plane visual
    private MeshGeometryModel3D _planeMesh = new() { CullMode = SharpDX.Direct3D11.CullMode.None };
    private LineGeometryModel3D _planeLine = new();
    private GroupModel3D        _planeGroup = new();

    // Undo stack (per model) — just stores the previous working verts list
    private List<float[]>? _undoSnapshot;

    // Clip plane world bounds (set when a model is loaded)
    private double _clipMin, _clipMax;        // range along current axis
    private int    _clipAxis = 2;             // 0=X, 1=Y, 2=Z

    private EventHandler? _renderHandler;

    // ── Constructor ────────────────────────────────────────────────────────────

    public DentalCastEditorWindow(IEnumerable<(string Name, float[] FlatVerts, byte R, byte G, byte B)> meshes)
    {
        InitializeComponent();
        MainViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();

        // Head-lamp follows camera
        _renderHandler = (_, _) =>
        {
            var d = SubCamera.LookDirection;
            if (d.Length > 0.001) { d.Normalize(); Headlamp.Direction = new(-d.X,-d.Y,-d.Z); Backlamp.Direction = new(d.X,d.Y,d.Z); }
        };
        CompositionTarget.Rendering += _renderHandler;

        // Add visual groups
        MainGroup.Children.Add(_planeGroup);
        _planeGroup.Children.Add(_planeMesh);
        _planeGroup.Children.Add(_planeLine);

        // Wire shells list
        ShellList.ItemsSource = _shells;

        // Load models
        foreach (var (name, flat, r, g, b) in meshes)
        {
            var verts = MeshHelper.ToVertexList(flat);
            _models.Add((name, new List<float[]>(verts), new List<float[]>(verts), r, g, b));
            ModelSelector.Items.Add(name);
        }

        // Cleanup
        Closed += (_, _) =>
        {
            if (_renderHandler != null) { CompositionTarget.Rendering -= _renderHandler; _renderHandler = null; }
            MainGroup.Children.Clear();
            _planeGroup.Children.Clear();
            if (MainViewport.EffectsManager is IDisposable d) d.Dispose();
            MainViewport.EffectsManager = null;
        };

        // Select first model
        if (_models.Count > 0) ModelSelector.SelectedIndex = 0;
    }

    // ── Model selector ─────────────────────────────────────────────────────────

    private void ModelSelector_SelectionChanged(object s, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        int idx = ModelSelector.SelectedIndex;
        if (idx < 0 || idx >= _models.Count) return;
        _currentModelIndex = idx;
        _undoSnapshot = null;
        UndoClipBtn.IsEnabled = false;
        LoadCurrentModelAsShells();
        SetStatus("Model loaded — ready to edit.");
    }

    private void LoadCurrentModelAsShells()
    {
        if (_currentModelIndex < 0) return;
        var (_, _, working, _, _, _) = _models[_currentModelIndex];

        ClearShells();

        // Treat the working verts as a single shell initially
        AddShell("Model", working, 0);
        UpdateClipRange();
        FitCamera(working);
    }

    // ── Shell helpers ──────────────────────────────────────────────────────────

    private void ClearShells()
    {
        foreach (var sh in _shells)
            if (sh.Model3D != null) MainGroup.Children.Remove(sh.Model3D);
        _shells.Clear();
        UpdateShellStats();
    }

    private void AddShell(string baseName, List<float[]> verts, int index)
    {
        var (_, _, _, r, g, b) = _models[_currentModelIndex];
        // Tint shells slightly differently so they are distinguishable
        float tint = index == 0 ? 1f : 0.65f + (index % 5) * 0.07f;
        var col = Color.FromRgb(
            (byte)Math.Clamp(r * tint, 0, 255),
            (byte)Math.Clamp(g * tint, 0, 255),
            (byte)Math.Clamp(b * tint, 0, 255));

        var m3d = MakeMesh(verts, col, 1.0);
        MainGroup.Children.Add(m3d);

        var shell = new ShellItem
        {
            Label         = $"{baseName} #{index + 1}",
            TriangleCount = verts.Count / 3,
            Verts         = verts,
            Model3D       = m3d,
            IsVisible     = true
        };
        _shells.Add(shell);
        UpdateShellStats();
    }

    private void UpdateShellStats()
    {
        int total = _shells.Sum(s => s.TriangleCount);
        ShellStatsText.Text = $"{_shells.Count} shell(s), {total:N0} triangles total";
    }

    // ── Clip Plane ─────────────────────────────────────────────────────────────

    private void ShowClipPlane_Changed(object s, RoutedEventArgs e)
        => _planeGroup.Visibility = ShowClipPlaneCheck.IsChecked == true ? Visibility.Visible : Visibility.Hidden;

    private void ClipAxis_Changed(object sender, RoutedEventArgs e)
    {
        _clipAxis = AxisX.IsChecked == true ? 0 : AxisY.IsChecked == true ? 1 : 2;
        UpdateClipRange();
    }

    private void UpdateClipRange()
    {
        if (_currentModelIndex < 0 || _shells.Count == 0) return;
        var verts = GetMergedCurrentVerts();
        if (verts.Count == 0) return;

        double mn = double.MaxValue, mx = double.MinValue;
        foreach (var v in verts)
        {
            double val = _clipAxis == 0 ? v[0] : _clipAxis == 1 ? v[1] : v[2];
            if (val < mn) mn = val;
            if (val > mx) mx = val;
        }
        _clipMin = mn;
        _clipMax = mx;
        RebuildClipPlane();
    }

    private void ClipPos_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
        => RebuildClipPlane();

    private void RebuildClipPlane()
    {
        if (_clipMax <= _clipMin) return;
        double pos = _clipMin + ClipPosSlider.Value * (_clipMax - _clipMin);
        ClipPosLabel.Text = $"{pos:F2} mm";

        // Build a thin quad perpendicular to the current axis
        float pad = (float)((_clipMax - _clipMin) * 0.6);
        var mb = new HelixToolkit.Geometry.MeshBuilder();

        System.Numerics.Vector3 A, B, C, D;
        if (_clipAxis == 0)      // X-plane
        {
            float x = (float)pos;
            float y0 = (float)_clipMin - pad, y1 = (float)_clipMax + pad;
            float z0 = (float)_clipMin - pad, z1 = (float)_clipMax + pad;
            A = new(x, y0, z0); B = new(x, y1, z0); C = new(x, y1, z1); D = new(x, y0, z1);
        }
        else if (_clipAxis == 1) // Y-plane
        {
            float y = (float)pos;
            float x0 = (float)_clipMin - pad, x1 = (float)_clipMax + pad;
            float z0 = (float)_clipMin - pad, z1 = (float)_clipMax + pad;
            A = new(x0, y, z0); B = new(x1, y, z0); C = new(x1, y, z1); D = new(x0, y, z1);
        }
        else                    // Z-plane
        {
            float z = (float)pos;
            float x0 = (float)_clipMin - pad, x1 = (float)_clipMax + pad;
            float y0 = (float)_clipMin - pad, y1 = (float)_clipMax + pad;
            A = new(x0, y0, z); B = new(x1, y0, z); C = new(x1, y1, z); D = new(x0, y1, z);
        }

        mb.AddTriangle(A, B, C); mb.AddTriangle(A, C, D);
        mb.AddTriangle(A, C, B); mb.AddTriangle(A, D, C); // double-sided
        _planeMesh.Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(mb.ToMesh());
        _planeMesh.Material  = new PhongMaterial
        {
            DiffuseColor  = new(0.2f, 0.7f, 1f, 0.18f),
            EmissiveColor = new(0.1f, 0.5f, 0.9f, 0.12f)
        };

        // Outline
        var lb = new HelixToolkit.SharpDX.LineBuilder();
        lb.AddLine(A, B); lb.AddLine(B, C); lb.AddLine(C, D); lb.AddLine(D, A);
        _planeLine.Geometry = lb.ToLineGeometry3D();
        _planeLine.Color    = Colors.Cyan;
        _planeLine.Thickness = 1.5;
    }

    // Apply clip ───────────────────────────────────────────────────────────────

    private async void ApplyClip_Click(object s, RoutedEventArgs e)
    {
        if (_currentModelIndex < 0 || _shells.Count == 0) return;

        SetStatus("Clipping...");
        Cursor = Cursors.Wait;
        ApplyClipBtn.IsEnabled = false;

        // Snapshot for undo (merge all shells at this moment)
        _undoSnapshot = new List<float[]>(GetMergedCurrentVerts());

        double pos    = _clipMin + ClipPosSlider.Value * (_clipMax - _clipMin);
        bool keepAbove = KeepAbove.IsChecked == true;

        // Normal vector along axis
        double nx = _clipAxis == 0 ? 1 : 0;
        double ny = _clipAxis == 1 ? 1 : 0;
        double nz = _clipAxis == 2 ? 1 : 0;
        double d  = -pos;   // plane: n·p + d = 0 → n·x = pos

        try
        {
            // Clip each visible shell independently
            var newShells = new List<List<float[]>>();
            foreach (var sh in _shells.Where(sh => sh.IsVisible))
            {
                var verts = sh.Verts;
                var (above, below) = await System.Threading.Tasks.Task.Run(()
                    => MeshOps.TrueSliceByPlane(verts, nx, ny, nz, d, capEnds: true));
                var kept = keepAbove ? above : below;
                if (kept.Count >= 3) newShells.Add(kept);
            }
            // Keep invisible shells as-is
            foreach (var sh in _shells.Where(sh => !sh.IsVisible))
                newShells.Add(sh.Verts);

            if (newShells.Count == 0) { SetStatus("Clip produced empty result — nothing changed."); return; }

            // Flatten back to single shell (can always split again)
            var merged = newShells.SelectMany(v => v).ToList();
            UpdateWorkingVerts(merged);
            ClearShells();
            AddShell("Clipped", merged, 0);
            UpdateClipRange();
            UndoClipBtn.IsEnabled = true;
            SetStatus($"Clip applied — {merged.Count / 3:N0} triangles remain.");
        }
        catch (Exception ex)
        {
            SetStatus($"Clip failed: {ex.Message}");
        }
        finally
        {
            ApplyClipBtn.IsEnabled = true;
            Cursor = Cursors.Arrow;
        }
    }

    private void UndoClip_Click(object s, RoutedEventArgs e)
    {
        if (_undoSnapshot == null) return;
        UpdateWorkingVerts(_undoSnapshot);
        ClearShells();
        AddShell("Restored", _undoSnapshot, 0);
        UpdateClipRange();
        _undoSnapshot = null;
        UndoClipBtn.IsEnabled = false;
        SetStatus("Undo applied — previous state restored.");
    }

    // Close surfaces ───────────────────────────────────────────────────────────

    private async void CloseSurfaces_Click(object s, RoutedEventArgs e)
    {
        if (_currentModelIndex < 0 || _shells.Count == 0) return;
        SetStatus("Closing open surfaces..."); Cursor = Cursors.Wait;

        var snap = GetMergedCurrentVerts();
        _undoSnapshot = new List<float[]>(snap);

        try
        {
            var closed = await System.Threading.Tasks.Task.Run(() => MeshOps.CloseHoles(snap));
            UpdateWorkingVerts(closed);
            ClearShells();
            AddShell("Closed", closed, 0);
            SetStatus($"Holes filled — {closed.Count / 3:N0} triangles.");
            UndoClipBtn.IsEnabled = false; // undo-slot now stale, clear it
        }
        catch (Exception ex) { SetStatus($"Close failed: {ex.Message}"); }
        finally { Cursor = Cursors.Arrow; }
    }

    // Split shells ─────────────────────────────────────────────────────────────

    private async void SplitShells_Click(object s, RoutedEventArgs e)
    {
        if (_currentModelIndex < 0 || _shells.Count == 0) return;
        SetStatus("Splitting into connected shells..."); Cursor = Cursors.Wait;

        var merged = GetMergedCurrentVerts();
        try
        {
            var components = await System.Threading.Tasks.Task.Run(()
                => MeshOps.LabelConnectedComponents(merged));

            ClearShells();
            for (int i = 0; i < components.Count; i++)
                AddShell("Shell", components[i], i);

            SetStatus($"Found {components.Count} shell(s).");
        }
        catch (Exception ex) { SetStatus($"Split failed: {ex.Message}"); }
        finally { Cursor = Cursors.Arrow; }
    }

    private void KeepSelectedShell_Click(object s, RoutedEventArgs e)
    {
        var sel = ShellList.SelectedItem as ShellItem;
        if (sel == null) { SetStatus("No shell selected."); return; }
        var kept = sel.Verts;
        UpdateWorkingVerts(kept);
        ClearShells();
        AddShell("Kept", kept, 0);
        SetStatus($"Kept selected shell — {kept.Count / 3:N0} triangles.");
    }

    private void RemoveSelectedShell_Click(object s, RoutedEventArgs e)
    {
        var sel = ShellList.SelectedItem as ShellItem;
        if (sel == null) { SetStatus("No shell selected."); return; }
        if (sel.Model3D != null) MainGroup.Children.Remove(sel.Model3D);
        _shells.Remove(sel);
        // Rebuild working verts from remaining shells
        var merged = GetMergedCurrentVerts();
        UpdateWorkingVerts(merged);
        SetStatus($"Shell removed — {merged.Count / 3:N0} triangles remain.");
        UpdateShellStats();
    }

    private void MergeShells_Click(object s, RoutedEventArgs e)
    {
        if (_shells.Count == 0) return;
        var merged = GetMergedCurrentVerts();
        UpdateWorkingVerts(merged);
        ClearShells();
        AddShell("Merged", merged, 0);
        SetStatus($"All shells merged — {merged.Count / 3:N0} triangles.");
    }

    // Shell visibility ─────────────────────────────────────────────────────────

    private void ShellVisibility_Changed(object s, RoutedEventArgs e)
    {
        // Handled via data binding on ShellItem.IsVisible
    }

    private void ShellList_SelectionChanged(object s, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Highlight selected shell (give it a slight emissive tint)
        foreach (var sh in _shells)
            if (sh.Model3D?.Material is PhongMaterial pm)
                pm.EmissiveColor = new(0, 0, 0, 1);

        if (ShellList.SelectedItem is ShellItem sel && sel.Model3D?.Material is PhongMaterial selPm)
            selPm.EmissiveColor = new(0.08f, 0.18f, 0.35f, 1);
    }

    // ── Fit view ───────────────────────────────────────────────────────────────

    private void FitView_Click(object s, RoutedEventArgs e)
    {
        var v = GetMergedCurrentVerts();
        if (v.Count > 0) FitCamera(v);
    }

    // ── Footer buttons ─────────────────────────────────────────────────────────

    private void Revert_Click(object s, RoutedEventArgs e)
    {
        if (_currentModelIndex < 0) return;
        var (name, original, _, r, g, b) = _models[_currentModelIndex];
        var reverted = new List<float[]>(original);
        _models[_currentModelIndex] = (name, original, reverted, r, g, b);
        _undoSnapshot = null;
        UndoClipBtn.IsEnabled = false;
        ClearShells();
        AddShell("Original", reverted, 0);
        UpdateClipRange();
        SetStatus("Reverted to original — all edits discarded.");
    }

    private void Apply_Click(object s, RoutedEventArgs e)
    {
        // Collect edited meshes for all models where working ≠ original
        EditedMeshes.Clear();
        foreach (var (name, original, working, _, _, _) in _models)
        {
            // Consider it changed if verts count differs OR it's not the exact same list reference
            if (!ReferenceEquals(working, original) || working.Count != original.Count)
                EditedMeshes[name] = working;
        }
        if (EditedMeshes.Count == 0)
        {
            // Even if nothing changed vs original, send back the working mesh
            var (name, _, working, _, _, _) = _models[_currentModelIndex];
            EditedMeshes[name] = working;
        }
        Accepted = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object s, RoutedEventArgs e) { DialogResult = false; Close(); }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Merge all currently displayed shells into one triangle-soup list.</summary>
    private List<float[]> GetMergedCurrentVerts()
        => _shells.SelectMany(sh => sh.Verts).ToList();

    private void UpdateWorkingVerts(List<float[]> verts)
    {
        if (_currentModelIndex < 0) return;
        var (name, original, _, r, g, b) = _models[_currentModelIndex];
        _models[_currentModelIndex] = (name, original, verts, r, g, b);
    }

    private void SetStatus(string msg) => StatusText.Text = msg;

    private void FitCamera(List<float[]> verts)
    {
        if (verts == null || verts.Count == 0) return;
        if (MainViewport.Camera is not HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam) return;
        double mnX=9e9,mnY=9e9,mnZ=9e9,mxX=-9e9,mxY=-9e9,mxZ=-9e9;
        foreach (var v in verts)
        {
            if(v[0]<mnX)mnX=v[0]; if(v[0]>mxX)mxX=v[0];
            if(v[1]<mnY)mnY=v[1]; if(v[1]>mxY)mxY=v[1];
            if(v[2]<mnZ)mnZ=v[2]; if(v[2]>mxZ)mxZ=v[2];
        }
        var c  = new Point3D((mnX+mxX)/2,(mnY+mxY)/2,(mnZ+mxZ)/2);
        double dist = Math.Sqrt(Math.Pow(mxX-mnX,2)+Math.Pow(mxY-mnY,2)+Math.Pow(mxZ-mnZ,2)) * 1.1;
        cam.Position = new(c.X, c.Y - dist, c.Z + dist * 0.3);
        cam.LookDirection = new(0, dist, -dist * 0.3);
        cam.UpDirection   = new(0, 0, 1);
        MainViewport.FixedRotationPointEnabled = true;
        MainViewport.FixedRotationPoint = c;
    }

    private MeshGeometryModel3D MakeMesh(List<float[]> verts, Color col, double opacity)
    {
        var b = new HelixToolkit.Geometry.MeshBuilder();
        for (int i = 0; i < verts.Count; i += 3)
            if (i + 2 < verts.Count) b.AddTriangle(V3(verts[i]), V3(verts[i+1]), V3(verts[i+2]));
        return new MeshGeometryModel3D
        {
            Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(b.ToMesh()),
            Material = new PhongMaterial
            {
                DiffuseColor      = new(col.R/255f, col.G/255f, col.B/255f, (float)opacity),
                SpecularColor     = new(0.3f, 0.3f, 0.3f, 1f),
                SpecularShininess = 24f
            }
        };
    }

    private static System.Numerics.Vector3 V3(float[] v) => new(v[0], v[1], v[2]);
}
