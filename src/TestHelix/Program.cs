using System;
using System.Linq;
using HelixToolkit.Wpf.SharpDX;

class Program {
    static void Main() {
        var t = typeof(ViewportExtensions);
        foreach(var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)) {
            if(m.Name.Contains("Zoom")) {
                Console.WriteLine($"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");
            }
        }
    }
}
