# MORPH / OrthoPlanner — LLM Working Context

> **READ THIS FIRST** before touching any code in this repository.  
> This document is the authoritative reference for AI assistants working on this project.  
> Keep it updated whenever the architecture changes significantly.

---

## 1. What Is This Project?

**MORPH** is an open-source, Windows desktop orthognathic surgery planning application built with **C# / WPF / .NET 8**.  
Target users: maxillofacial surgeons and orthodontists.  
Primary developer: a surgeon (non-programmer) — every AI response must be explicit and pedagogical.

**Repo:** `https://github.com/LackingComponents/MORPH`  
**Solution file:** `OrthoPlanner.sln`  
**Local path (Mirko's machine):** `C:\Users\Mirko\Documents\Orthoplanner`

---

## 2. Solution Layout

```
OrthoPlanner.sln
├── src/OrthoPlanner.App         → WPF UI layer (Views, ViewModels, Windows)
├── src/OrthoPlanner.Core        → Business logic (engines, loaders, data models)
└── src/OrthoPlanner.Infrastructure → DICOM / DRR generation
```

### Key NuGet packages

| Package | Purpose |
|---|---|
| `HelixToolkit.Wpf.SharpDX` 3.1.2 | 3D rendering (DirectX-backed via SharpDX) |
| `fo-dicom` (FellowOakDicom) | DICOM loading and codec transcoding |
| `CommunityToolkit.Mvvm` 8.4.0 | MVVM: `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]` |
| `ManifoldNET` 1.0.7-alpha | Boolean mesh operations (requires `manifoldc.dll` + `tbb12.dll` native DLLs in output) |
| ILGPU | GPU acceleration (files from collaborator fork) |

> ⚠️ ManifoldNET requires `Prefer32Bit=false` in the csproj and the two native DLLs must be copied to the build output. This is already configured.

---

## 3. Application Startup Flow

`App.xaml.cs → OnStartup()`

1. Show `SplashWindow` immediately.
2. Register fo-dicom codecs on a background thread (`DicomSetupBuilder`).
3. Call `AppTempStorage.Initialize()` — creates/clears `%TEMP%\OrthoPlanner\`.
4. Register a global `Window.Loaded` handler that applies **dark window chrome** via `DwmSetWindowAttribute` (attributes 19, 20, 34, 35, 36). Every window opened later gets this automatically.
5. Register a `SystemEvents.PowerModeChanged` handler — on Sleep, all `EffectsManager` instances are disposed to avoid DirectX deadlocks; on Resume they are recreated.
6. Open `MainWindow`, close splash.

---

## 4. The MVVM Architecture

### 4.1 The Partial-Class Pattern

`MainViewModel` is a **single class split across many files** using C# `partial class`. This is the only way CommunityToolkit.Mvvm source generators work across files.

**Every partial file adds properties and commands to the same `MainViewModel` object.**  
There is only one `MainViewModel` instance, created by `MainWindow` (set as `DataContext`).

| File | Responsibility |
|---|---|
| `ViewModels/MainViewModel.cs` | Class declaration, shared helpers (`RefreshCombinedModel`, `MeshHelper`, `SegmentViewModel`, `MeshViewModel`) |
| `ViewModels/DicomViewModel.cs` | DICOM loading, MPR slice rendering, window/level, histograms, NHP physical reslice |
| `ViewModels/SegmentationViewModel.cs` | Segmentation pipeline, region growing, mesh generation |
| `ViewModels/OsteotomyViewModel.cs` | Osteotomy wizard launchers (LeFort 1, BSSO, Genioplasty, Condyle Split) |
| `ViewModels/SurgeryViewModel.cs` | Per-segment 6-DOF surgical movements, occlusion STL management, plan tree |
| `ViewModels/StlViewModel.cs` | STL import, dental alignment, merge, export |
| `ViewModels/ProjectViewModel.cs` | Save/Load `.orthoplan` ZIP archives |
| `ViewModels/NhpViewModel.cs` | Natural Head Position alignment (visual preview + commit/reslice) |
| `ViewModels/UndoRedoViewModel.cs` | 5-level undo stack (segment/mesh snapshots only) |
| `ViewModels/ViewportViewModel.cs` | Camera anchors, lighting, MPR toggles, orthographic mode |
| `ViewModels/VolumeRenderingViewModel.cs` | Volume rendering toggle |
| `ViewModels/NhpViewModel.cs` | NHP delta transform logic |
| `ViewModels/SplintViewModel.cs` | Splint planner state |
| `ViewModels/OcclusionPlanViewModel.cs` | Per-occlusion surgical plan snapshots |

### 4.2 Key Collections on MainViewModel

```csharp
ObservableCollection<SegmentViewModel>  Segments        // All bone/tissue segments from CT
ObservableCollection<MeshViewModel>     ImportedMeshes  // Imported STL dental casts
ObservableCollection<MeshViewModel>     LoadedOcclusions // Occlusion STL files
ObservableCollection<OcclusionNodeViewModel> OcclusionNodes // Plan tree nodes
```

### 4.3 Helper ViewModel Classes (defined in MainViewModel.cs)

**`SegmentViewModel`** — represents one segmented tissue:
- `Label` (byte): unique ID in `SegmentationVolume.Labels[]`
- `Name` (string): determines surgical roles (see §8)
- `Vertices` (float[]): flat array, stride 3 (x,y,z per triangle vertex — 9 floats per triangle)
- `Geometry`, `Material`: HelixToolkit SharpDX objects for 3D rendering
- `Transform`: WPF `Transform3D` = NHP transform composed with surgical transform
- `SurgicalTransform`: just the surgical movement, NHP-independent
- `BuildModel()`: converts `Vertices` → HelixToolkit geometry + Phong material

**`MeshViewModel`** — represents an imported STL file (dental casts, occlusion):
- Same vertex/geometry pattern as `SegmentViewModel`
- `ScanType`: `Upper`, `Lower`, or `Other` (DentalScanType enum)
- `MaxillaOcclusionTransform`, `MandibleOcclusionTransform`: Matrix3D (row-vector convention)

**`MeshHelper`** (static):
- `ToVertexList(float[])` / `ToFlatArray(List<float[]>)`: convert between stride-3 flat array and `List<float[3]>` (many older windows use the list form)
- `BuildModel3D(...)`: creates `MeshGeometryModel3D` with Phong material from vertices

---

## 5. Core Data Structures

### 5.1 VolumeData (`OrthoPlanner.Core.Imaging`)

Stores the DICOM CT scan as a flat `short[]` array of Hounsfield Units (HU).

```
Voxels[x + y*Width + z*Width*Height]   // flat index formula — memorize this
Spacing[0] = X spacing in mm (column)
Spacing[1] = Y spacing in mm (row)  
Spacing[2] = Z spacing in mm (slice)
```

Key methods:
- `GetVoxel(x, y, z)` / `SetVoxel(x, y, z, value)` — boundary-safe
- `GetAxialSlice(z, wc, ww)` → `byte[]` grayscale
- `GetAxialSliceBgra(z, wc, ww, min, max)` → `byte[]` BGRA32 with blue threshold overlay
- `GetAxialSliceWithMaskBgra(z, wc, ww, segVol)` → `byte[]` BGRA32 with label color blending
- Coronal/Sagittal equivalents (Z is always reversed so superior = top of image)
- `GetPanoramicMIPBgra(...)` — curved dental panoramic MPR
- `Histogram` (int[512]), `HistogramMax` — computed after `ComputeMinMax()`

### 5.2 SegmentationVolume (`OrthoPlanner.Core.Segmentation`)

Parallel label array that matches `VolumeData` dimensions:
```
Labels[x + y*Width + z*Width*Height]  // same indexing as VolumeData.Voxels
// 0 = unlabeled, 1..255 = segment label IDs
```
- `Segments` dictionary: `byte label → SegmentInfo` (Name, ColorR/G/B)
- `AddSegment(SegmentInfo)`, `ClearLabel(byte)`, `ClearAll()`
- `CountVoxels(byte)` — count labeled voxels
- `GetLabel/SetLabel(x, y, z)` — boundary-safe accessors

### 5.3 AppTempStorage (`OrthoPlanner.Core`)

Simple static helper:
- `TempDirectory` = `%TEMP%\OrthoPlanner\`
- `Initialize()` — called at startup, clears old files
- `GetTempFilePath(ext)` — generates a unique temp file path

---

## 6. The DICOM Loading Flow

```
MainViewModel.OpenDicomFolderAsync()
  └─ DicomLoader.ScanFolderAsync(folderPath)     → List<DicomSeriesInfo>
  └─ DicomSelectorWindow (shows series list)
  └─ DicomLoader.LoadSeriesAsync(filePaths)      → VolumeData
  └─ Volume assigned, UI state initialized
  └─ UpdateAllSlices() + UpdateHistograms() + RefreshCombinedModel()
```

On load, the following defaults are set:
- `WindowCenter = 400, WindowWidth = 2000` (bone window)
- `IsoMin = -1000, IsoMax = Volume.MaxValue`
- Slice indices set to volume center
- `AxialDisplayHeight` etc. set in `GridLength Star` units proportional to physical mm dimensions (ensures anatomically correct aspect ratios)

---

## 7. The Segmentation Pipeline

Called from `SegmentationViewModel.RunSegmentInternalAsync()`.

```
1. ThresholdSegment()          → labels voxels in HU range
   (+ enhanceThinBone if Bone) → internal-air-touching partial voxels included
2. RemoveSmallComponents()     → if EnhanceThinBone=true, remove scatter < 50 voxels
3. MorphologicalClosing()      → dilation+erosion (1 or 2 iterations)
4. KeepLargestComponent()      → or KeepLargestComponents(N) or KeepTopPercentage(30%)
5. SmoothLabelMask()           → 3x3x3 majority vote (14/27 threshold)
6. ExtractSegmentMesh()        → Marching Cubes → flat float[] vertices
7. BuildModel()                → HelixToolkit geometry created
8. Segment added to Segments collection
```

After Bone segmentation, `_boneOnlySegVolume` is populated — a pristine backup used only by the Cranium/Mandible split. It must not be overwritten by subsequent segmentations.

**Live 3D preview** (`TriggerLivePreviewUpdate`): debounced 150ms async preview using step=1 (full res) marching cubes at the current bone HU range. Only active when `ShowBoneOverlay = true`.

**Region growing mode** (`IsRegionGrowMode`): user clicks on MPR slices to place seeds. Seeds are stored in `MultiSeeds` collection. `AddSeedPointAsync` fires `CompetitiveRegionGrow` and calls `UpdateAllSlices()` to show the current label overlay on MPR images.

---

## 8. Segment Naming Conventions — CRITICAL

Surgery transforms, osteotomy wizards, and occlusion alignment all **look up segments by name** using string Contains/StartsWith checks. The names are contractual:

| Segment Name | Role |
|---|---|
| `"Bone"` or starts with `"Bone"` | → `HardTissueModel` (the primary bone surface) |
| `"Soft Tissue"` or starts with `"Soft Tissue"` | → `SoftTissueModel` |
| `"Dental Scan"` or starts with `"Dental"` | → `DentalModel` |
| `"Cranium (Split)"` | Created by Condyle Split wizard |
| `"Mandible (Split)"` | Created by Condyle Split wizard |
| `"Cranium (LeFort Upper)"` | Created by LeFort 1 wizard (upper bone above cut) |
| `"Maxilla (LeFort 1 Separated)"` | Created by LeFort 1 wizard (moveable maxilla) |
| `"Maxilla Left (2-Piece)"` / `"Maxilla Right (2-Piece)"` | LeFort 1 sagittal cut |
| `"Maxilla Left (3-Piece)"` / `"Maxilla Right (3-Piece)"` / `"Maxilla Central / Premaxilla (3-Piece)"` | LeFort 1 Y-cut |
| `"Ramus Left"` / `"Ramus Right"` | Created by BSSO wizard (proximal = condyle-bearing) |
| `"Mandible"` | Created by BSSO wizard (distal = teeth-bearing) |
| `"Chin Segment"` | Created by Genioplasty wizard |

`UpdateSurgeryTransform()` matches segments via:
```csharp
var maxilla    = Segments.LastOrDefault(s => s.Name.Contains("Maxilla") && s.IsVisible);
var mandible   = Segments.LastOrDefault(s => s.Name.Contains("Mandible") && !s.Name.Contains("Cranium") && !s.Name.StartsWith("Ramus") && s.IsVisible);
var rightRamus = Segments.LastOrDefault(s => s.Name.Contains("Ramus Right") && s.IsVisible);
var leftRamus  = Segments.LastOrDefault(s => s.Name.Contains("Ramus Left") && s.IsVisible);
var chin       = Segments.LastOrDefault(s => s.Name.Contains("Chin") && s.IsVisible);
```

**Do not rename segments without updating ALL callers.**

---

## 9. The Transform Stack

Every segment has two independent transforms that compose:

```
seg.Transform = ComposeTransforms(_nhpTransform, seg.SurgicalTransform)
```

- `_nhpTransform`: the current uncommitted NHP delta (visual-only, not baked). Applied uniformly to all segments and imported meshes.
- `seg.SurgicalTransform`: the per-segment surgical movement (jaw slides, rotations).

`ComposeTransforms(first, second)` builds a `Transform3DGroup` — first is applied before second.

Pivot points for surgical movements:
- Maxilla / Mandible: `DentalMidlinePoint` (set by Condyle Split wizard) or `ModelCenter` as fallback
- Right Ramus: `RightCondyleCenter` or `ModelCenter`
- Left Ramus: `LeftCondyleCenter` or `ModelCenter`
- Chin: centroid of chin vertices

**CRITICAL translation sign convention:** In `BuildSurgeryTransform`:
```csharp
// Invert ant so positive values push forward (-ant)
group.Children.Add(new TranslateTransform3D(center.X + lat, center.Y - ant, center.Z + vert));
```
The Y axis is inverted for the anteroposterior direction. Do not change this.

---

## 10. NHP (Natural Head Position) Alignment

NHP works in two modes:

**Mode A — Visual preview (before Commit):**  
`NhpPitch/Roll/Yaw/Lateral/Anteroposterior/Vertical` sliders update `_nhpTransform` in real time.  
This is purely a visual transform — the underlying `Voxels` and `Vertices` data is unchanged.  
`IsNhpDirty` = true whenever any slider differs from its last-committed baseline (`_cPitch`, etc.).

**Mode B — Commit (`CommitNhpAsync`):**  
1. Captures the delta between current UI values and last committed baseline.
2. Calls `PerformPhysicalResliceAsync(delta...)` which:
   - Reslices `OriginalVolume` (the first CT loaded) through `SegmentationEngine.ResliceVolume()`, producing a new `VolumeData` physically in the NHP orientation.
   - Mutates all segment and mesh `Vertices` in-place by the delta matrix.
   - Rebuilds all 3D models.
   - Resets all Transform3D to Identity (vertices are now physically at the correct position).
   - Updates `BoneOnlyBounds` and `ModelCenter`.
   - Resets `_segVolume` to a fresh `SegmentationVolume` for the new dimensions.
3. The committed state becomes the new baseline (`_cXxx = NhpXxx`).

---

## 11. The Surgical Planning Windows (Wizard Pattern)

Each osteotomy is handled by a dedicated `Window` class that receives input mesh vertices, lets the user place/adjust a cut plane, and returns result meshes via public properties.

| Command | Wizard Class | Input | Output Properties |
|---|---|---|---|
| `PlanLeFort1` | `LeFortOsteotomyWindow` | Cranium vertices | `UpperMaxillaResult`, `LowerMaxillaResult` |
| `PlanLeFort1SagittalCut` | `LeFort1SagittalCutWindow` | LeFort 1 Separated maxilla | `LeftResult`, `RightResult` |
| `PlanLeFort1YCut` | `LeFort1YCutWindow` | LeFort 1 Separated maxilla | `LeftResult`, `RightResult`, `CentralResult` |
| `PlanBsso` | `BssoOsteotomyWindow` | Mandible vertices | `ProximalResult` (Ramus), `DistalResult` (Mandible) |
| `PlanGenioplasty` | `GenioplastyOsteotomyWindow` | Mandible vertices | `UpperMandibleResult`, `ChinSegmentResult` |
| `SplitCraniumMandibleAsync` | `CondyleSplitWindow` | Full bone mesh + segvol | `CraniumResult`, `MandibleResult`, `LeftCondyleCenter`, `RightCondyleCenter`, `DentalMidlinePoint` |

Pattern for calling a wizard:
```csharp
var wizard = new XxxWindow(MeshHelper.ToVertexList(inputSegment.Vertices));
wizard.Owner = Application.Current.MainWindow;
if (wizard.ShowDialog() == true && wizard.Accepted)
{
    SaveStateForUndo();
    // hide original, add new segments from wizard results
    RefreshCombinedModel();
}
```

All wizard windows own their own `Viewport3DX` with a dedicated `EffectsManager`.  
They are heavy — always call `GC.Collect()` after closing `CondyleSplitWindow`.

---

## 12. Occlusion STL Alignment Flow

The occlusion STL workflow links dental casts to the CT bone segments to define the surgical bite.

**Automated alignment (`AlignOcclusions`):**
1. ICP 1: Align Occlusion STL → Maxilla (500 iterations, 40%/50% cull)
2. ICP 2: Align CT Mandible → ICP-aligned Occlusion (500 iterations, 50%/20% cull)
3. Show `OcclusionCheckerWindow` for review.
4. If accepted:
   - `occlusion.Transform` = ICP 1 result (visual position in the scene)
   - `occlusion.MandibleOcclusionTransform` = ICP 2 result (encodes "how far mandible is from planned occlusion")

**Manual alignment (`ManualOcclusionAlignmentWindow`):**
- User places corresponding landmark pairs on bone viewport and occlusion viewport
- `IcpAligner.ComputeLandmarkTransform()` → initial rigid alignment from 3+ point pairs
- `IcpAligner.Align()` for fine refinement
- Result: `MaxillaTransform`, `MandibleTransform` (double[4,4])
- After acceptance, `IcpAligner.TransformVertices(occlusion.Vertices, ...)` physically moves the STL

**Matrix convention warning:**  
`IcpAligner` uses **column-vector convention** (`double[4,4]`).  
WPF `Matrix3D` uses **row-vector convention**.  
`ToDoubleMatrix(Matrix3D m)` and `ConvertToMatrix3D(double[,] m)` in `SurgeryViewModel` handle this conversion — do not transpose manually.

---

## 13. The Cephalometry Module

`CephalometryOverlay` is a `UserControl` embedded in `MainWindow` that toggles visible via `IsCephalometryOpen`.

It operates in two modes toggled by `Ctrl+D`:
- **2D mode**: renders a DRR (Digitally Reconstructed Radiograph) — Lateral or PA — on which the user places 2D landmark dots.
- **3D mode**: hides the DRR, makes the shared `MainViewport` (HelixToolkit) transparent, and places landmark spheres directly on the 3D model.

**DRR generation** (`DrrGenerator` in `OrthoPlanner.Infrastructure`):
- Ray-sum algorithm with HU+1000 shift
- 1st/99th percentile windowing
- Gamma correction γ=0.55
- **Do NOT reintroduce Beer-Lambert law** — it was tested and produces worse results.

**Landmark sync:** When a 2D landmark is placed, `Project2DTo3D()` maps the 2D DRR pixel to a 3D point on the model surface. When 3D mode places a landmark, the 2D position is back-projected into the DRR. This bidirectional sync keeps both views consistent.

**Mouse controls in 2D mode:**
- Left-click: place landmark / drag existing landmark
- Right-drag: window/level adjustment
- Middle-drag: pan
- Scroll: zoom
- Right-click on landmark dot: delete landmark

### 13.1 Landmark Persistence

Landmark data is saved in `project.json` via `MainViewModel.SavedCephLandmarks` (a `List<CephLandmarkSave>`).

```csharp
// Defined in MainViewModel.cs
public record CephLandmarkSave(
    string LandmarkId,
    double X2D, double Y2D,           // DRR pixel coords
    double X3D, double Y3D, double Z3D, // 3D world coords
    bool IsPlaced, bool IsLateral);

public List<CephLandmarkSave> SavedCephLandmarks { get; set; } = new();
```

**Write path:** After every UI change (drag, place, delete), `CephalometryOverlay.SyncLandmarksToVm()` serializes all currently-placed landmarks into `VM.SavedCephLandmarks`. `ProjectViewModel.SaveProject()` then includes them in `project.json`.

**Read path:** On `OpenProjectAsync`, the JSON list is deserialized into `VM.SavedCephLandmarks`. When the overlay is first shown (or `SetVolume` is called), `RestoreLandmarkData()` applies these records to the in-memory landmark objects and redraws the canvas.

### 13.2 Measurement Types and the `CephMeasurement` Class

All user-drawn cephalometric measurements (points, lines, planes, angles, distances) are stored as `CephMeasurement` objects in `CephToolState.Measurements`.

```csharp
// OrthoPlanner.Core/Imaging/CephMeasurement.cs
public class CephMeasurement
{
    public string Id { get; set; }        // 8-char GUID fragment, unique key
    public string Label { get; set; }     // Display label (e.g. "L1", "A1")
    public CephTool ToolType { get; set; } // Which tool created this
    public List<CephPoint> Points { get; set; } // 1–3 control points
    public double Value { get; set; }     // Computed value (mm, degrees)
    public string Unit { get; set; }      // "mm" or "°"
    public bool IsVisible { get; set; } = true; // Whether drawn on canvas
    public byte ColorR, ColorG, ColorB;   // Swatch color
    public string? RefMeasurementId1/2;   // For derived measurements
}
```

`CephTool` enum values and their category in the Measurements tree:

| Value | Tree group |
|---|---|
| `CustomPoint` | Points |
| `Line` | Linear |
| `InfinitePlane` | Planes |
| `AnglePlanes` | Angles |
| `Angle3Points` | Angles |
| `DistancePoints` | Linear |
| `DistancePointPlane` | Linear |

### 13.3 Public API Exposed by CephalometryOverlay

These methods are called from `MainWindow.xaml.cs` to drive the Measurements tab tree:

```csharp
// Event fired on every add / delete / visibility change
public event Action? MeasurementsChanged;

// Read the live list
public IReadOnlyList<CephMeasurement> GetMeasurements();

// Called by the tree's per-item eye checkbox
public void SetMeasurementVisible(CephMeasurement m, bool visible);

// Called by the tree's per-item delete button
public void DeleteMeasurementFromTree(CephMeasurement m);
```

Subscription happens in `MainWindow.OnLoaded` inside the `IsCephalometryOpen` property-change handler:
```csharp
CephalometryPanel.MeasurementsChanged -= RebuildCephMeasurementTree;
CephalometryPanel.MeasurementsChanged += RebuildCephMeasurementTree;
```

### 13.4 The Measurements Tab Tree (MainWindow right panel, tab index 1)

The Measurements tab (`RightPanelTabIndex == 1`) contains a two-level collapsible tree:

```
MEASUREMENTS
├── [☐] Custom Measurements      ← collapsed by default; reserved for future 3D measurements
└── [☐] 📐 Cephalometry          ← expanded by default
    ├── [☐] Points   → populated from CephTool.CustomPoint
    ├── [☐] Planes   → populated from CephTool.InfinitePlane
    ├── [☐] Angles   → populated from Angle3Points + AnglePlanes
    └── [☐] Linear   → populated from Line + DistancePoints + DistancePointPlane
```

Each row has: visibility `CheckBox` → color swatch `Ellipse` → label `TextBlock` → delete `Button`.

The tree is rebuilt in `MainWindow.RebuildCephMeasurementTree()`, which is triggered by `MeasurementsChanged`. Group-level checkboxes sync down via `CephAllVisibility_Changed` and `CephGroupVisibility_Changed`.

Named XAML elements in `MainWindow.xaml`:
- `CephPointsPanel`, `CephPlanesPanel`, `CephAnglesPanel`, `CephLinearPanel` — the four `StackPanel` hosts
- `CephAllGroupCheck`, `CephPtsGroupCheck`, `CephPlanesGroupCheck`, `CephAnglesGroupCheck`, `CephLinearGroupCheck` — group checkboxes
- `CxMeasListPanel` — placeholder for future custom measurements

---

## 14. Project Save/Load Format

`.orthoplan` files are **ZIP archives** containing:
- `project.json` — metadata (patient info, HU ranges, segment/mesh names, volume dimensions, **ceph landmarks**)
- `volume.bin` — raw `short[]` voxel data (Buffer.BlockCopy, little-endian)
- `segments/N_Name.bin` — segment vertex data (int vertexCount, then float x/y/z per vertex)
- `meshes/N_Name.bin` — imported mesh vertex data (same format)
- `occlusions/N_Name.bin` — occlusion STL vertex data (same format as meshes)

Current version field in JSON: **`"Version": "2.1"`**.

Backwards compatibility:
- `"2.0"` files: loaded normally; occlusion and ceph data simply absent (treated as empty).
- Pre-2.0 (single HU range): `TryGetProperty("MinHU", ...)` fallback is handled.

**What IS saved (as of v2.1):**

| Data | Where |
|---|---|
| Volume voxels | `volume.bin` |
| Segment vertices + colors | `segments/` + `project.json` |
| Imported mesh vertices | `meshes/` |
| Occlusion STL vertices | `occlusions/` |
| Per-occlusion `Matrix3D` transforms | `project.json` → `"OcclusionTransforms"` array |
| Cephalometric landmark positions | `project.json` → `"CephLandmarks"` array |

**What is NOT saved:**
- The `_segVolume` (SegmentationVolume labels) — must re-run segmentation after load.
- Surgical movement slider values (`SurgMaxillaLat`, etc.).
- NHP slider values.
- Undo stack.
- Cephalometric **measurements** (`CephMeasurement` list) — only landmarks are saved, not the drawn lines/angles.

On load, `_segVolume` is reset to null (segmentation must be rerun if needed for splitting).

### Occlusion persistence detail

In `ProjectViewModel.SaveProject()`:
1. Each `LoadedOcclusions[i]` vertex array is written to `occlusions/i_Name.bin`.
2. The corresponding `MaxillaOcclusionTransform` (`Matrix3D`) is serialized as a 16-element JSON array under `"OcclusionTransforms"`.

In `OpenProjectAsync()`:
1. Each `occlusions/*.bin` is read back into a new `MeshViewModel`.
2. The matching `Matrix3D` is deserialized and applied to `MaxillaOcclusionTransform`.

> ⚠️ `MandibleOcclusionTransform` is also saved (second Matrix3D per occlusion). Both are stored in the same `OcclusionTransforms` JSON array as consecutive entries.

---

## 15. Undo/Redo System

**Scope:** Only segment and imported mesh lists are undoable. DICOM volume, surgical sliders, NHP, and segmentation mask are NOT captured.

**Stack:** Maximum 5 snapshots (`_undoStack`, `_redoStack`).

`SaveStateForUndo()` must be called **before** any destructive action (adding, removing, or replacing segments).

`CreateStateSnapshot()` copies the collection references (not deep clones of vertices). If vertices are mutated in-place after a snapshot, the snapshot will reflect those mutations.

---

## 16. Protected Files — Never Modify Without Explicit Instruction

- `Polyplane.cs`
- `App.xaml.cs`
- `BoolConverters.cs`
- `AppTempStorage.cs`
- All files in `OrthoPlanner.Core` (unless the task explicitly targets them)

> **Note:** `MainWindow.xaml` and `MainWindow.xaml.cs` were previously on this list but have since required multiple additions (Measurements tab tree, crosshair wiring). They may be edited with explicit instruction but require a build check after every change.

---

## 17. Build Command

```powershell
cd "C:\Users\Mirko\Documents\Orthoplanner"
dotnet build OrthoPlanner.sln --configuration Debug --no-incremental
```

**Always run this after any change.** Do not proceed to the next step until the build is clean (0 errors).

---

## 18. Pending Refactoring Work

1. `CephalometryOverlay.xaml.cs` (~2100 lines) has no dedicated ViewModel. A public API exists (`GetMeasurements`, `SetMeasurementVisible`, `MeasurementsChanged`) to allow the Measurements tab to interact with it without a ViewModel, but a proper `CephalometryViewModel` would clean this up substantially.
2. Cephalometric **measurements** (drawn lines, angles, distances) are not saved to the project file — only landmarks are. Add measurement serialization to `ProjectViewModel.SaveProject` / `OpenProjectAsync`.
3. Surgical planning windows (`BssoOsteotomyWindow`, `LeFortOsteotomyWindow`, `CondyleSplitWindow`) contain 600–900 lines each with business logic mixed into the code-behind. Refactor into Core services.
4. Clean up spurious root-level files: `extract.cs`, `ViewCube.cs`, `ViewCubeVisual3D.cs`, `RefactorUI/`, `RestoreUI/`, `probe*.cs`, `patch.cs`, etc.

---

## 19. Recent Changes (June 2026)

### Session: Persistence + Measurements Tree

**Files changed:**

| File | What changed |
|---|---|
| `OrthoPlanner.Core/Imaging/CephMeasurement.cs` | Added `IsVisible` property (default `true`) |
| `Views/CephalometryOverlay.xaml.cs` | Added landmark persistence: `SyncLandmarksToVm()`, `RestoreLandmarkData()`; added public API: `MeasurementsChanged`, `GetMeasurements()`, `SetMeasurementVisible()`, `DeleteMeasurementFromTree()`; `RefreshMeasurementOverlay()` now skips `IsVisible=false` items |
| `ViewModels/MainViewModel.cs` | Added `CephLandmarkSave` record; added `SavedCephLandmarks` property |
| `ViewModels/ProjectViewModel.cs` | Bumped version to `2.1`; added save/load for occlusion vertex data (`occlusions/` sub-ZIP) and their `Matrix3D` transforms; added save/load for `CephLandmarks` JSON array |
| `MainWindow.xaml` | Replaced Measurements tab static text with a two-level collapsible `Expander` tree (Cephalometry → Points/Planes/Angles/Linear) |
| `MainWindow.xaml.cs` | Added `RebuildCephMeasurementTree()`, `CephAllVisibility_Changed()`, `CephGroupVisibility_Changed()`, `CxMeasGroupVisibility_Changed()`, `SetEmptyPlaceholder()` |

**Risks resolved:**
- Cephalometry overlay fragility (landmarks now persist across sessions)
- Occlusion alignment loss on project save (occlusion STLs + transforms now saved)
