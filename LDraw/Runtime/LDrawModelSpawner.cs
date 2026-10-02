using UnityEngine;
using LDraw.Mods;

// Note: BlockBrick is in global namespace, not LDraw namespace

namespace LDraw
{
    /// <summary>
    /// Configuration for spawning LDraw parts with building system support.
    /// </summary>
    [System.Serializable]
    public class LDrawSpawnConfig
    {
        public bool addBuildingComponents = true;  // Add BlockBrick, Rigidbody, Collider
        public bool enableGravity = true;          // Enable gravity on spawned bricks
        public float studlessLodTransitionHeight = 80f; // World-space switch distance to studless LOD

        public static LDrawSpawnConfig Default => new LDrawSpawnConfig
        {
            addBuildingComponents = true,
            enableGravity = true,
            studlessLodTransitionHeight = 80f
        };

        public static LDrawSpawnConfig StaticTerrain => new LDrawSpawnConfig
        {
            addBuildingComponents = true,
            enableGravity = false,
            studlessLodTransitionHeight = 80f
        };
    }

    /// <summary>
    /// Runtime utility for spawning LDraw parts as GameObjects.
    /// </summary>
    public static class LDrawModelSpawner
    {
        /// <summary>
        /// Spawns an LDraw part at the given position and rotation with default config.
        /// </summary>
        public static GameObject SpawnPart(string partNumber, int mainColorCode,
            Vector3 position, Quaternion rotation)
        {
            return SpawnPart(partNumber, mainColorCode, position, rotation, LDrawSpawnConfig.Default);
        }

        /// <summary>
        /// Spawns an LDraw part at the given position and rotation with custom configuration.
        /// The part gets a MeshFilter, MeshRenderer, LDrawPart component, and optionally building components.
        /// mainColorCode: -1 = use .dat file colors, otherwise color 16 submeshes use mainColorCode.
        /// </summary>
        public static GameObject SpawnPart(string partNumber, int mainColorCode,
            Vector3 position, Quaternion rotation, LDrawSpawnConfig config)
        {
            var catalog = Resources.Load<LDrawPartCatalog>("LDrawPartCatalog");
            if (catalog == null)
            {
                Debug.LogError("LDrawPartCatalog not found in Resources.");
                return null;
            }

            var entry = catalog.GetPart(partNumber);
            if (entry == null)
            {
                Debug.LogError($"Part {partNumber} not found in catalog.");
                return null;
            }

            var matLib = Resources.Load<LDrawMaterialLibrary>("LDrawMaterialLibrary");
            if (matLib == null)
            {
                Debug.LogError("LDrawMaterialLibrary not found in Resources.");
                return null;
            }

            // Load mesh
            var mesh = Resources.Load<Mesh>(MeshResourcePath(entry.MeshAssetPath));
            if (mesh == null)
            {
                // Try loading via asset path directly (editor only)
                #if UNITY_EDITOR
                mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(entry.MeshAssetPath);
                #endif
            }
            if (mesh == null)
            {
                RuntimePartRegistry.TryGetMesh(partNumber, out mesh);
            }
            if (mesh == null)
            {
                Debug.LogError($"Could not load mesh for part {partNumber} at {entry.MeshAssetPath}");
                return null;
            }

            // Create GameObject
            var go = new GameObject($"LDraw_{partNumber}_{entry.Description}");
            go.transform.position = position;
            go.transform.rotation = rotation;

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();

            // Setup LDrawPart component with submesh color info
            var ldrawPart = go.AddComponent<LDrawPart>();
            ldrawPart.PartNumber = entry.PartNumber;
            ldrawPart.Description = entry.Description;

            int submeshCount = entry.SubmeshColorCodes != null ? entry.SubmeshColorCodes.Length : 1;
            ldrawPart.Submeshes = new LDrawPart.SubmeshColor[submeshCount];

            var colorTable = Resources.Load<LDrawColorTable>("LDrawColorTable");

            for (int i = 0; i < submeshCount; i++)
            {
                int originalCode = entry.SubmeshColorCodes != null ? entry.SubmeshColorCodes[i] : 16;

                string textureName = (entry.SubmeshTextureNames != null && i < entry.SubmeshTextureNames.Length)
                    ? entry.SubmeshTextureNames[i]
                    : null;

                // Determine assigned color based on mainColorCode
                int assignedCode;
                if (!string.IsNullOrEmpty(textureName))
                {
                    assignedCode = originalCode;
                }
                else if (mainColorCode == -1)
                {
                    assignedCode = originalCode;
                }
                else
                {
                    assignedCode = mainColorCode;
                }

                string label = $"Submesh {i}";
                if (colorTable != null)
                {
                    var colorEntry = colorTable.GetColor(originalCode);
                    if (colorEntry != null) label = colorEntry.Name;
                }

                if (!string.IsNullOrEmpty(textureName))
                    label += $" (Textured: {textureName})";

                ldrawPart.Submeshes[i] = new LDrawPart.SubmeshColor
                {
                    OriginalColorCode = originalCode,
                    CurrentColorCode = assignedCode,
                    TextureName = textureName,
                    Label = label
                };
            }

            ldrawPart.ApplyColors();

            if (!string.IsNullOrWhiteSpace(entry.StudlessLodMeshAssetPath))
            {
                var lodMesh = Resources.Load<Mesh>(MeshResourcePath(entry.StudlessLodMeshAssetPath));
                if (lodMesh == null)
                {
                    #if UNITY_EDITOR
                    lodMesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(entry.StudlessLodMeshAssetPath);
                    #endif
                }

                if (lodMesh != null)
                {
                    LDrawPartLodUtility.ConfigureStudlessLod(go, lodMesh, meshRenderer.sharedMaterials,
                        config.studlessLodTransitionHeight);
                }
            }

            // Add building system components if enabled
            if (config.addBuildingComponents)
            {
                AddBuildingComponents(go, entry, config);
            }

            return go;
        }

        /// <summary>
        /// Add building system components (BlockBrick, Rigidbody, Collider) to a spawned part.
        /// Connection points come from parsed LDraw data stored in the catalog.
        /// </summary>
        private static void AddBuildingComponents(GameObject go, LDrawPartCatalog.PartEntry entry, LDrawSpawnConfig config)
        {
            // Add collider (use mesh bounds to estimate size)
            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                var bounds = meshFilter.sharedMesh.bounds;
                var boxCollider = go.AddComponent<BoxCollider>();
                boxCollider.center = bounds.center;
                boxCollider.size = bounds.size;
            }
            else
            {
                var boxCollider = go.AddComponent<BoxCollider>();
                boxCollider.size = Vector3.one;
            }

            // Add rigidbody
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = config.enableGravity;
            rb.linearDamping = 1f;
            rb.angularDamping = 5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            if (!config.enableGravity)
            {
                rb.isKinematic = true;
            }

            // Add BlockBrick component
            BlockBrick blockBrick = go.AddComponent<BlockBrick>();
            blockBrick.enableGravity = config.enableGravity;

            // Set connection points from parsed LDraw data
            if (entry.Connections != null && entry.Connections.Count > 0)
            {
                blockBrick.SetConnectionPointsFromData(
                    entry.Connections.Types,
                    entry.Connections.Positions,
                    entry.Connections.Directions,
                    entry.Connections.Depths);
            }
            else
            {
                Debug.LogWarning($"No connection data for part {entry.PartNumber}. Rebuild converted parts to generate connection data.");
            }
        }

        /// <summary>
        /// Converts an asset path like "Assets/LDraw/GeneratedMeshes/parts/3001.asset"
        /// to a Resources-relative path. Since meshes aren't in Resources/,
        /// this returns null and we fall back to AssetDatabase in editor.
        /// </summary>
        static string MeshResourcePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            return name;
        }
    }
}














