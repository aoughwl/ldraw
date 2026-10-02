using System;
using System.Collections.Generic;
using UnityEngine;

namespace LDraw
{
    [CreateAssetMenu(fileName = "LDrawMaterialLibrary", menuName = "LDraw/Material Library")]
    public class LDrawMaterialLibrary : ScriptableObject
    {
        [Serializable]
        public class MaterialEntry
        {
            public int ColorCode;
            public Material Material;
        }

        [Serializable]
        public class TexturedMaterialEntry
        {
            public int ColorCode;
            public string TextureName;
            public Material Material;
        }

        public List<MaterialEntry> Materials = new List<MaterialEntry>();
        public List<TexturedMaterialEntry> TexturedMaterials = new List<TexturedMaterialEntry>();

        Dictionary<int, Material> _lookup;
        Dictionary<string, Material> _texturedLookup;

        public void BuildLookup()
        {
            _lookup = new Dictionary<int, Material>(Materials.Count);
            foreach (var entry in Materials)
            {
                if (entry.Material != null)
                    _lookup[entry.ColorCode] = entry.Material;
            }
        }

        public Material GetMaterial(int colorCode)
        {
            if (_lookup == null)
                BuildLookup();
            return _lookup.TryGetValue(colorCode, out var mat) ? mat : null;
        }

        /// <summary>
        /// Gets a material by color code and optional texture name.
        /// If textureName is null or empty, returns the color-only material.
        /// If the textured material is not found, returns the color-only material as fallback.
        /// </summary>
        public Material GetMaterial(int colorCode, string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
                return GetMaterial(colorCode);

            if (_texturedLookup == null)
                BuildTexturedLookup();

            string key = $"{colorCode}_{textureName}";
            if (_texturedLookup.TryGetValue(key, out var mat))
                return mat;

            // Fallback to color-only material
            Debug.LogWarning($"Textured material not found: {key}, using color-only fallback");
            return GetMaterial(colorCode);
        }

        void BuildTexturedLookup()
        {
            _texturedLookup = new Dictionary<string, Material>();
            foreach (var entry in TexturedMaterials)
            {
                if (entry.Material != null)
                {
                    string key = $"{entry.ColorCode}_{entry.TextureName}";
                    _texturedLookup[key] = entry.Material;
                }
            }
        }

        public void SetMaterial(int colorCode, Material mat)
        {
            if (_lookup == null)
                BuildLookup();

            _lookup[colorCode] = mat;

            // Update or add to serialized list
            for (int i = 0; i < Materials.Count; i++)
            {
                if (Materials[i].ColorCode == colorCode)
                {
                    Materials[i].Material = mat;
                    return;
                }
            }

            Materials.Add(new MaterialEntry { ColorCode = colorCode, Material = mat });
        }

        public bool RemoveTexturedMaterial(int colorCode, string textureName)
        {
            if (_texturedLookup == null)
                BuildTexturedLookup();

            string key = $"{colorCode}_{textureName}";
            _texturedLookup.Remove(key);

            for (int i = TexturedMaterials.Count - 1; i >= 0; i--)
            {
                if (TexturedMaterials[i].ColorCode == colorCode &&
                    TexturedMaterials[i].TextureName == textureName)
                {
                    TexturedMaterials.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        public void SetTexturedMaterial(int colorCode, string textureName, Material mat)
        {
            if (_texturedLookup == null)
                BuildTexturedLookup();

            string key = $"{colorCode}_{textureName}";
            _texturedLookup[key] = mat;

            // Update or add to serialized list
            for (int i = 0; i < TexturedMaterials.Count; i++)
            {
                if (TexturedMaterials[i].ColorCode == colorCode &&
                    TexturedMaterials[i].TextureName == textureName)
                {
                    TexturedMaterials[i].Material = mat;
                    return;
                }
            }

            TexturedMaterials.Add(new TexturedMaterialEntry
            {
                ColorCode = colorCode,
                TextureName = textureName,
                Material = mat
            });
        }
    }
}
