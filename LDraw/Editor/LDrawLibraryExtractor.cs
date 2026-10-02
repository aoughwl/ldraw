using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace LDraw.Editor
{
    public static class LDrawLibraryExtractor
    {
        static readonly string DefaultZipPath = @"C:\Users\savant\Downloads\complete.zip";
        static readonly string LibraryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LDrawLibrary"));

        public static string GetLibraryRoot() => LibraryRoot;

        public static string GetLDrawRoot() => Path.Combine(LibraryRoot, "ldraw");

        public static bool IsExtracted()
        {
            string partsDir = Path.Combine(GetLDrawRoot(), "parts");
            return Directory.Exists(partsDir) && Directory.GetFiles(partsDir, "*.dat").Length > 100;
        }

        public static void Extract(string zipPath = null)
        {
            zipPath ??= DefaultZipPath;

            if (!File.Exists(zipPath))
            {
                Debug.LogError($"LDraw complete.zip not found at: {zipPath}");
                return;
            }

            if (Directory.Exists(LibraryRoot))
                Directory.Delete(LibraryRoot, true);

            Directory.CreateDirectory(LibraryRoot);

            Debug.Log($"Extracting LDraw library from {zipPath} to {LibraryRoot}...");
            ZipFile.ExtractToDirectory(zipPath, LibraryRoot);
            Debug.Log("LDraw library extraction complete.");
        }

        public static LibraryStats GetStats()
        {
            var stats = new LibraryStats();
            string root = GetLDrawRoot();

            if (!Directory.Exists(root))
                return stats;

            string partsDir = Path.Combine(root, "parts");
            string subpartsDir = Path.Combine(root, "parts", "s");
            string primitivesDir = Path.Combine(root, "p");
            string configPath = Path.Combine(root, "LDConfig.ldr");

            if (Directory.Exists(partsDir))
                stats.PartCount = Directory.GetFiles(partsDir, "*.dat").Length;
            if (Directory.Exists(subpartsDir))
                stats.SubpartCount = Directory.GetFiles(subpartsDir, "*.dat").Length;
            if (Directory.Exists(primitivesDir))
                stats.PrimitiveCount = Directory.GetFiles(primitivesDir, "*.dat", SearchOption.AllDirectories).Length;
            stats.HasColorConfig = File.Exists(configPath);

            return stats;
        }

        public struct LibraryStats
        {
            public int PartCount;
            public int SubpartCount;
            public int PrimitiveCount;
            public bool HasColorConfig;
        }
    }
}
