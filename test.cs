using System;
using System.Reflection;
using System.Linq;
public class Program {
    public static void Main() {
        Assembly asm = Assembly.LoadFrom(@"C:\Users\Mirko\.nuget\packages\helixtoolkit.wpf\2.25.0\lib\net462\HelixToolkit.Wpf.dll");
        Type t = asm.GetType("HelixToolkit.Wpf.HelixViewport3D");
        foreach(var p in t.GetProperties().Where(p => p.Name.Contains("Gesture"))) {
            Console.WriteLine(p.Name + " : " + p.PropertyType.Name);
        }
    }
}
