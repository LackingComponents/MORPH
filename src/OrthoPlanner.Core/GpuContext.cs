using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;
using ILGPU.Runtime.CPU;

namespace OrthoPlanner.Core;

/// <summary>
/// Singleton that manages the ILGPU context and best available accelerator.
/// Priority: CUDA (NVIDIA) → OpenCL (AMD/Intel) → CPU fallback.
/// Dispose once at app shutdown (in App.xaml.cs OnExit).
/// </summary>
public sealed class GpuContext : IDisposable
{
    private static GpuContext? _instance;
    private static readonly object _lock = new();

    public Context Context { get; }
    public Accelerator Accelerator { get; }
    public bool IsGpuAvailable { get; }
    public string DeviceName { get; }

    private GpuContext()
    {
        // ILGPU 1.5.x: backends must be explicitly enabled at context creation
        Context = Context.Create(builder => builder
            .Cuda()
            .OpenCL()
            .CPU());

        // 1. Try CUDA (NVIDIA)
        try
        {
            var cudaDevices = Context.GetCudaDevices();
            if (cudaDevices.Count > 0)
            {
                Accelerator = cudaDevices[0].CreateCudaAccelerator(Context);
                IsGpuAvailable = true;
                DeviceName = $"CUDA: {Accelerator.Name}";
                return;
            }
        }
        catch { /* CUDA not installed or no NVIDIA GPU */ }

        // 2. Try OpenCL (AMD, Intel, NVIDIA without CUDA toolkit)
        try
        {
            var clDevices = Context.GetCLDevices();
            // DeviceCollection<T> in ILGPU 1.5.x does not implement IEnumerable<T>
            // — iterate with index, no LINQ. Pick first GPU, fallback to any device.
            CLDevice? bestDevice = null;
            for (int i = 0; i < clDevices.Count; i++)
            {
                var d = clDevices[i];
                // AcceleratorType.OpenCL == GPU device; CPU-based OpenCL has AcceleratorType.CPU
                if (bestDevice == null || d.AcceleratorType == AcceleratorType.OpenCL)
                    bestDevice = d;
            }
            if (bestDevice != null)
            {
                Accelerator = bestDevice.CreateCLAccelerator(Context);
                IsGpuAvailable = true;
                DeviceName = $"OpenCL: {Accelerator.Name}";
                return;
            }
        }
        catch { /* OpenCL not available */ }

        // 3. CPU fallback — ILGPU still vectorizes with SIMD
        var cpuDevices = Context.GetCPUDevices();
        Accelerator = cpuDevices[0].CreateCPUAccelerator(Context);
        IsGpuAvailable = false;
        DeviceName = $"CPU (fallback): {Accelerator.Name}";
    }

    public static GpuContext Instance
    {
        get
        {
            if (_instance == null)
                lock (_lock)
                    _instance ??= new GpuContext();
            return _instance;
        }
    }

    public void Dispose()
    {
        Accelerator?.Dispose();
        Context?.Dispose();
        _instance = null;
    }
}