using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using LDraw;

namespace LDraw.Editor
{
    public static class LDrawPartRefreshEvents
    {
        public static event Action<IReadOnlyList<string>> PartsRebuilt;

        public static void RaisePartsRebuilt(IReadOnlyList<string> partNumbers)
        {
            PartsRebuilt?.Invoke(partNumbers);
        }
    }

    [InitializeOnLoad]
    static class LDrawPartMeshSync
    {
        const string PartCatalogAssetPath = "Assets/LDraw/Resources/LDrawPartCatalog.asset";

        static LDrawPartMeshSync()
        {
            LDrawPartRefreshEvents.PartsRebuilt += RefreshPartsInEditor;
        }

        static void RefreshPartsInEditor(IReadOnlyList<string> rebuiltPartNumbers)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LDrawPartCatalog>(PartCatalogAssetPath);
            if (catalog == null)
            {
                return;
            }

            HashSet<string> filter = null;
            if (rebuiltPartNumbers != null && rebuiltPartNumbers.Count > 0)
            {
                filter = new HashSet<string>(rebuiltPartNumbers, StringComparer.OrdinalIgnoreCase);
            }

            int refreshedCount = 0;
            var parts = Resources.FindObjectsOfTypeAll<LDrawPart>();
            foreach (var part in parts)
            {
                if (part == null || EditorUtility.IsPersistent(part))
                    continue;

                if (string.IsNullOrWhiteSpace(part.PartNumber))
                    continue;

                if (filter != null && !filter.Contains(part.PartNumber))
                    continue;

                var entry = catalog.GetPart(part.PartNumber);
                if (entry == null || string.IsNullOrWhiteSpace(entry.MeshAssetPath))
                    continue;

                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(entry.MeshAssetPath);
                if (mesh == null)
                    continue;

                bool changed = false;

                var meshFilter = part.GetComponent<MeshFilter>();
                if (meshFilter == null)
                {
                    meshFilter = part.gameObject.AddComponent<MeshFilter>();
                    changed = true;
                }

                if (meshFilter.sharedMesh != mesh)
                {
                    meshFilter.sharedMesh = mesh;
                    EditorUtility.SetDirty(meshFilter);
                    changed = true;
                }

                var meshCollider = part.GetComponent<MeshCollider>();
                if (meshCollider != null && meshCollider.sharedMesh != mesh)
                {
                    meshCollider.sharedMesh = mesh;
                    EditorUtility.SetDirty(meshCollider);
                    changed = true;
                }

                var meshRenderer = part.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    Mesh studlessMesh = null;
                    if (!string.IsNullOrWhiteSpace(entry.StudlessLodMeshAssetPath))
                    {
                        studlessMesh = AssetDatabase.LoadAssetAtPath<Mesh>(entry.StudlessLodMeshAssetPath);
                    }

                    bool hadLodGroup = part.GetComponent<LODGroup>() != null;
                    LDrawPartLodUtility.ConfigureStudlessLod(part.gameObject, studlessMesh, meshRenderer.sharedMaterials);
                    if (studlessMesh != null || hadLodGroup)
                    {
                        changed = true;
                    }
                }

                var blockBrick = part.GetComponent<BlockBrick>();
                if (blockBrick != null && entry.Connections != null)
                {
                    var conn = entry.Connections;
                    if (conn.Types != null && conn.Positions != null && conn.Directions != null &&
                        conn.Positions.Length >= conn.Types.Length * 3 &&
                        conn.Directions.Length >= conn.Types.Length * 3)
                    {
                        blockBrick.SetConnectionPointsFromData(
                            conn.Types,
                            conn.Positions,
                            conn.Directions,
                            conn.Depths);
                        EditorUtility.SetDirty(blockBrick);
                        changed = true;
                    }
                }

                if (changed)
                {
                    EditorUtility.SetDirty(part);
                    if (part.gameObject.scene.IsValid())
                    {
                        EditorSceneManager.MarkSceneDirty(part.gameObject.scene);
                    }
                    refreshedCount++;
                }
            }

            if (refreshedCount > 0)
            {
                Debug.Log($"Refreshed {refreshedCount} LDrawPart instance(s) after mesh rebuild.");
            }
        }
    }
}
