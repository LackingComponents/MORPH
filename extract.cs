using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        string path = @"C:\Users\Mirko\.gemini\antigravity\brain\f8e1514a-9dd9-4678-9fe5-23d0bcfa4706\.system_generated\logs\overview.txt";
        if (!File.Exists(path)) { Console.WriteLine("Not found"); return; }
        
        string[] lines = File.ReadAllLines(path);
        StringBuilder sb = new StringBuilder();
        bool inBlock = false;
        
        // Find the last occurrences of MainWindow.xaml in tool calls or responses
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("MainWindow.xaml"))
            {
                // Grab surrounding 100 lines for context
                sb.AppendLine("=== MATCH AT LINE " + i + " ===");
                int start = Math.Max(0, i - 100);
                int end = Math.Min(lines.Length, i + 500);
                for (int j = start; j < end; j++)
                {
                    sb.AppendLine(lines[j]);
                }
                sb.AppendLine("==================================");
                i = end;
            }
        }
        
        File.WriteAllText("extracted.txt", sb.ToString());
        Console.WriteLine("Done. Wrote extracted.txt, size: " + new FileInfo("extracted.txt").Length);
    }
}
