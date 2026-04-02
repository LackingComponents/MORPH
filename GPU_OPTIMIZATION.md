# GPU & CPU Optimization — MORPH / OrthoPlanner

## Sommario delle modifiche

Questo documento descrive tutte le ottimizzazioni introdotte rispetto alla versione originale.

---

## Nuovi file

### `src/OrthoPlanner.Core/GpuContext.cs`
Singleton che gestisce il contesto ILGPU e il miglior acceleratore disponibile.

**Priorità auto-detection:**
1. **CUDA** (NVIDIA) — massima performance
2. **OpenCL** (AMD, Intel, NVIDIA senza CUDA toolkit)
3. **CPU fallback** — ILGPU vectorizza comunque con SIMD

```csharp
// Utilizzo (automatico, non serve chiamarlo esplicitamente)
var gpu = GpuContext.Instance;
Console.WriteLine(gpu.DeviceName);     // es. "CUDA: NVIDIA GeForce RTX 3070"
Console.WriteLine(gpu.IsGpuAvailable); // true / false
```

**Dispose all'uscita dell'app** (in `App.xaml.cs` o `Main`):
```csharp
protected override void OnExit(ExitEventArgs e)
{
    GpuContext.Instance.Dispose();
    base.OnExit(e);
}
```

### `src/OrthoPlanner.Core/GpuKernels.cs`
Tutti i kernel ILGPU. Ogni metodo `static void KernelName(Index1D index, ...)` è un kernel
GPU — il primo parametro `Index1D` è il thread index (gestito automaticamente da ILGPU).

| Kernel | Descrizione |
|--------|-------------|
| `ThresholdKernel` | Labeling voxel in range [minHU, maxHU] |
| `ClearKernel` | Azzeramento array |
| `DilationKernel` | Dilatazione morfologica 6-connessa |
| `ErosionKernel` | Erosione morfologica 6-connessa |
| `SmoothLabelKernel` | Smoothing con majority vote 3×3×3 |
| `MarchingCubesCubeIndexKernel` | Calcolo cube-index per ogni cella |
| `TransformVerticesKernel` | Applicazione matrice 4×4 a vertici |
| `ComputeDistancesKernel` | Distanze squared tra coppie di punti |
| `ComputeTriCentroidZKernel` | Centroide Z per ogni triangolo |
| `ZPlaneMaskKernel` | Maschera above/below per Z-split |
| `TrilinearResliceKernel` | Interpolazione trilineare per reslice |

---

## File modificati

### `src/OrthoPlanner.Core/OrthoPlanner.Core.csproj`
Aggiunte dipendenze NuGet:
```xml
<PackageReference Include="ILGPU" Version="1.5.1" />
<PackageReference Include="ILGPU.Algorithms" Version="1.5.1" />
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
<Optimize>true</Optimize>
```

ILGPU (https://ilgpu.net/) supporta CUDA, OpenCL e CPU — nessun altro SDK richiesto
per il fallback CPU. Per CUDA serve installare il CUDA Toolkit di NVIDIA.

---

### `src/OrthoPlanner.Core/Imaging/MarchingCubes.cs`

| Aspetto | Prima | Dopo |
|---------|-------|------|
| Cube-index computation | Triple `for z/y/x` sequenziale | **GPU kernel** (ILGPU) |
| Vertex interpolation | `for z` sequenziale + `List<float[]>` con allocazioni | **`Parallel.For`** su Z + `ConcurrentBag` thread-local |
| Fallback | — | `ExtractCpuParallel` se GPU non disponibile |

**Speedup atteso:** 5–20× su volumi CT standard (512×512×300).

---

### `src/OrthoPlanner.Core/Segmentation/SegmentationEngine.cs`

| Metodo | Prima | Dopo |
|--------|-------|------|
| `ThresholdSegment` (plain) | Triple `for` sequenziale | **GPU kernel** → fallback `Parallel.For` |
| `ThresholdSegment` (thin-bone) | Triple `for` sequenziale | `Parallel.For` su Z |
| `MorphologicalClosing` | — (non era parallelizzato) | **GPU dilation+erosion kernels** → fallback `Parallel.For` |
| `SmoothLabelMask` | `for z` sequenziale | **GPU majority-vote kernel** → fallback `Parallel.For` |
| `ExtractSegmentMesh` | Chiamata MC sequenziale | Masked array costruito con `Parallel.For` + MC ottimizzato |
| `ResliceVolume` | Solo `Parallel.For` su Z | **GPU trilinear kernel** → fallback `Parallel.For` |
| BFS (RegionGrow, ConnectedComp) | Invariati — sequenziali per natura | Invariati |

---

### `src/OrthoPlanner.Core/Geometry/IcpAligner.cs`

| Aspetto | Prima | Dopo |
|---------|-------|------|
| Target culling (nearest-neighbor) | `for` sequenziale su tutti i target | **`Parallel.For`** |
| Per-iteration NN search | `for` sequenziale su source points | **`Parallel.For`** |
| Step transform application | `for` sequenziale | **`Parallel.For`** |
| `TransformVertices` | `for` sequenziale | **`Parallel.For`** |
| Array correnti sorgente | `double[nSrc, 3]` (cache-unfriendly) | `double[nSrc * 3]` flat (cache-friendly) |

---

### `src/OrthoPlanner.Core/Geometry/MeshOps.cs`

| Metodo | Prima | Dopo |
|--------|-------|------|
| `SubtractByProximity` | `for` sequenziale | **`Parallel.For`** con `bool[] keepFlags` |
| `SplitByZPlane` | `for` sequenziale | **`Parallel.For`** |
| `ClipToBoundingBox` | `for` sequenziale | **`Parallel.For`** |
| `ExcludeBoundingBox` | `for` sequenziale | **`Parallel.For`** |
| `SubtractByArchVolume` | `for` sequenziale | **`Parallel.For`** |
| `CleanAndMergeDentalCast` | Parzialmente ottimizzato (KdTree interno) | Invariato — logica complessa con dipendenze |
| `LabelConnectedComponents` | BFS sequenziale | Invariato — grafo con dipendenze dati |

---

## Requisiti runtime

| Componente | Requisito |
|------------|-----------|
| .NET | 8.0+ |
| CUDA (opzionale) | CUDA Toolkit 11.x o 12.x + driver NVIDIA ≥ 450 |
| OpenCL (opzionale) | Driver AMD/Intel/NVIDIA con supporto OpenCL 1.2+ |
| CPU fallback | Sempre disponibile, nessun requisito aggiuntivo |

**Per verificare quale acceleratore viene usato**, aggiungere al log di avvio:
```csharp
var gpu = GpuContext.Instance;
Logger.Info($"[GPU] Using: {gpu.DeviceName} (GPU available: {gpu.IsGpuAvailable})");
```

---

## Pattern CPU-first usato per BFS e algoritmi con dipendenze dati

I metodi BFS (RegionGrow, ConnectedComponents) sono **intrinsecamente sequenziali** per natura
del grafo — ogni voxel processato aggiorna lo stato che influenza i vicini. Non sono
parallelizzabili senza ristrutturazioni architetturali profonde (es. parallel BFS con wavefronts).
Questi sono stati lasciati invariati.

---

## Compatibilità API

**Tutte le signature pubbliche sono identiche all'originale.** Nessuna modifica necessaria
al codice chiamante (ViewModel, finestre WPF, ecc.).
