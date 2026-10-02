using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LDraw
{
    public struct SubmeshInfo
    {
        public int ColorCode;
        public string TextureName;  // null for untextured submeshes
    }

    public static class LDrawMeshBuilder
    {
        // Helper class to store per-group geometry data
        class GroupGeometry
        {
            public List<Vector3> Vertices = new List<Vector3>();
            public List<Vector2> UVs = new List<Vector2>();
        }

        /// <summary>
        /// Builds a Unity Mesh from a flattened list of LDraw triangles.
        /// Handles coordinate conversion, winding order, vertex welding, and normal generation.
        /// </summary>
        public static Mesh BuildMesh(List<LDrawTriangle> triangles, string meshName = "LDrawMesh")
        {
            if (triangles == null || triangles.Count == 0)
                return null;

            // Step 1: Convert all vertices from LDraw to Unity coordinates
            // and build raw vertex/index arrays
            int vertexCount = triangles.Count * 3;
            var rawVertices = new Vector3[vertexCount];
            var rawIndices = new int[vertexCount];

            for (int t = 0; t < triangles.Count; t++)
            {
                var tri = triangles[t];
                int baseIdx = t * 3;

                rawVertices[baseIdx] = LDrawCoordinates.ConvertPosition(tri.V1);
                rawVertices[baseIdx + 1] = LDrawCoordinates.ConvertPosition(tri.V2);
                rawVertices[baseIdx + 2] = LDrawCoordinates.ConvertPosition(tri.V3);

                // The (x, -y, -z) coordinate conversion already transforms
                // LDraw CCW (right-handed) into Unity CW (left-handed) winding
                rawIndices[baseIdx] = baseIdx;
                rawIndices[baseIdx + 1] = baseIdx + 1;
                rawIndices[baseIdx + 2] = baseIdx + 2;
            }

            // Step 3: Weld vertices
            LDrawMeshOptimizer.WeldVertices(rawVertices, rawIndices,
                out var weldedVertices, out var weldedIndices);

            // Step 4: Split hard edges and generate smooth normals
            LDrawMeshOptimizer.SplitHardEdges(weldedVertices, weldedIndices,
                out var finalVertices, out var finalIndices, out var finalNormals);

            // Step 5: Build the mesh
            var mesh = new Mesh();
            mesh.name = meshName;

            // Use 32-bit indices if needed
            if (finalVertices.Length > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.vertices = finalVertices;
            mesh.normals = finalNormals;
            mesh.triangles = finalIndices;

            mesh.RecalculateBounds();
            mesh.Optimize();

            return mesh;
        }

        /// <summary>
        /// Builds a mesh with per-color submeshes for multi-material support.
        /// Returns the mesh and a list of color codes in submesh order.
        /// </summary>
        public static (Mesh mesh, List<int> colorCodes) BuildMultiColorMesh(
            List<LDrawTriangle> triangles, string meshName = "LDrawMesh")
        {
            var (mesh, submeshes) = BuildMultiColorMesh(triangles, new List<LDrawTexturedTriangle>(), meshName);
            if (mesh == null) return (null, null);

            var colorCodes = new List<int>();
            foreach (var info in submeshes)
                colorCodes.Add(info.ColorCode);

            return (mesh, colorCodes);
        }

        /// <summary>
        /// Builds a mesh with per-color and per-texture submeshes for multi-material support.
        /// Returns the mesh and submesh info with color codes and texture names.
        /// </summary>
        public static (Mesh mesh, List<SubmeshInfo> submeshes) BuildMultiColorMesh(
            List<LDrawTriangle> untextured,
            List<LDrawTexturedTriangle> textured,
            string meshName = "LDrawMesh")
        {
            bool hasUntextured = untextured != null && untextured.Count > 0;
            bool hasTextured = textured != null && textured.Count > 0;

            if (!hasUntextured && !hasTextured)
                return (null, null);

            // Remove duplicate and degenerate triangles to prevent rendering artifacts
            if (hasUntextured)
            {
                untextured = RemoveDuplicateTriangles(untextured);
                untextured = RemoveDegenerateTriangles(untextured);
            }
            if (hasTextured)
            {
                textured = RemoveDuplicateTexturedTriangles(textured);
                textured = RemoveDegenerateTexturedTriangles(textured);
            }

            // Group triangles by (color, texture) pairs
            var groups = new Dictionary<(int color, string texture), GroupGeometry>();

            // Add untextured triangles with key = (color, null)
            if (hasUntextured)
            {
                foreach (var tri in untextured)
                {
                    var key = (tri.Color, (string)null);
                    if (!groups.ContainsKey(key))
                    {
                        groups[key] = new GroupGeometry();
                    }

                    var geom = groups[key];
                    geom.Vertices.Add(tri.V1);
                    geom.Vertices.Add(tri.V2);
                    geom.Vertices.Add(tri.V3);

                    // Don't generate UVs yet for untextured geometry
                    // Will generate after welding using smooth vertex normals
                    geom.UVs.Add(Vector2.zero);
                    geom.UVs.Add(Vector2.zero);
                    geom.UVs.Add(Vector2.zero);
                }
            }

            // Add textured triangles with key = (color, textureName)
            if (hasTextured)
            {
                foreach (var tri in textured)
                {
                    var key = (tri.Color, tri.TextureName);
                    if (!groups.ContainsKey(key))
                    {
                        groups[key] = new GroupGeometry();
                    }

                    var geom = groups[key];
                    geom.Vertices.Add(tri.V1);
                    geom.Vertices.Add(tri.V2);
                    geom.Vertices.Add(tri.V3);
                    geom.UVs.Add(tri.UV1);
                    geom.UVs.Add(tri.UV2);
                    geom.UVs.Add(tri.UV3);
                }
            }

            var allVertices = new List<Vector3>();
            var allNormals = new List<Vector3>();
            var allUVs = new List<Vector2>();
            var submeshIndices = new List<int[]>();
            var submeshInfos = new List<SubmeshInfo>();

            foreach (var kvp in groups)
            {
                var (colorCode, textureName) = kvp.Key;
                var geom = kvp.Value;

                int vertexOffset = allVertices.Count;

                // Convert vertices from LDraw to Unity coordinates
                var rawVerts = new Vector3[geom.Vertices.Count];
                for (int i = 0; i < geom.Vertices.Count; i++)
                    rawVerts[i] = LDrawCoordinates.ConvertPosition(geom.Vertices[i]);

                var rawIdx = new int[geom.Vertices.Count];
                for (int i = 0; i < rawIdx.Length; i++)
                    rawIdx[i] = i;

                Vector3[] weldedVerts, splitVerts, splitNormals;
                int[] weldedIdx, splitIdx;
                Vector2[] splitUVs;

                // For untextured geometry: weld position-only, then generate UVs from smooth normals
                // For textured geometry: preserve UVs from file
                if (textureName == null)
                {
                    // Untextured: weld by position only
                    LDrawMeshOptimizer.WeldVertices(rawVerts, rawIdx,
                        out weldedVerts, out weldedIdx);

                    // Split hard edges and generate smooth normals
                    LDrawMeshOptimizer.SplitHardEdges(weldedVerts, weldedIdx,
                        out splitVerts, out splitIdx, out splitNormals);

                    // NOW generate triplanar UVs using the smooth vertex normals
                    // Note: splitVerts are in Unity space (scaled 0.05x), so we adjust tileScale accordingly
                    splitUVs = new Vector2[splitVerts.Length];
                    for (int i = 0; i < splitVerts.Length; i++)
                    {
                        // Use 0.2 tileScale for Unity-space coords (0.01 * 20 to compensate for 0.05 scale)
                        splitUVs[i] = GenerateTriplanarUV(splitVerts[i], splitNormals[i], tileScale: 0.2f);
                    }
                }
                else
                {
                    // Textured: preserve UVs from file, use UV-aware welding
                    var rawUVs = geom.UVs.ToArray();

                    LDrawMeshOptimizer.WeldVertices(rawVerts, rawIdx, rawUVs,
                        out weldedVerts, out weldedIdx, out var weldedUVs);

                    LDrawMeshOptimizer.SplitHardEdges(weldedVerts, weldedIdx, weldedUVs,
                        out splitVerts, out splitIdx, out splitNormals, out splitUVs);
                }

                // Offset indices
                var offsetIdx = new int[splitIdx.Length];
                for (int j = 0; j < splitIdx.Length; j++)
                    offsetIdx[j] = splitIdx[j] + vertexOffset;

                allVertices.AddRange(splitVerts);
                allNormals.AddRange(splitNormals);
                allUVs.AddRange(splitUVs);
                submeshIndices.Add(offsetIdx);
                submeshInfos.Add(new SubmeshInfo { ColorCode = colorCode, TextureName = textureName });
            }

            var mesh = new Mesh();
            mesh.name = meshName;

            if (allVertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.vertices = allVertices.ToArray();
            mesh.normals = allNormals.ToArray();
            mesh.uv = allUVs.ToArray();
            mesh.subMeshCount = submeshIndices.Count;

            for (int s = 0; s < submeshIndices.Count; s++)
                mesh.SetTriangles(submeshIndices[s], s);

            mesh.RecalculateBounds();
            mesh.Optimize();

            return (mesh, submeshInfos);
        }

        /// <summary>
        /// Removes duplicate triangles that have the same vertices and color.
        /// Prevents normal conflicts and specular highlight flickering.
        /// </summary>
        static List<LDrawTriangle> RemoveDuplicateTriangles(List<LDrawTriangle> triangles)
        {
            const float epsilon = 0.0001f;
            var unique = new List<LDrawTriangle>();
            var seen = new HashSet<string>();

            foreach (var tri in triangles)
            {
                // Create a hash key from color and vertex positions (order-independent)
                var verts = new[] { tri.V1, tri.V2, tri.V3 };
                System.Array.Sort(verts, (a, b) =>
                {
                    int cmp = a.x.CompareTo(b.x);
                    if (cmp != 0) return cmp;
                    cmp = a.y.CompareTo(b.y);
                    if (cmp != 0) return cmp;
                    return a.z.CompareTo(b.z);
                });

                string key = $"{tri.Color}_{RoundVec(verts[0], epsilon)}_{RoundVec(verts[1], epsilon)}_{RoundVec(verts[2], epsilon)}";

                if (!seen.Contains(key))
                {
                    seen.Add(key);
                    unique.Add(tri);
                }
            }

            if (unique.Count < triangles.Count)
                Debug.Log($"Removed {triangles.Count - unique.Count} duplicate triangles");

            return unique;
        }

        /// <summary>
        /// Removes duplicate textured triangles.
        /// </summary>
        static List<LDrawTexturedTriangle> RemoveDuplicateTexturedTriangles(List<LDrawTexturedTriangle> triangles)
        {
            const float epsilon = 0.0001f;
            var unique = new List<LDrawTexturedTriangle>();
            var seen = new HashSet<string>();

            foreach (var tri in triangles)
            {
                var verts = new[] { tri.V1, tri.V2, tri.V3 };
                System.Array.Sort(verts, (a, b) =>
                {
                    int cmp = a.x.CompareTo(b.x);
                    if (cmp != 0) return cmp;
                    cmp = a.y.CompareTo(b.y);
                    if (cmp != 0) return cmp;
                    return a.z.CompareTo(b.z);
                });

                string key = $"{tri.Color}_{tri.TextureName}_{RoundVec(verts[0], epsilon)}_{RoundVec(verts[1], epsilon)}_{RoundVec(verts[2], epsilon)}";

                if (!seen.Contains(key))
                {
                    seen.Add(key);
                    unique.Add(tri);
                }
            }

            if (unique.Count < triangles.Count)
                Debug.Log($"Removed {triangles.Count - unique.Count} duplicate textured triangles");

            return unique;
        }

        static string RoundVec(Vector3 v, float epsilon)
        {
            float Snap(float val) => Mathf.Round(val / epsilon) * epsilon;
            return $"{Snap(v.x):F4},{Snap(v.y):F4},{Snap(v.z):F4}";
        }

        /// <summary>
        /// Removes degenerate triangles (zero area, extreme aspect ratio, or invalid normals).
        /// </summary>
        static List<LDrawTriangle> RemoveDegenerateTriangles(List<LDrawTriangle> triangles)
        {
            const float minArea = 0.005f; // Minimum triangle area in LDraw units squared (filters tiny coplanar planes)
            const float maxAspectRatio = 1000f; // Maximum edge length ratio
            var valid = new List<LDrawTriangle>();

            foreach (var tri in triangles)
            {
                // Calculate edge lengths
                float len1 = Vector3.Distance(tri.V1, tri.V2);
                float len2 = Vector3.Distance(tri.V2, tri.V3);
                float len3 = Vector3.Distance(tri.V3, tri.V1);

                // Check for zero-length edges
                if (len1 < 0.0001f || len2 < 0.0001f || len3 < 0.0001f)
                {
                    Debug.LogWarning($"Skipping degenerate triangle with zero-length edge (color {tri.Color})");
                    continue;
                }

                // Calculate triangle area using cross product
                var e1 = tri.V2 - tri.V1;
                var e2 = tri.V3 - tri.V1;
                float area = Vector3.Cross(e1, e2).magnitude * 0.5f;

                if (area < minArea)
                {
                    Debug.LogWarning($"Skipping degenerate triangle with area {area} (color {tri.Color})");
                    continue;
                }

                // Check aspect ratio (detect needle triangles)
                float maxLen = Mathf.Max(len1, Mathf.Max(len2, len3));
                float minLen = Mathf.Min(len1, Mathf.Min(len2, len3));
                if (maxLen / minLen > maxAspectRatio)
                {
                    Debug.LogWarning($"Skipping needle triangle with aspect ratio {maxLen / minLen:F1} (color {tri.Color})");
                    continue;
                }

                valid.Add(tri);
            }

            if (valid.Count < triangles.Count)
                Debug.Log($"Removed {triangles.Count - valid.Count} degenerate triangles");

            return valid;
        }

        /// <summary>
        /// Removes degenerate textured triangles.
        /// </summary>
        static List<LDrawTexturedTriangle> RemoveDegenerateTexturedTriangles(List<LDrawTexturedTriangle> triangles)
        {
            const float minArea = 0.01f; // Minimum triangle area in LDraw units squared
            const float maxAspectRatio = 1000f;
            var valid = new List<LDrawTexturedTriangle>();

            foreach (var tri in triangles)
            {
                float len1 = Vector3.Distance(tri.V1, tri.V2);
                float len2 = Vector3.Distance(tri.V2, tri.V3);
                float len3 = Vector3.Distance(tri.V3, tri.V1);

                if (len1 < 0.0001f || len2 < 0.0001f || len3 < 0.0001f)
                {
                    Debug.LogWarning($"Skipping degenerate textured triangle with zero-length edge");
                    continue;
                }

                var e1 = tri.V2 - tri.V1;
                var e2 = tri.V3 - tri.V1;
                float area = Vector3.Cross(e1, e2).magnitude * 0.5f;

                if (area < minArea)
                {
                    Debug.LogWarning($"Skipping degenerate textured triangle with area {area}");
                    continue;
                }

                float maxLen = Mathf.Max(len1, Mathf.Max(len2, len3));
                float minLen = Mathf.Min(len1, Mathf.Min(len2, len3));
                if (maxLen / minLen > maxAspectRatio)
                {
                    Debug.LogWarning($"Skipping needle textured triangle with aspect ratio {maxLen / minLen:F1}");
                    continue;
                }

                valid.Add(tri);
            }

            if (valid.Count < triangles.Count)
                Debug.Log($"Removed {triangles.Count - valid.Count} degenerate textured triangles");

            return valid;
        }

        /// <summary>
        /// Generates triplanar-mapped UV coordinates for a vertex.
        /// Projects the vertex position along all three axes and blends based on the surface normal.
        /// This provides seamless UVs for complex geometry without manual unwrapping.
        /// </summary>
        /// <param name="vertex">Vertex position in LDraw coordinates</param>
        /// <param name="normal">Surface normal (typically face normal for the triangle)</param>
        /// <param name="tileScale">Scale factor for UV tiling (default 0.01 = 1 texture repeat per 100 LDU)</param>
        static Vector2 GenerateTriplanarUV(Vector3 vertex, Vector3 normal, float tileScale = 0.01f)
        {
            // Calculate blend weights based on absolute normal components
            // Surfaces facing +/-X will use YZ projection, etc.
            Vector3 blendWeights = new Vector3(
                Mathf.Abs(normal.x),
                Mathf.Abs(normal.y),
                Mathf.Abs(normal.z)
            );

            // Normalize weights so they sum to 1
            float sum = blendWeights.x + blendWeights.y + blendWeights.z;
            if (sum > 0.0001f)
                blendWeights /= sum;
            else
                blendWeights = new Vector3(0.333f, 0.333f, 0.333f); // Fallback for degenerate normals

            // Project vertex position along each axis
            // X-axis projection (YZ plane)
            Vector2 uvX = new Vector2(vertex.z, vertex.y) * tileScale;
            // Y-axis projection (XZ plane)
            Vector2 uvY = new Vector2(vertex.x, vertex.z) * tileScale;
            // Z-axis projection (XY plane)
            Vector2 uvZ = new Vector2(vertex.x, vertex.y) * tileScale;

            // Blend the three projections based on surface orientation
            Vector2 blendedUV = uvX * blendWeights.x + uvY * blendWeights.y + uvZ * blendWeights.z;

            return blendedUV;
        }
    }
}
