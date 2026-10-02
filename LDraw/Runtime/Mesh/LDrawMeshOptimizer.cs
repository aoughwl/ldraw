using System.Collections.Generic;
using UnityEngine;

namespace LDraw
{
    public static class LDrawMeshOptimizer
    {
        const float WeldEpsilon = 0.0001f;
        const float UVEpsilon = 0.05f;  // Larger tolerance to allow triplanar UVs to weld across edges
        const float SmoothingAngleThreshold = 45f;

        /// <summary>
        /// Welds vertices that are within epsilon distance using spatial hashing.
        /// Returns remapped indices and unique vertex list.
        /// </summary>
        public static void WeldVertices(Vector3[] vertices, int[] indices,
            out Vector3[] weldedVertices, out int[] weldedIndices)
        {
            float cellSize = WeldEpsilon * 10f;
            var spatialHash = new Dictionary<long, List<int>>();
            var uniqueVerts = new List<Vector3>();
            int[] remap = new int[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                long hash = SpatialHash(v, cellSize);

                int foundIndex = -1;

                // Check this cell and neighbors
                for (int dx = -1; dx <= 1 && foundIndex < 0; dx++)
                for (int dy = -1; dy <= 1 && foundIndex < 0; dy++)
                for (int dz = -1; dz <= 1 && foundIndex < 0; dz++)
                {
                    long neighborHash = SpatialHashOffset(v, cellSize, dx, dy, dz);
                    if (!spatialHash.TryGetValue(neighborHash, out var bucket)) continue;

                    foreach (int idx in bucket)
                    {
                        if (Vector3.Distance(uniqueVerts[idx], v) < WeldEpsilon)
                        {
                            foundIndex = idx;
                            break;
                        }
                    }
                }

                if (foundIndex >= 0)
                {
                    remap[i] = foundIndex;
                }
                else
                {
                    int newIndex = uniqueVerts.Count;
                    uniqueVerts.Add(v);
                    remap[i] = newIndex;

                    if (!spatialHash.TryGetValue(hash, out var bucket))
                    {
                        bucket = new List<int>();
                        spatialHash[hash] = bucket;
                    }
                    bucket.Add(newIndex);
                }
            }

            weldedVertices = uniqueVerts.ToArray();
            weldedIndices = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++)
                weldedIndices[i] = remap[indices[i]];
        }

        /// <summary>
        /// UV-aware vertex welding. Vertices with same position but different UVs are NOT welded.
        /// This preserves UV seams and prevents texture bleeding.
        /// </summary>
        public static void WeldVertices(Vector3[] vertices, int[] indices, Vector2[] uvs,
            out Vector3[] weldedVertices, out int[] weldedIndices, out Vector2[] weldedUVs)
        {
            if (uvs == null || uvs.Length == 0)
            {
                // No UVs - fall back to position-only welding
                WeldVertices(vertices, indices, out weldedVertices, out weldedIndices);
                weldedUVs = new Vector2[0];
                return;
            }

            float cellSize = WeldEpsilon * 10f;
            var spatialHash = new Dictionary<long, List<int>>();
            var uniqueVerts = new List<Vector3>();
            var uniqueUVs = new List<Vector2>();
            int[] remap = new int[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                var uv = uvs[i];
                long hash = SpatialHash(v, cellSize);

                int foundIndex = -1;

                // Check this cell and neighbors
                for (int dx = -1; dx <= 1 && foundIndex < 0; dx++)
                for (int dy = -1; dy <= 1 && foundIndex < 0; dy++)
                for (int dz = -1; dz <= 1 && foundIndex < 0; dz++)
                {
                    long neighborHash = SpatialHashOffset(v, cellSize, dx, dy, dz);
                    if (!spatialHash.TryGetValue(neighborHash, out var bucket)) continue;

                    foreach (int idx in bucket)
                    {
                        // Check both position AND UV distance
                        if (Vector3.Distance(uniqueVerts[idx], v) < WeldEpsilon &&
                            Vector2.Distance(uniqueUVs[idx], uv) < UVEpsilon)
                        {
                            foundIndex = idx;
                            break;
                        }
                    }
                }

                if (foundIndex >= 0)
                {
                    remap[i] = foundIndex;
                }
                else
                {
                    int newIndex = uniqueVerts.Count;
                    uniqueVerts.Add(v);
                    uniqueUVs.Add(uv);
                    remap[i] = newIndex;

                    if (!spatialHash.TryGetValue(hash, out var bucket))
                    {
                        bucket = new List<int>();
                        spatialHash[hash] = bucket;
                    }
                    bucket.Add(newIndex);
                }
            }

            weldedVertices = uniqueVerts.ToArray();
            weldedUVs = uniqueUVs.ToArray();
            weldedIndices = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++)
                weldedIndices[i] = remap[indices[i]];
        }

        /// <summary>
        /// Generates smooth normals with hard edges at the smoothing angle threshold.
        /// Uses angle-weighted normals for smooth groups.
        /// </summary>
        public static Vector3[] GenerateSmoothNormals(Vector3[] vertices, int[] indices)
        {
            float cosThreshold = Mathf.Cos(SmoothingAngleThreshold * Mathf.Deg2Rad);
            int triCount = indices.Length / 3;

            // Compute face normals
            var faceNormals = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                int i0 = indices[t * 3];
                int i1 = indices[t * 3 + 1];
                int i2 = indices[t * 3 + 2];

                var e1 = vertices[i1] - vertices[i0];
                var e2 = vertices[i2] - vertices[i0];
                faceNormals[t] = Vector3.Cross(e1, e2).normalized;
            }

            // Build adjacency: for each vertex, which triangles reference it
            var vertexTriangles = new Dictionary<int, List<int>>();
            for (int t = 0; t < triCount; t++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int vi = indices[t * 3 + j];
                    if (!vertexTriangles.TryGetValue(vi, out var list))
                    {
                        list = new List<int>();
                        vertexTriangles[vi] = list;
                    }
                    list.Add(t);
                }
            }

            // For each vertex in each triangle, accumulate normals from adjacent
            // triangles whose face normal is within the smoothing threshold
            var normals = new Vector3[vertices.Length];

            for (int t = 0; t < triCount; t++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int vi = indices[t * 3 + j];
                    var fn = faceNormals[t];

                    Vector3 smoothNormal = Vector3.zero;

                    if (vertexTriangles.TryGetValue(vi, out var adjacentTris))
                    {
                        foreach (int adjT in adjacentTris)
                        {
                            if (Vector3.Dot(fn, faceNormals[adjT]) >= cosThreshold)
                            {
                                // Angle-weighted contribution
                                int ai0 = indices[adjT * 3];
                                int ai1 = indices[adjT * 3 + 1];
                                int ai2 = indices[adjT * 3 + 2];

                                float angle = GetVertexAngle(vertices, ai0, ai1, ai2, vi);
                                smoothNormal += faceNormals[adjT] * angle;
                            }
                        }
                    }

                    normals[vi] = smoothNormal.normalized;
                }
            }

            return normals;
        }

        /// <summary>
        /// Splits vertices at hard edges (where smoothing groups differ) so each vertex
        /// can have the correct normal. Returns new vertex/index/normal arrays.
        /// </summary>
        public static void SplitHardEdges(Vector3[] vertices, int[] indices,
            out Vector3[] splitVertices, out int[] splitIndices, out Vector3[] splitNormals)
        {
            float cosThreshold = Mathf.Cos(SmoothingAngleThreshold * Mathf.Deg2Rad);
            int triCount = indices.Length / 3;

            // Compute face normals
            var faceNormals = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                var e1 = vertices[indices[t * 3 + 1]] - vertices[indices[t * 3]];
                var e2 = vertices[indices[t * 3 + 2]] - vertices[indices[t * 3]];
                faceNormals[t] = Vector3.Cross(e1, e2).normalized;
            }

            // Build adjacency
            var vertexTriangles = new Dictionary<int, List<int>>();
            for (int t = 0; t < triCount; t++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int vi = indices[t * 3 + j];
                    if (!vertexTriangles.TryGetValue(vi, out var list))
                    {
                        list = new List<int>();
                        vertexTriangles[vi] = list;
                    }
                    list.Add(t);
                }
            }

            var outVerts = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outIndices = new int[indices.Length];

            // For each triangle, for each vertex, compute the smooth normal
            // considering only adjacent triangles within the smoothing angle
            for (int t = 0; t < triCount; t++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int vi = indices[t * 3 + j];
                    var fn = faceNormals[t];

                    Vector3 smoothNormal = Vector3.zero;

                    if (vertexTriangles.TryGetValue(vi, out var adjacentTris))
                    {
                        foreach (int adjT in adjacentTris)
                        {
                            if (Vector3.Dot(fn, faceNormals[adjT]) >= cosThreshold)
                            {
                                int ai0 = indices[adjT * 3];
                                int ai1 = indices[adjT * 3 + 1];
                                int ai2 = indices[adjT * 3 + 2];
                                float angle = GetVertexAngle(vertices, ai0, ai1, ai2, vi);
                                smoothNormal += faceNormals[adjT] * angle;
                            }
                        }
                    }

                    smoothNormal = smoothNormal.normalized;
                    if (smoothNormal == Vector3.zero)
                        smoothNormal = fn;

                    int newIndex = outVerts.Count;
                    outVerts.Add(vertices[vi]);
                    outNormals.Add(smoothNormal);
                    outIndices[t * 3 + j] = newIndex;
                }
            }

            splitVertices = outVerts.ToArray();
            splitIndices = outIndices;
            splitNormals = outNormals.ToArray();
        }

        /// <summary>
        /// UV-aware hard edge splitting. Splits vertices at hard edges and preserves UV coordinates.
        /// </summary>
        public static void SplitHardEdges(Vector3[] vertices, int[] indices, Vector2[] uvs,
            out Vector3[] splitVertices, out int[] splitIndices, out Vector3[] splitNormals, out Vector2[] splitUVs)
        {
            if (uvs == null || uvs.Length == 0)
            {
                // No UVs - fall back to position-only splitting
                SplitHardEdges(vertices, indices, out splitVertices, out splitIndices, out splitNormals);
                splitUVs = new Vector2[0];
                return;
            }

            float cosThreshold = Mathf.Cos(SmoothingAngleThreshold * Mathf.Deg2Rad);
            int triCount = indices.Length / 3;

            // Compute face normals
            var faceNormals = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                var e1 = vertices[indices[t * 3 + 1]] - vertices[indices[t * 3]];
                var e2 = vertices[indices[t * 3 + 2]] - vertices[indices[t * 3]];
                faceNormals[t] = Vector3.Cross(e1, e2).normalized;
            }

            // Build adjacency
            var vertexTriangles = new Dictionary<int, List<int>>();
            for (int t = 0; t < triCount; t++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int vi = indices[t * 3 + j];
                    if (!vertexTriangles.TryGetValue(vi, out var list))
                    {
                        list = new List<int>();
                        vertexTriangles[vi] = list;
                    }
                    list.Add(t);
                }
            }

            var outVerts = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outUVs = new List<Vector2>();
            var outIndices = new int[indices.Length];

            // For each triangle, for each vertex, compute the smooth normal
            for (int t = 0; t < triCount; t++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int vi = indices[t * 3 + j];
                    var fn = faceNormals[t];

                    Vector3 smoothNormal = Vector3.zero;

                    if (vertexTriangles.TryGetValue(vi, out var adjacentTris))
                    {
                        foreach (int adjT in adjacentTris)
                        {
                            if (Vector3.Dot(fn, faceNormals[adjT]) >= cosThreshold)
                            {
                                int ai0 = indices[adjT * 3];
                                int ai1 = indices[adjT * 3 + 1];
                                int ai2 = indices[adjT * 3 + 2];
                                float angle = GetVertexAngle(vertices, ai0, ai1, ai2, vi);
                                smoothNormal += faceNormals[adjT] * angle;
                            }
                        }
                    }

                    smoothNormal = smoothNormal.normalized;
                    if (smoothNormal == Vector3.zero)
                        smoothNormal = fn;

                    int newIndex = outVerts.Count;
                    outVerts.Add(vertices[vi]);
                    outNormals.Add(smoothNormal);
                    outUVs.Add(uvs[vi]);
                    outIndices[t * 3 + j] = newIndex;
                }
            }

            splitVertices = outVerts.ToArray();
            splitIndices = outIndices;
            splitNormals = outNormals.ToArray();
            splitUVs = outUVs.ToArray();
        }

        static float GetVertexAngle(Vector3[] vertices, int i0, int i1, int i2, int vertexIndex)
        {
            Vector3 v, e1, e2;

            if (vertexIndex == i0)
            {
                v = vertices[i0]; e1 = vertices[i1] - v; e2 = vertices[i2] - v;
            }
            else if (vertexIndex == i1)
            {
                v = vertices[i1]; e1 = vertices[i0] - v; e2 = vertices[i2] - v;
            }
            else
            {
                v = vertices[i2]; e1 = vertices[i0] - v; e2 = vertices[i1] - v;
            }

            float mag1 = e1.magnitude;
            float mag2 = e2.magnitude;
            if (mag1 < 1e-8f || mag2 < 1e-8f) return 0f;

            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(e1 / mag1, e2 / mag2), -1f, 1f));
        }

        static long SpatialHash(Vector3 v, float cellSize)
        {
            int x = Mathf.FloorToInt(v.x / cellSize);
            int y = Mathf.FloorToInt(v.y / cellSize);
            int z = Mathf.FloorToInt(v.z / cellSize);
            return ((long)x * 73856093L) ^ ((long)y * 19349669L) ^ ((long)z * 83492791L);
        }

        static long SpatialHashOffset(Vector3 v, float cellSize, int dx, int dy, int dz)
        {
            int x = Mathf.FloorToInt(v.x / cellSize) + dx;
            int y = Mathf.FloorToInt(v.y / cellSize) + dy;
            int z = Mathf.FloorToInt(v.z / cellSize) + dz;
            return ((long)x * 73856093L) ^ ((long)y * 19349669L) ^ ((long)z * 83492791L);
        }
    }
}
