using System;
using System.Linq;
using System.Reflection;

class Program
{
    static void Main()
    {
        // Force load HelixToolkit by referencing a known type
        var dummy = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera();
        
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        var asm1 = Assembly.Load("HelixToolkit.Wpf.SharpDX");
        var asm2 = Assembly.Load("HelixToolkit.SharpDX");
        foreach(var asm in new[] { asm1, asm2 })
        {
            foreach(var t in asm.GetTypes().Where(x => x.Name.Contains("EffectsManager")))
            {
                Console.WriteLine("  " + t.FullName);
            }
        }
        builder.AddSphere(new System.Numerics.Vector3(0,0,0), 2f);
        var geom = builder.ToMesh();
        var dxGeom = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(geom);
        Console.WriteLine(dxGeom.GetType().FullName);
    }
}
