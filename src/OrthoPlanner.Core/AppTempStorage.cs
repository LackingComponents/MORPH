using System;
using System.IO;

namespace OrthoPlanner.Core;

public static class AppTempStorage
{
    public static string TempDirectory { get; }

    static AppTempStorage()
    {
        TempDirectory = Path.Combine(Path.GetTempPath(), "OrthoPlanner");
    }

    /// <summary>
    /// Ensures the temporary directory exists and clears any existing files from previous sessions.
    /// Call this once at application startup.
    /// </summary>
    public static void Initialize()
    {
        try
        {
            if (Directory.Exists(TempDirectory))
            {
                // Delete all files in the temp directory
                var files = Directory.GetFiles(TempDirectory);
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Ignore files that are locked or cannot be deleted
                    }
                }
            }
            else
            {
                Directory.CreateDirectory(TempDirectory);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize temp storage: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates a unique temporary file path within the OrthoPlanner temp directory.
    /// </summary>
    public static string GetTempFilePath(string extension = ".tmp")
    {
        if (!Directory.Exists(TempDirectory))
        {
            Directory.CreateDirectory(TempDirectory);
        }
        return Path.Combine(TempDirectory, Guid.NewGuid().ToString("N") + extension);
    }
}
