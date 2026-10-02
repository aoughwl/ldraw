using UnityEngine;
using UnityEditor;
using System.IO;

namespace LDraw.Editor
{
    public static class LDrawTextureImporter
    {
        const string SourcePath = "LDrawLibrary/ldraw/parts/textures";
        const string DestPath = "Assets/LDraw/GeneratedTextures";

        /// <summary>
        /// Imports all PNG textures from the LDraw library into the Unity project.
        /// Copies files from LDrawLibrary/ldraw/parts/textures to Assets/LDraw/GeneratedTextures.
        /// </summary>
        public static void ImportAllTextures()
        {
            if (!Directory.Exists(SourcePath))
            {
                Debug.LogError($"Texture source path not found: {SourcePath}");
                return;
            }

            if (!Directory.Exists(DestPath))
                Directory.CreateDirectory(DestPath);

            var sourceFiles = Directory.GetFiles(SourcePath, "*.png");
            int imported = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var sourcePath in sourceFiles)
                {
                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = Path.Combine(DestPath, fileName);

                    File.Copy(sourcePath, destPath, overwrite: true);
                    imported++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // Import all at once
            AssetDatabase.Refresh();

            // Set import settings for all textures
            foreach (var sourcePath in sourceFiles)
            {
                string fileName = Path.GetFileName(sourcePath);
                string assetPath = $"{DestPath}/{fileName}";

                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    importer.sRGBTexture = true;
                    importer.maxTextureSize = 2048;
                    importer.textureCompression = TextureImporterCompression.Compressed;
                    importer.alphaIsTransparency = true;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.SaveAndReimport();
                }
            }

            Debug.Log($"Imported {imported} textures to {DestPath}");
        }

        /// <summary>
        /// Loads a texture by name from the GeneratedTextures directory.
        /// Returns null if the texture is not found.
        /// </summary>
        public static Texture2D LoadTexture(string textureName)
        {
            string path = $"{DestPath}/{textureName}";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
                Debug.LogWarning($"Texture not found: {textureName} at {path}. Run Window > LDraw > Importer > Import Textures first.");
            return tex;
        }

        /// <summary>
        /// Gets the number of imported textures.
        /// </summary>
        public static int GetImportedTextureCount()
        {
            if (!Directory.Exists(DestPath))
                return 0;
            return Directory.GetFiles(DestPath, "*.png").Length;
        }
    }
}
