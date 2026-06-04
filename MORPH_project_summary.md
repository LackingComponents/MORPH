# MORPH — Project Handoff Summary

## What is MORPH?

MORPH is an open-source orthognathic surgery planning application for the cranio-maxillofacial (CMF) surgery community. It is built with **C# / WPF (.NET)** and targets Windows desktop. The project was born from real clinical needs and has no references to commercial surgical planning software to avoid legal complications.

The primary developer is a maxillofacial surgeon (Lore) with limited programming background. AI assistance (Claude, Claude Code, Cursor) is the main implementation pathway. Every prompt must be written with that in mind: be explicit, never assume prior knowledge, never skip steps.

---

## Repository

- **Active repo:** `https://github.com/LackingComponents/MORPH`
- **Local path:** `C:\Users\fdrln\Desktop\CMF PLANNER\MORPH`
- **Solution file:** `OrthoPlanner.sln`
- **License:** Apache 2.0
- **Active branch:** `Lore`
- **Safety branch:** `Lore_review` (used for major refactoring, then force-reset to `Lore`)

A collaborator (`xXyouknowxX`) maintains a fork at `github.com/xXyouknowxX/MORPH` and has contributed GPU acceleration files, geometry utilities, and a Photogrammetry module.

---

## Solution Structure

```
OrthoPlanner.sln
├── OrthoPlanner.App         → WPF UI layer (Views, ViewModels, Windows)
├── OrthoPlanner.Core        → Business logic (engines, loaders, data models)
└── OrthoPlanner.Infrastructure → DICOM / file I/O
```

### Key Libraries
| Library | Purpose |
|---|---|
| HelixToolkit | 3D rendering in WPF |
| fo-dicom | DICOM loading and handling |
| CommunityToolkit.Mvvm | MVVM pattern, partial class ViewModels |
| ILGPU | GPU acceleration |

---

## Current Feature Set

### DICOM & Imaging
- DICOM CT import via fo-dicom
- Volume rendering
- DRR (Digitally Reconstructed Radiograph) generation:
  - Ray-sum algorithm with HU+1000 shift
  - Percentile windowing (1st/99th percentile)
  - Gamma correction (γ = 0.55)
  - **Note:** Beer-Lambert law was tested and produced worse results — do not reintroduce it

### Cephalometry Module
- 42-landmark placement system (Badiali et al., *Progress in Orthodontics* 2022)
- Reference planes (Frankfurt, McNamara, etc.)
- Measurements and export
- 3D ↔ DRR toggle (Ctrl+D) with bidirectional landmark synchronization

### Surgical Planning
- BSSO osteotomy window (`BssoOsteotomyWindow`)
- LeFort I osteotomy window (`LeFortOsteotomyWindow`)
- Condyle split window (`CondyleSplitWindow`)
- Each window is 600–800 lines; refactoring into Core services is planned but not yet done

### Segmentation
- Bone and soft tissue segmentation engine (`SegmentationEngine` in Core)

### GPU / Geometry (from collaborator fork)
- `GpuContext.cs`, `GpuKernels.cs` — GPU acceleration
- `IcpAligner.cs`, `KdTree.cs`, `MeshOps.cs`, `SplineHelper.cs`, `StlIO.cs` — geometry utilities
- Photogrammetry module

---

## Architecture: ViewModel Refactoring

`MainViewModel.cs` (~3,500 lines, a God Object) has been refactored into **partial classes** using CommunityToolkit.Mvvm:

| File | Responsibility |
|---|---|
| `DicomViewModel.cs` | DICOM loading, volume data |
| `SurgeryViewModel.cs` | Surgical planning state |
| `SegmentationViewModel.cs` | Segmentation controls |
| `OsteotomyViewModel.cs` | Osteotomy operations |
| `ProjectViewModel.cs` | Project save/load |
| + 5 smaller partials | Misc grouped concerns |

Build verification was confirmed at zero errors after this refactoring.

---

## Protected Files — NEVER MODIFY

These files must never be changed unless explicitly instructed by Lore:

- `MainViewModel.cs` (original from fork)
- `MainWindow.xaml` and `MainWindow.xaml.cs`
- `Polyplane.cs`
- `App.xaml.cs`
- `BoolConverters` (all files)
- `AppTempStorage`
- All files in `OrthoPlanner.Core` business logic (unless a specific task targets them)

---

## Pending Work

1. Refactor `CephalometryWindow.xaml.cs` (~900 lines, no dedicated ViewModel) into a proper `CephalometryViewModel`
2. Refactor surgical planning windows (`BssoOsteotomyWindow`, `LeFortOsteotomyWindow`, `CondyleSplitWindow`) into Core services
3. Clean up spurious root-level files: `extract.cs`, `ViewCube.cs`, `ViewCubeVisual3D.cs`, `RefactorUI/`, `RestoreUI/`

---

## Build Command

```
dotnet build OrthoPlanner.sln --configuration Debug --no-incremental
```

Always run this after any change. Do not proceed to the next step until the build is clean.

---

## Key Principles for AI Assistants Working on This Project

1. **Read before writing.** Always read the existing file in full before proposing changes to it. Never assume file contents.
2. **Anchor every prompt to the local path:** `C:\Users\fdrln\Desktop\CMF PLANNER\MORPH`
3. **Never modify protected files** (listed above) without explicit instruction.
4. **Step-by-step with confirmation gates.** Propose one logical block of changes at a time. Wait for Lore to confirm before proceeding.
5. **Build after every change.** The build command above must pass before moving on.
6. **Explain concepts as you go.** Lore is learning while building. Briefly explain why you are doing what you are doing.
7. **Never batch unverified steps.** Do not chain multiple changes that haven't been individually confirmed.
8. **Clinical terminology is understood.** DRR, HU, MIP, Frankfurt plane, McNamara, BSSO, LeFort I, teleradiografia laterale — use these freely.

---

## Domain Context

This is a **medical software** project. The clinical workflow it supports is:
1. Import CT scan (DICOM)
2. Segment bone and soft tissues
3. Plan orthognathic osteotomies (jaw surgery) — BSSO, LeFort I, genioplasty, etc.
4. Perform cephalometric analysis on DRR
5. Export STL for 3D printing of surgical guides or models
6. Superimpose STL on DICOM

The target users are maxillofacial surgeons and orthodontists.
