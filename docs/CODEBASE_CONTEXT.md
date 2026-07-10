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

> **Before any refactor or edit to a shared contract, read §22 (Whole-Project Architecture & Cascade Risk).** It maps the god-objects and the cross-cutting conventions where a single change ripples to many call sites — the reason "it got bigger and bigger, and now one change risks cascading breaks."

---

## 2. Solution Layout

```
OrthoPlanner.sln
├── src/OrthoPlanner.App    → WPF UI layer (Views, ViewModels, Windows, code-behind)
└── src/OrthoPlanner.Core   → Business logic (engines, loaders, data models, DRR)

> `OrthoPlanner.Infrastructure` no longer exists — it was folded into `OrthoPlanner.Core` (commit `c107f93`). `DrrGenerator` now lives in `src/OrthoPlanner.Core/Imaging/`. There are exactly **two** projects today.
```

### Key NuGet packages

| Package | Project | Purpose |
|---|---|---|
| `HelixToolkit.Wpf.SharpDX` 3.1.2 | App | 3D rendering (DirectX-backed via SharpDX) |
| `CommunityToolkit.Mvvm` 8.4.0 | App | MVVM: `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]` |
| `ManifoldNET` 1.0.7-alpha | App + Core | Boolean mesh operations (requires `manifoldc.dll` + `tbb12.dll` native DLLs in output) |
| `fo-dicom` 5.2.5 | Core | DICOM loading |
| `fo-dicom.Codecs` 5.16.5.1 | Core | DICOM codec transcoding |
| `geometry3Sharp` 1.0.324 | Core | `DMesh3` indexed mesh + SDF types (`BoundedImplicitFunction3d`, `AxisAlignedBox3d`); used by `MeshOps`, `IcpAligner`, `SplintEngine` |
| `ILGPU` 1.5.1 | Core | GPU acceleration — CUDA (NVIDIA) → OpenCL (AMD/Intel) → CPU fallback; powers `GpuMorphology3D`. (`ILGPU.Algorithms` was removed.) |

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
| `ViewModels/MainViewModel.cs` | **Class declaration + base** (`public partial class MainViewModel : ObservableObject`), shared helpers (`RefreshCombinedModel`, `MeshHelper`), and the helper VMs `SegmentViewModel` (L174) + `MeshViewModel` (L300) |
| `ViewModels/DicomViewModel.cs` | DICOM loading, MPR slice rendering, window/level, histograms, NHP physical reslice |
| `ViewModels/SegmentationViewModel.cs` | Segmentation pipeline, region growing, mesh generation, live bone preview |
| `ViewModels/OsteotomyViewModel.cs` | Osteotomy wizard launchers (LeFort 1, BSSO, Genioplasty, Condyle Split) |
| `ViewModels/SurgeryViewModel.cs` | Per-segment 6-DOF surgical movements, occlusion STL alignment / ICP orchestration, per-occlusion plan tree |
| `ViewModels/StlViewModel.cs` | STL import + classify, dental cast alignment (ICP), clean-merge, export |
| `ViewModels/ProjectViewModel.cs` | Save/Load `.orthoplan` ZIP archives (incl. occlusion vertices/transforms + ceph landmarks) |
| `ViewModels/NhpViewModel.cs` | Natural Head Position — visual preview (sliders → `_nhpTransform`) + commit/reslice, bakes landmarks through the delta |
| `ViewModels/UndoRedoViewModel.cs` | 5-level undo stack (segment/mesh snapshots + condyle landmark backup) |
| `ViewModels/ViewportViewModel.cs` | Camera anchors, clipping, MPR toggles, orthographic mode |
| `ViewModels/VolumeRenderingViewModel.cs` | Volume rendering toggle (uses `AllowUnsafeBlocks` for half-float write) |
| `ViewModels/SplintViewModel.cs` | Splint planner state, `CondyleBox`, mandibular autorotation params |
| `ViewModels/ThreeDModelPanelViewModel.cs` | 3D-model panel controls |

> **Crucial:** all 13 files above are `partial class MainViewModel` — they add to a *single* object, the app's one `DataContext` created by `MainWindow`. There is no encapsulation between them: every partial sees every field/property the others declare. This is the project's central **god-object** (see §22). `OcclusionPlanViewModel` and `OcclusionNodeViewModel` are *not* part of it — they are independent `ObservableObject` subclasses owned by the surgery VM.

**Independent VM classes** (their own `ObservableObject`, *not* `MainViewModel` partials):
- `DicomSelectorViewModel` + `SeriesItemViewModel` — series picker dialog.
- `OcclusionNodeViewModel`, `OcclusionPlanViewModel` — the plan-tree nodes/plans owned by `SurgeryViewModel`.
- `PhotogrammetryViewModel`, `PhotoViewModel`, `MeasurementViewModel`, `LineAnnotationViewModel`, `AngleAnnotationViewModel` (under `ViewModels/Photogrammetry/`) — the 2D-photo measurement subsystem (see §21).

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
   - Reslices `Volume` (the committed CT) through `SegmentationEngine.ResliceVolume()`, producing a new `VolumeData` physically in the NHP orientation.
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
| `SplitCraniumMandibleAsync` | `CondyleSplitWindow` | Full bone mesh + segvol | `CraniumResult`, `MandibleResult`, `LeftCondyleCenter`, `RightCondyleCenter`, `DentalMidlinePoint`, `Left/RightCondyleHalfExtents`. Two entry modes via a `landmarkOnlyMode` flag (`false` = full split from `OsteotomyViewModel`; `true` = landmarks-only from `SurgeryViewModel.EnsureCondyleFulcrum`). |
| (seed split fallback) | `SeedSplitWindow` | Full bone mesh + segvol | `CraniumResult`, `MandibleResult` — but **no landmarks** (a contract gap: cases routed through the seed path silently lose the Ramus fulcrum + splint box; see [cranium-mandible-split-redesign.md](cranium-mandible-split-redesign.md) §2). |
| (cast / dental align) | `DentalAlignmentWindow` / `DentalCastEditorWindow` | CT bone + cast meshes | `FinalTransform` (`double[,]`) + merged cast mesh; the algorithm-only path reuses `IcpAligner`. |

> The **splint** wizards (`SplintPlannerWindow`, `SplintSequenceWindow`) and the **occlusion-alignment** windows (`OcclusionAlignmentWindow`, `ManualOcclusionAlignmentWindow`, `OcclusionCheckerWindow`) follow the same show-dialog → read-public-properties pattern but emit splint meshes or `Matrix3D`/`double[,]` transforms rather than osteotomy result meshes.

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

Current version field in JSON: **`"Version": "2.2"`** (bumped from "2.1"; the 2.1 occlusion/ceph-landmark additions and the 2.0 / pre-2.0 fallbacks below still apply).

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

> **Refactor carve-out:** the "All files in `OrthoPlanner.Core`" line is **suspended for the app-wide decomposition refactor** (in flight) — the Core engines (`SplintEngine`, `MeshOps`, `SegmentationEngine`) are in scope to be split by responsibility with their public API kept stable. The usual protect rule reapplies once that refactor lands.

---

## 17. Build Command

```powershell
cd "C:\Users\Mirko\Documents\Orthoplanner"
dotnet build OrthoPlanner.sln --configuration Debug --no-incremental
```

**Always run this after any change.** Do not proceed to the next step until the build is clean (0 errors).

---

## 18. Pending Refactoring Work

0. **App-wide decomposition (in flight):** pulling pure logic out of the god-objects into plain Core helpers + a missing `CephalometryViewModel`, behavior-preserving with a characterization ("golden-master") harness, ordered least-risk first. §22 maps the god-objects and the cascade contracts this refactor is meant to shrink.
1. `CephalometryOverlay.xaml.cs` (~2027 lines) has no dedicated ViewModel. A public API exists (`GetMeasurements`, `SetMeasurementVisible`, `MeasurementsChanged`) to allow the Measurements tab to interact with it without a ViewModel, but a proper `CephalometryViewModel` would clean this up substantially. **Also:** `Make3DSphere`/`Make3DLine` are duplicated identically in `MainWindow.xaml.cs` (dedup target).
2. Cephalometric **measurements** (drawn lines, angles, distances) are not saved to the project file — only landmarks are. Add measurement serialization to `ProjectViewModel.SaveProject` / `OpenProjectAsync`.
3. Surgical planning windows mix business logic into the code-behind: `CondyleSplitWindow` (~1251), `SplintPlannerWindow` (~894), `SplintSequenceWindow` (~695), `BssoOsteotomyWindow` (~696), `LeFortOsteotomyWindow` (~657), etc. The **Cranium/Mandible split** is already redesigned as Architecture C (Core solvers behind `ICraniumMandibleSolver`) — see [cranium-mandible-split-redesign.md](cranium-mandible-split-redesign.md). Model the other wizards on the same "pure Core solver + thin window" boundary.
4. ~~Clean up spurious root-level files~~ — **DONE** (`extract.cs`, `ViewCube.cs`, `RefactorUI/`, `RestoreUI/`, `probe*.cs`, `patch.cs` are gone; `MarchingCubesTables` folded into `Core.Imaging.MarchingCubes`; the `OrthoPlanner.Infrastructure` project removed; `ILGPU.Algorithms` removed).

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

---

## 20. Visual Style & UX/UI Design System

This section describes the colors, typography, layout structures, custom styling rules, and WPF/DWM windows integration details that establish and maintain MORPH's premium dark-theme visual style. When building new UI elements or editing views, AI assistants must adhere strictly to these conventions to ensure a consistent user experience.

### 20.1 Global Color Palette

MORPH uses a custom Slate / Blue-Grey color system defined in `Themes/DarkTheme.xaml` and merged globally. Avoid hardcoding random colors; always reference these keys or brushes:

| Color Key | Brush Key | Value | Purpose / Application |
|---|---|---|---|
| `BgDark` | `BgDarkBrush` | `#FF0D1117` | Primary window backgrounds |
| `BgMedium` | `BgMediumBrush` | `#FF151B23` | Sidebar panel backgrounds, toolbars, status bar, context menus |
| `BgLight` | `BgLightBrush` | `#FF1E2730` | Control backgrounds (textboxes, slider track, progress bar base, option panels) |
| `BgHover` | `BgHoverBrush` | `#FF283545` | Hover state background, highlighted menu items, selected row backdrops |
| `Accent` | `AccentBrush` | `#FF6B8DAF` | Muted slate-blue accent for active borders, slider thumbs, toggle buttons, progress bar indicators |
| `AccentHover` | `AccentHoverBrush` | `#FF7FA3C7` | Lighter slate-blue hover state for active components |
| `Success` | - | `#FF6BA88C` | Muted sage-green for positive/accept actions (e.g., Accept buttons) |
| `Warning` | - | `#FFD4A056` | Muted gold/orange for warning highlights |
| `TextPrimary` | `TextPrimaryBrush` | `#FFD0D8E0` | Off-white / light slate grey for primary text, values, and titles |
| `TextSecondary` | `TextSecondaryBrush`| `#FF6E7F90` | Muted slate blue-grey for secondary text, labels, and placeholders |
| `Border` | `BorderBrush` | `#FF232E3A` | Separators, grid dividers, control borders |

### 20.2 Typography

* **Font Family**: `Segoe UI` is used exclusively for UI text. `Consolas` (monospace) is used for floating overlay HUD displays.
* **Sizes & Weights**:
  * **Default Window / View Text**: `13px`, Regular.
  * **Default Control Labels (Buttons, CheckBoxes)**: `12px`, Regular.
  * **Section Headers (`PanelHeader`)**: `10px`, SemiBold, `Opacity=0.85`, Foreground `AccentBrush`, Margin `0,0,0,4`.
  * **Secondary Labels / Muted Info (`StatusText`)**: `11px`, Foreground `TextSecondaryBrush`.
  * **Dynamic Tree Items**: `10px`, Foreground `#D0D8E0`.
  * **Empty Placeholders**: `10px`, Italic, Foreground `#6E7F90`.
  * **Step Titles** (in osteotomy wizards): `16px`, SemiBold, Foreground `#EAECF0`.
  * **Monospace HUD Text**: `13px`, SemiBold, Foreground `#00E5FF` (Cyan).

### 20.3 Global Window Dark Chrome (DWM Integration)

To avoid standard white OS window title bars and borders clashing with the dark client area, `App.xaml.cs` hooks into a global `Window.Loaded` event handler and applies native Windows DWM attributes:
* **Immersive Dark Mode** is enabled by setting DWM attributes `20` and `19` to `1`.
* **Caption/Title Bar Color** (DWM attribute `35`) is set to match the window's WPF `Background` color (typically `BgDark` `#0D1117` or `BgMedium` `#151B23`).
* **Window Border Color** (DWM attribute `34`) is set to match the window's WPF `Background` color.
* **Window Title Text Color** (DWM attribute `36`) is set to light grey `#D0D8E0`.

> [!NOTE]
> Do not define default light-colored windows. Always style window backgrounds with `BgDarkBrush` or `BgMediumBrush` so the DWM interop works seamlessly.

### 20.4 Control Template Conventions

* **Buttons**:
  * Default style: Rounded border (`CornerRadius="4"`), padding `14,6`, cursor `Hand`. Hover state transitions background to `BgHoverBrush` and border to `AccentBrush`. Pressed state uses `AccentBrush`. Disabled state uses `0.4` Opacity.
  * `AccentButton`: Uses `AccentBrush` background, white text, and `SemiBold` font weight.
  * Wizard Action Buttons: Cancel buttons use `#20FFFFFF` background with `#40FFFFFF` border. Accept / Success buttons use `#308040` (sage green) background. Primary action / Next buttons use `#3060A0` (deep blue) background.
* **CheckBox (Toggle Switch)**:
  * Overridden to display as a sliding switch toggle: Width `32`, Height `16`, track `CornerRadius="8"`.
  * When Checked, the track background becomes `AccentBrush` and the thumb slides right (changing margin from `2,0,18,0` to `18,0,2,0`).
* **Sliders**:
  * Track height is `4` with a progress fill using `AccentBrush`.
  * Thumb is an `Ellipse` with diameter `14`, filled with `AccentBrush` and a `2px` stroke of `BgDarkBrush`.
* **Expander (TransparentExpander)**:
  * Default expanders are customized to be borderless except for a bottom line separator.
  * Uses a custom vector path arrow (`M 0,0 L 4,4 L 0,8` in White) which rotates 90 degrees when expanded.
* **Scrollbars**:
  * Unobtrusive track width of `6` with an `AccentBrush` rounded thumb.

### 20.5 Layout & Grid Structure

* **Main Workspace Grid Columns** (`MainWindow.xaml`):
  * **Left Sidebar**: Width `230`. Contains the workflow accordion of expanders. Styled with background `BgMediumBrush` and border `BorderBrush`.
  * **Center Viewport Area**: Width `4*`. Houses the 3D renderer and MPR slices.
  * **Right Panel**: Width `1.5*`. Houses lists of segments, STL files, and measurements.
* **Settings Popups**:
  * Small gear buttons (`M19.43...` SVG path) trigger a `Popup` control.
  * Popup boxes use Background `#FF21252B`, BorderBrush `#FF30343D`, and a CornerRadius of `4`. They feature vertical layouts with custom stepper repeat buttons (`▲`/`▼`, Width `20`, Height `12`).
* **HUD Overlays**:
  * Overlays (like Window/Level or Zoom details) float in the top-right of viewports using semi-transparent dark boxes (Background `#BB000C18`, CornerRadius `6`, BorderBrush `#FF3A5A7A`).

### 20.6 Dynamic Code-Behind UI Generation

When creating UI elements in C# (e.g. measurements tree lists), adhere to these layout styles:
* **Tree Rows**: Horizontal StackPanel with margin `0,1,0,1`.
* **Row Elements**:
  1. CheckBox (visibility toggle).
  2. Color Swatch (Ellipse, `Width=7`, `Height=7`, VerticalAlignment `Center`, Right Margin `5`).
  3. TextBlock (Label + value, FontSize `10`, Foreground `#D0D8E0`, `TextTrimming=CharacterEllipsis`, `MaxWidth=140`).
  4. Delete Button (✕ text, FontSize `8`, Padding `2,0`, transparent background, Foreground `#888888`, Cursor `Hand`, ToolTip `Delete measurement`).
* **Empty States**: Show italicized placeholder TextBlocks in `#6E7F90`.

---

## 21. Photogrammetry (2D Photo Measurement)

A measurement feature **separate** from the 3D cephalometry module (§13): photogrammetry works on ordinary 2D clinical photos (profile / frontal / smiling), not CT or DRR.

Entry: `Views/PhotogrammetryView.xaml.cs` (canvas + mouse) bound to `PhotogrammetryViewModel` under `ViewModels/Photogrammetry/`, which owns an `ObservableCollection<PhotoViewModel>`. It is **not** part of the `MainViewModel` god-object — its own independent `ObservableObject` (see §4.1).

Tool modes (`PhotogrammetryToolMode`):

| Mode | Action |
|---|---|
| `Pan` | default: left-drag pan, scroll zoom |
| `Normalize` | draw a line, enter its real mm length → sets `PixelsPerMm` (the calibration that makes every other mode quantitative) |
| `Horizon` | draw a line between two points → rotate the image so that line is horizontal |
| `Measure` | draw a line → read distance in mm (uses `PixelsPerMm`) |
| `DrawLine` | permanent annotation line |
| `Angle` | draw a line → read its angle from horizontal |

Annotations persist (in-memory only) as `MeasurementViewModel` / `LineAnnotationViewModel` / `AngleAnnotationViewModel` on each `PhotoViewModel`. **No project-file persistence yet** — the same gap as ceph-measurements (§18-2).

---

## 22. Whole-Project Architecture & Cascade Risk

> Read this before planning any refactor or touching a shared contract. It names the **god-objects** and, more importantly, the cross-cutting conventions where a single change ripples across many call sites — the "it got bigger and bigger, now one change risks cascading breaks" problem.

### 22.1 How the app grew

MORPH began small: one `MainWindow`, one `MainViewModel`, a few Core engines. The partial-class MVVM pattern (required by CommunityToolkit.Mvvm source generators) let features scale by adding `partial class MainViewModel` files — which silently turned **one class into a thirteen-headed god-object** (§22.2). In parallel, each new osteotomy / splint wizard grew its own `Window` code-behind that accumulated rendering + mouse + algorithm together, and the Core engines grew to 1400–1500 lines. The result today: no tests, heavy shared mutable state, and **string-based contracts** (§22.3) that mean every edit risks an invisible cascade.

### 22.2 The god-objects (the targets of the refactor)

| God-object | Lines | Why it is a god-object |
|---|---|---|
| `MainViewModel` (13 partials) | ~5,700 | One object, one `DataContext`; all 13 files share all private state. DICOM, segmentation, surgery, NHP, splint, undo, viewport, volume, 3D-panel, project all on one class. Editing a field is visible to every other partial — zero encapsulation. |
| `CephalometryOverlay.xaml.cs` | 2027 | 2D landmark sidebar + DRR gen/render + canvas mouse + 2D↔3D projection math + 3D landmarks/measurements + tool-by-tool handlers + measurement drawing + public API + persistence. No ViewModel. |
| `MainWindow.xaml.cs` | 1494 | Camera/orbit + grid overlay + MPR slice interaction + crosshairs + numeric-textbox helpers + the entire ceph measurement-tree + its own custom 3D-measurement subsystem (duplicates CephalometryOverlay's `Make3DSphere`/`Make3DLine`). |
| `CondyleSplitWindow.xaml.cs` | 1251 | DX rendering + mouse hit-testing + plane math + voxel flood-fill + marching-cubes orchestration, all in one code-behind. (Redesign in flight — see [cranium-mandible-split-redesign.md](cranium-mandible-split-redesign.md).) |
| `SplintPlannerWindow.xaml.cs` | 894 | Same wizard-mixing pattern (arch point drag, autorotation, snapping, rendering). |
| `SurgeryViewModel.cs` (partial of the god) | 996 | Surgical-transform math + occlusion ICP orchestration + plan tree all on the shared object (mixed with the matrix-convention converters). |
| `SplintEngine.cs` (Core) | 1515 | Arch sampling + the `GenerateSplint` pipeline + autorotation + rotation utils + ribbon/line-strip + tooth-pocket + `OffsetImpl` SDF. A pipeline, but with several separable sub-algorithms. |
| `MeshOps.cs` (Core) | 1469 | Slicing + dental-cast ops + topology/components + `DMesh3` interop + bbox clip — a grab-bag. |
| `SegmentationEngine.cs` (Core) | 1384 | Threshold/clean/morphology + region-grow variants + mesh extraction + `ResliceVolume` (an imaging op misplaced in Segmentation). |

> Big-but-coherent files deliberately left alone by the refactor: `IcpAligner.cs` (691 — one concern, ICP+SVD), `DicomViewModel.cs` (780 — "the DICOM/MPR viewport VM"), `VolumeData.cs` / `MarchingCubes.cs` / `SdfEngine.cs` (each one cohesive algorithm). The decomposition is by **cohesion**, not line-count.

### 22.3 The cross-cutting contracts — where one change cascades

These are the load-bearing conventions; touch one and a fan-out breaks.

**a. Segment-name strings (§8) — the biggest epicenter.** Surgery transforms, occlusion, NHP bake, the splint engine, undo, and persistence all locate segments by `Segments.LastOrDefault(s => s.Name.Contains("Maxilla") ...)` etc. Renaming a segment — or adding one whose name substring-matches an existing role — breaks **surgery transforms**, **occlusion alignment**, **NHP baking**, **undo**, and **save/load** simultaneously, with **no compiler error**, only a wrong surgical result downstream. Hardening path: an explicit role tag/enum on `SegmentViewModel` instead of substring matching (keep names identical during the refactor; the tag can be a follow-up).

**b. The `MainViewModel` god-object itself.** Because every feature is a partial of one class, *any* new state becomes a field all 13 files can see → no boundary to reason about → "easy" additions that braid unrelated features together. Even unrelated features read each other's fields (e.g. splint-wizard state read by surgery-transform code). The unit of change is effectively the whole class.

**c. Wizard output contracts.** Each wizard exposes specific `*Result`/`*Center` properties, and `OsteotomyViewModel` hardcodes those names ([OsteotomyViewModel.cs:328](../src/OrthoPlanner.App/ViewModels/OsteotomyViewModel.cs#L328)). Adding or renaming a result breaks the caller silently. The seed-split path already violates this contract (emits meshes but no landmarks — see [cranium-mandible-split-redesign.md](cranium-mandible-split-redesign.md) §2). The split redesign centralizes `SplitRequest`/`SplitResult` records one place; the other wizards should follow.

**d. Matrix / vector-convention split.** Three conventions coexist with no single conversion boundary: WPF `Matrix3D`/`Vector3D` (row-vector), `IcpAligner` `double[4,4]` (column-vector), and `System.Numerics.Vector3`. The converters (`ToDoubleMatrix` / `ConvertToMatrix3D`) live in `SurgeryViewModel`. Getting the convention wrong = a silently-transposed transform. The split redesign does the conversion once at the wizard boundary — the pattern to copy.

**e. Coordinate spaces.** Three spaces interleave per feature: raw **DICOM** (`x*spacing`), **baked/NHP** (post-commit), and **display** (view-only `Transform`). Each feature handles them by hand; the missing bake on returned split meshes (split P1) is a known silent-wrong-result trap. Convention to enforce: the wizard that owns the forward transform bakes it *before* exposing results; consumers must not re-bake.

**f. NHP-commit fan-out.** `CommitNhp` rewrites `Volume`, mutates every segment/mesh `Vertices` in place, resets transforms, and bakes `Left/RightCondyleCenter` + `DentalMidlinePoint` — but **not** `CondyleHalfExtents` (split P4). A commit touches ~6 subsystems; forgetting one field is an invisible splint-geometry error. Mitigation: make "what NHP bakes" an explicit, enumerable list.

**g. Shared mutable mask state.** `_segVolume` (and the recently-removed `_boneOnlySegVolume`) is mutated by live-preview / seed-split paths; the cranium split had to drop a fast path because a prior seed-split-preview could clobber the mask. Shared mutables that previews write to are a standing footgun.

### 22.4 Why it has become "impossible to maintain"

- **Locating a change** requires reading across multiple partials / windows — no single file shows a feature end-to-end.
- **No compiler guard** on the contract layers: a renamed segment, field, or result property builds clean and ships wrong.
- **No tests** mean behavior regressions surface only when a surgeon clicks the exact flow — sometimes clinics later.
- **Heavy shared mutable state** (god-object fields + the segment volume) means refactors can change timing/data a unit test would have caught.

### 22.5 What the refactor buys, and how it is ordered

The decomposition (approach B) shrinks each cascade to a contract boundary:
- Pull **pure math** out of code-behind / the god-object into Core helpers, backed by a **characterization harness** (golden-master fingerprints on a saved `.orthoplan` — vertex counts + bounding box + sampled-vertex hash) so algorithmic regressions the string/god-object contracts currently hide become *visible automatically*, not just when a surgeon clicks.
- Keep **public API + segment names + wizard result properties + matrix conventions identical** so the cascade contracts above do not fire.
- The split redesign (Architecture C) is the exemplar for one subsystem; model the rest on its "pure Core solver + thin window + record contracts" boundary. Interfaces appear **only** where there is genuinely >1 implementation (e.g. `ICraniumMandibleSolver`); no interface-with-one-impl, no DI, no factory unless there's a real second caller — per the project's lazy-code rule.
- **Order = risk-ascending (cohesion verdict drives it):** bank the safe wins first (dedup `Make3DSphere`/`Make3DLine`; lift `ResliceVolume` from Segmentation into `Imaging`), then the wizard math, hardest-coupled (`CondyleSplit`, `SplintPlanner`) last. Each step ends with a clean build + a golden-master re-diff.

---

## 23. Recent Changes (late June – July 2026)

- **Project structure:** `OrthoPlanner.Infrastructure` folded into `OrthoPlanner.Core`; `DrrGenerator` now in `Core/Imaging`. `MarchingCubesTables` folded into `Core.Imaging.MarchingCubes`. `ILGPU.Algorithms` removed (raw `ILGPU` 1.5.1 kept). Root junk files cleaned up (§18-4 done).
- **Splint wizard:** multi-step `SplintSequenceWindow` + intermediate/final wafer orchestration added; `SplintEngine` gained O(1) arch-lookup tables, Taubin smoothing, spine rasterization, guided bridge.
- **Cranium/Mandible split:** function report [cranium-mandible-split.md](cranium-mandible-split.md) (problems P1–P7) and redesign [cranium-mandible-split-redesign.md](cranium-mandible-split-redesign.md) (Architecture C) written; redesign not yet implemented.
- **Dead-code audits:** trimmed unused `SplineHelper`/`StlIO` lines, deduped converters + DWM P-Invoke.
- **Photogrammetry:** new 2D-photo measurement subsystem (documented in §21).
- **Project version:** `"2.1"` → `"2.2"`.
