using System;
using System.Collections.Generic;
using UnityEngine;

namespace LDraw
{
    /// <summary>
    /// Component attached to spawned LDraw parts. Tracks part info and
    /// per-submesh color assignments. Each submesh corresponds to a color
    /// group from the original LDraw file.
    /// </summary>
    public class LDrawPart : MonoBehaviour
    {
        const float DefaultStudLodTransitionHeight = 80f;

        static LDrawPartCatalog _catalogCache;
        static readonly Dictionary<string, Mesh> _studlessMeshCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);

        [Header("Part Info")]
        public string PartNumber;
        public string Description;

        [Header("Submesh Colors")]
        [Tooltip("One entry per submesh, each with its LDraw color code.")]
        public SubmeshColor[] Submeshes = Array.Empty<SubmeshColor>();

        [Serializable]
        public class SubmeshColor
        {
            [Tooltip("LDraw color code from the original .dat file.")]
            public int OriginalColorCode;
            [Tooltip("Currently assigned LDraw color code.")]
            public int CurrentColorCode;
            [Tooltip("Texture name if this submesh uses a texture, null otherwise.")]
            public string TextureName;
            [Tooltip("Display name for this submesh slot.")]
            public string Label;
        }

        void Awake()
        {
            EnsureStudLodConfigured();
        }

        /// <summary>
        /// Applies current color assignments to the MeshRenderer materials.
        /// </summary>
        public void ApplyColors()
        {
            var matLib = Resources.Load<LDrawMaterialLibrary>("LDrawMaterialLibrary");
            if (matLib == null)
            {
                Debug.LogError("LDrawMaterialLibrary not found in Resources.");
                return;
            }

            var renderer = GetComponent<MeshRenderer>();
            if (renderer == null) return;

            var mats = new Material[Submeshes.Length];
            for (int i = 0; i < Submeshes.Length; i++)
            {
                // Use textured material if texture name is specified
                var mat = matLib.GetMaterial(Submeshes[i].CurrentColorCode, Submeshes[i].TextureName);
                if (mat == null)
                {
                    // Fallback: try color 0 (black)
                    mat = matLib.GetMaterial(0);
                }
                mats[i] = mat;
            }

            renderer.sharedMaterials = mats;
            EnsureStudLodConfigured();
        }

        /// <summary>
        /// Changes the color for a specific submesh and applies it.
        /// </summary>
        public void SetSubmeshColor(int submeshIndex, int colorCode)
        {
            if (submeshIndex < 0 || submeshIndex >= Submeshes.Length) return;
            Submeshes[submeshIndex].CurrentColorCode = colorCode;
            ApplyColors();
        }

        /// <summary>
        /// Changes all submeshes to the given color.
        /// </summary>
        public void SetAllColors(int colorCode)
        {
            for (int i = 0; i < Submeshes.Length; i++)
            {
                Submeshes[i].CurrentColorCode = colorCode;
            }
            ApplyColors();
        }

        public void EnsureStudLodConfigured()
        {
            if (string.IsNullOrWhiteSpace(PartNumber))
                return;

            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                return;

            if (_catalogCache == null)
                _catalogCache = Resources.Load<LDrawPartCatalog>("LDrawPartCatalog");
            if (_catalogCache == null)
                return;

            var entry = _catalogCache.GetPart(PartNumber);
            if (entry == null || string.IsNullOrWhiteSpace(entry.StudlessLodMeshAssetPath))
                return;

            var studlessMesh = ResolveStudlessMesh(entry.StudlessLodMeshAssetPath);
            if (studlessMesh == null)
                return;

            LDrawPartLodUtility.ConfigureStudlessLod(
                gameObject,
                studlessMesh,
                meshRenderer.sharedMaterials,
                DefaultStudLodTransitionHeight);
        }

        static Mesh ResolveStudlessMesh(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            if (_studlessMeshCache.TryGetValue(assetPath, out var cached) && cached != null)
                return cached;

            var meshName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            Mesh mesh = Resources.Load<Mesh>(meshName);

            #if UNITY_EDITOR
            if (mesh == null)
                mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            #endif

            if (mesh != null)
                _studlessMeshCache[assetPath] = mesh;

            return mesh;
        }
    }
}














