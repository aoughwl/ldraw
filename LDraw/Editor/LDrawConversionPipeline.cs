using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using LDraw;

namespace LDraw.Editor
{
    public static class LDrawConversionPipeline
    {
        const string GeneratedMeshesPath = "Assets/LDraw/GeneratedMeshes/parts";
        const string GeneratedMaterialsPath = "Assets/LDraw/GeneratedMaterials";
        const string ResourcesPath = "Assets/LDraw/Resources";
        const string ColorTableAssetPath = "Assets/LDraw/Resources/LDrawColorTable.asset";
        const string MaterialLibraryAssetPath = "Assets/LDraw/Resources/LDrawMaterialLibrary.asset";
        const string PartCatalogAssetPath = "Assets/LDraw/Resources/LDrawPartCatalog.asset";

        public static LDrawColorTable GenerateColorTable()
        {
            string ldrawRoot = LDrawLibraryExtractor.GetLDrawRoot();
            string ldConfigPath = Path.Combine(ldrawRoot, "LDConfig.ldr");

            if (!File.Exists(ldConfigPath))
            {
                Debug.LogError($"LDConfig.ldr not found at {ldConfigPath}. Extract the library first.");
                return null;
            }

            EnsureDirectoryExists(ResourcesPath);

            var colorTable = AssetDatabase.LoadAssetAtPath<LDrawColorTable>(ColorTableAssetPath);
            if (colorTable == null)
            {
                colorTable = ScriptableObject.CreateInstance<LDrawColorTable>();
                AssetDatabase.CreateAsset(colorTable, ColorTableAssetPath);
            }

            colorTable.ParseFromFile(ldConfigPath);
            EditorUtility.SetDirty(colorTable);
            AssetDatabase.SaveAssets();

            return colorTable;
        }

        public static LDrawMaterialLibrary GenerateMaterials(LDrawColorTable colorTable = null)
        {
            if (colorTable == null)
                colorTable = AssetDatabase.LoadAssetAtPath<LDrawColorTable>(ColorTableAssetPath);
            if (colorTable == null)
            {
                Debug.LogError("Color table not found. Generate colors first.");
                return null;
            }

            EnsureDirectoryExists(GeneratedMaterialsPath);
            EnsureDirectoryExists($"{GeneratedMaterialsPath}/Textured");
            EnsureDirectoryExists(ResourcesPath);

            var library = AssetDatabase.LoadAssetAtPath<LDrawMaterialLibrary>(MaterialLibraryAssetPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<LDrawMaterialLibrary>();
                AssetDatabase.CreateAsset(library, MaterialLibraryAssetPath);
            }

            library.Materials.Clear();

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < colorTable.Colors.Count; i++)
                {
                    var colorEntry = colorTable.Colors[i];
                    EditorUtility.DisplayProgressBar("Generating Materials",
                        $"Creating material for {colorEntry.Name} ({i + 1}/{colorTable.Colors.Count})",
                        (float)i / colorTable.Colors.Count);

                    string matPath = $"{GeneratedMaterialsPath}/LDraw_Color_{colorEntry.Code}_{SanitizeName(colorEntry.Name)}.mat";

                    var existingMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (existingMat != null)
                    {
                        library.SetMaterial(colorEntry.Code, existingMat);
                        continue;
                    }

                    var mat = LDrawMaterialFactory.CreateMaterial(colorEntry);
                    if (mat == null) continue;

                    AssetDatabase.CreateAsset(mat, matPath);
                    library.SetMaterial(colorEntry.Code, mat);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();

            Debug.Log($"Generated {library.Materials.Count} materials.");
            return library;
        }

        /// <summary>
        /// Converts a single LDraw part file to a Unity mesh asset with multi-material submeshes.
        /// </summary>
        public static bool ConvertPart(string partFilePath, LDrawFileResolver resolver,
            LDrawParser parser, LDrawPartCatalog catalog)
        {
            string partName = Path.GetFileNameWithoutExtension(partFilePath);
            string meshAssetPath = $"{GeneratedMeshesPath}/{partName}.asset";
            string studlessLodMeshAssetPath = $"{GeneratedMeshesPath}/{partName}_nostud.asset";

            // Check if already converted AND in catalog
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            if (existingMesh != null && catalog.HasPart(partName))
                return true;

            try
            {
                Mesh mesh;
                List<SubmeshInfo> submeshes;
                List<LDrawConnectionReference> connections;
                Mesh studlessLodMesh = null;

                // If mesh already exists, load it and rebuild submesh info from parsing
                if (existingMesh != null)
                {
                    Debug.Log($"Mesh exists for {partName}, updating catalog entry...");
                    mesh = existingMesh;

                    // Re-parse to get submesh info and connections
                    var result = parser.FlattenPartWithConnections(partFilePath);
                    connections = result.connections;
                    AugmentKnownMissingConnections(partName, partFilePath, resolver, connections);
                    (_, submeshes) = LDrawMeshBuilder.BuildMultiColorMesh(result.untextured, result.textured, partName);
                }
                else
                {
                    // Convert from scratch
                    var result = parser.FlattenPartWithConnections(partFilePath);
                    connections = result.connections;
                    AugmentKnownMissingConnections(partName, partFilePath, resolver, connections);
                    bool hasUntextured = result.untextured != null && result.untextured.Count > 0;
                    bool hasTextured = result.textured != null && result.textured.Count > 0;

                    if (!hasUntextured && !hasTextured)
                    {
                        Debug.LogWarning($"No geometry in part {partName}");
                        return false;
                    }

                    (mesh, submeshes) = LDrawMeshBuilder.BuildMultiColorMesh(result.untextured, result.textured, partName);
                    if (mesh == null)
                    {
                        Debug.LogWarning($"Failed to build mesh for {partName}");
                        return false;
                    }

                    EnsureDirectoryExists(GeneratedMeshesPath);
                    AssetDatabase.CreateAsset(mesh, meshAssetPath);
                }

                bool hasStudConnections = connections != null &&
                    connections.Exists(c => c.Type == LDrawConnectionType.Stud);

                // Build/create studless LOD mesh only for parts with stud geometry.
                // This avoids visual holes caused by runtime clipping approaches.
                if (hasStudConnections)
                {
                    var lodResult = parser.FlattenPart(partFilePath, omitStudGeometry: true);
                    bool lodHasGeometry = (lodResult.untextured != null && lodResult.untextured.Count > 0) ||
                                          (lodResult.textured != null && lodResult.textured.Count > 0);
                    if (lodHasGeometry)
                    {
                        var existingLodMesh = AssetDatabase.LoadAssetAtPath<Mesh>(studlessLodMeshAssetPath);
                        if (existingLodMesh != null)
                        {
                            studlessLodMesh = existingLodMesh;
                        }
                        else
                        {
                            (studlessLodMesh, _) = LDrawMeshBuilder.BuildMultiColorMesh(
                                lodResult.untextured, lodResult.textured, partName + "_nostud");
                            if (studlessLodMesh != null)
                            {
                                EnsureDirectoryExists(GeneratedMeshesPath);
                                AssetDatabase.CreateAsset(studlessLodMesh, studlessLodMeshAssetPath);
                            }
                        }
                    }
                    else
                    {
                        AssetDatabase.DeleteAsset(studlessLodMeshAssetPath);
                    }
                }
                else
                {
                    AssetDatabase.DeleteAsset(studlessLodMeshAssetPath);
                }

                // Extract submesh info
                var submeshColorCodes = new int[submeshes.Count];
                var submeshTextureNames = new string[submeshes.Count];
                for (int i = 0; i < submeshes.Count; i++)
                {
                    submeshColorCodes[i] = submeshes[i].ColorCode;
                    submeshTextureNames[i] = submeshes[i].TextureName;

                    // Create textured materials as needed
                    if (!string.IsNullOrEmpty(submeshes[i].TextureName))
                    {
                        CreateTexturedMaterial(submeshes[i].ColorCode, submeshes[i].TextureName);
                    }
                }

                // Convert connection data from LDraw space to Unity space,
                // then post-process: derive anti-studs, duplicate through-holes
                var (connectionData, holeScales) = ConvertConnectionData(connections);
                if (connectionData != null)
                {
                    Debug.Log($"[ConvertPart] {partName}: {connections.Count} raw connections, " +
                              $"types=[{string.Join(",", connectionData.Types)}] " +
                              $"scales=[{(holeScales != null ? string.Join(",", System.Array.ConvertAll(holeScales, s => s.ToString("F4"))) : "null")}]");
                }
                connectionData = PostProcessConnections(connectionData, mesh.bounds, holeScales);

                string description = LDrawParser.GetPartDescription(partFilePath);
                catalog.SetPart(partName, description, meshAssetPath,
                    studlessLodMesh != null ? studlessLodMeshAssetPath : null,
                    mesh.bounds.size,
                    submeshColorCodes, submeshTextureNames, connectionData);

                // Mark catalog as dirty so changes are saved
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error converting part {partName}: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Converts LDraw-space connection references to Unity-space ConnectionData for serialization.
        /// Also returns per-entry transform scales (in Unity units) for primitives whose
        /// direction magnitude encodes hole depth (e.g. axle hole primitives).
        /// </summary>
        static (LDrawPartCatalog.ConnectionData data, float[] scales) ConvertConnectionData(
            List<LDrawConnectionReference> connections)
        {
            if (connections == null || connections.Count == 0)
                return (null, null);

            int count = connections.Count;
            var data = new LDrawPartCatalog.ConnectionData
            {
                Types = new byte[count],
                Positions = new float[count * 3],
                Directions = new float[count * 3]
            };
            var scales = new float[count];

            for (int i = 0; i < count; i++)
            {
                var conn = connections[i];
                data.Types[i] = (byte)conn.Type;

                // Convert position: LDraw (x, y, z) → Unity (x, -y, -z) * 0.05
                Vector3 pos = LDrawCoordinates.ConvertPosition(conn.PositionLDraw);
                data.Positions[i * 3] = pos.x;
                data.Positions[i * 3 + 1] = pos.y;
                data.Positions[i * 3 + 2] = pos.z;

                // Direction is unnormalized from the parser — magnitude encodes
                // the transform scale (hole depth in LDU for axle hole primitives).
                Vector3 rawDir = new Vector3(conn.DirectionLDraw.x, -conn.DirectionLDraw.y, -conn.DirectionLDraw.z);
                float mag = rawDir.magnitude;
                scales[i] = mag * 0.05f; // LDU → Unity units

                Vector3 dir = mag > 0.0001f ? rawDir / mag : Vector3.up;
                data.Directions[i * 3] = dir.x;
                data.Directions[i * 3 + 1] = dir.y;
                data.Directions[i * 3 + 2] = dir.z;
            }

            return (data, scales);
        }

        /// <summary>
        /// Some parts model functional holes using raw geometry instead of connection primitives.
        /// Add missing connection markers for known cases.
        /// </summary>
        static void AugmentKnownMissingConnections(
            string partName, string partFilePath, LDrawFileResolver resolver, List<LDrawConnectionReference> connections)
        {
            if (connections == null || resolver == null || string.IsNullOrEmpty(partFilePath))
                return;

            // 34432 is an alias of 3649 (40T gear). Its perimeter pin holes are modeled by
            // repeated primitive geometry, not explicit peghole/beamhole primitives.
            // Dynamically discover those subpart transforms and synthesize center-placed
            // TechnicHole markers from the subpart's own local hole centers.
            if (partName == "34432" || partName == "3649")
            {
                var ringMarkers = new List<Matrix4x4>();
                CollectPrimitiveInstancesRecursive(
                    partFilePath,
                    Matrix4x4.identity,
                    "4-4ring3.dat",
                    resolver,
                    ringMarkers,
                    0);

                if (ringMarkers.Count == 0)
                    return;

                var derivedCenters = new List<(Vector3 pos, Vector3 dir)>();
                var centerKeys = new HashSet<string>();
                for (int r = 0; r < ringMarkers.Count; r++)
                {
                    Matrix4x4 world = ringMarkers[r];
                    Vector3 p = world.MultiplyPoint3x4(Vector3.zero);

                    // 4-4ring3 size is ~2 LDU in both in-plane axes for this representation.
                    float sx = new Vector3(world.m00, world.m10, world.m20).magnitude;
                    float sy = new Vector3(world.m01, world.m11, world.m21).magnitude;
                    float sz = new Vector3(world.m02, world.m12, world.m22).magnitude;
                    float avgScale = (sx + sy + sz) / 3f;
                    if (avgScale < 1.5f || avgScale > 2.5f)
                        continue;

                    // Markers appear on both +Z/-Z shells; project to center plane.
                    Vector3 center = new Vector3(p.x, p.y, 0f);
                    Vector3 dir = world.MultiplyVector(new Vector3(0f, -1f, 0f));
                    if (dir.sqrMagnitude < 1e-6f)
                        dir = new Vector3(0f, 0f, -1f);
                    dir.Normalize();
                    string key = $"{Mathf.Round(center.x * 100f)},{Mathf.Round(center.y * 100f)}";
                    if (centerKeys.Add(key))
                        derivedCenters.Add((center, dir));
                }

                const float posEps = 0.2f; // LDraw units
                for (int c = 0; c < derivedCenters.Count; c++)
                {
                    Vector3 worldPos = derivedCenters[c].pos;
                    bool exists = false;
                    for (int i = 0; i < connections.Count; i++)
                    {
                        if (connections[i].Type != LDrawConnectionType.TechnicHole)
                            continue;

                        if ((connections[i].PositionLDraw - worldPos).sqrMagnitude <= posEps * posEps)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                    {
                        connections.Add(new LDrawConnectionReference
                        {
                            Type = LDrawConnectionType.TechnicHole,
                            PositionLDraw = worldPos,
                            DirectionLDraw = derivedCenters[c].dir
                        });
                    }
                }
            }
        }

        static void CollectPrimitiveInstancesRecursive(
            string filePath,
            Matrix4x4 parentTransform,
            string targetPrimitiveSuffix,
            LDrawFileResolver resolver,
            List<Matrix4x4> result,
            int depth)
        {
            if (depth > 16 || string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;

            string currentDir = Path.GetDirectoryName(filePath);
            foreach (var raw in File.ReadAllLines(filePath))
            {
                if (!TryParseType1SubfileRef(raw, out var pos, out var basis, out var subfile))
                    continue;

                Matrix4x4 local = Matrix4x4.identity;
                local.m00 = basis.m00; local.m01 = basis.m01; local.m02 = basis.m02; local.m03 = pos.x;
                local.m10 = basis.m10; local.m11 = basis.m11; local.m12 = basis.m12; local.m13 = pos.y;
                local.m20 = basis.m20; local.m21 = basis.m21; local.m22 = basis.m22; local.m23 = pos.z;

                Matrix4x4 world = parentTransform * local;
                string normalizedRef = NormalizeRef(subfile);
                string resolved = resolver.Resolve(subfile, currentDir);
                if (string.IsNullOrEmpty(resolved))
                    continue;

                if (normalizedRef.EndsWith(NormalizeRef(targetPrimitiveSuffix)))
                    result.Add(world);

                CollectPrimitiveInstancesRecursive(resolved, world, targetPrimitiveSuffix, resolver, result, depth + 1);
            }
        }

        static bool TryParseType1SubfileRef(string line, out Vector3 pos, out Matrix4x4 basis, out string subfile)
        {
            pos = Vector3.zero;
            basis = Matrix4x4.identity;
            subfile = null;

            if (string.IsNullOrWhiteSpace(line))
                return false;

            string t = line.Trim();
            if (!t.StartsWith("1 "))
                return false;

            string[] parts = t.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 15)
                return false;

            // Type 1 format: 1 color x y z a b c d e f g h i file
            if (!TryParseInv(parts[2], out float x) ||
                !TryParseInv(parts[3], out float y) ||
                !TryParseInv(parts[4], out float z) ||
                !TryParseInv(parts[5], out float a) ||
                !TryParseInv(parts[6], out float b) ||
                !TryParseInv(parts[7], out float c) ||
                !TryParseInv(parts[8], out float d) ||
                !TryParseInv(parts[9], out float e) ||
                !TryParseInv(parts[10], out float f) ||
                !TryParseInv(parts[11], out float g) ||
                !TryParseInv(parts[12], out float h) ||
                !TryParseInv(parts[13], out float i))
                return false;

            pos = new Vector3(x, y, z);
            basis = Matrix4x4.identity;
            basis.m00 = a; basis.m01 = b; basis.m02 = c;
            basis.m10 = d; basis.m11 = e; basis.m12 = f;
            basis.m20 = g; basis.m21 = h; basis.m22 = i;
            subfile = parts[14];
            return true;
        }

        static bool TryParseInv(string s, out float value)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static string NormalizeRef(string reference)
        {
            return (reference ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
        }

        /// <summary>
        /// Post-processes converted connection data:
        /// 1. BottomTube entries (stud3/stud4) → converted to TechnicHole with negated direction,
        ///    and their presence triggers anti-stud grid derivation from male stud positions.
        /// 2. Through-holes (TechnicHole/TechnicAxleHole from connect.dat etc.) → split into
        ///    two face-offset connection points, one on each physical surface of the beam.
        /// 3. Studs, TechnicPins, TechnicAxles → passed through as-is.
        /// </summary>
        static LDrawPartCatalog.ConnectionData PostProcessConnections(
            LDrawPartCatalog.ConnectionData data, Bounds meshBounds, float[] holeScales = null)
        {
            if (data == null || data.Count == 0)
                return data;

            var types = new List<byte>();
            var positions = new List<float>();
            var directions = new List<float>();
            var depths = new List<float>();

            // First pass: detect BottomTubes and collect stud positions
            bool hasBottomTubes = false;
            var studXZPositions = new List<Vector2>();
            float maxStudTopY = float.MinValue;

            for (int i = 0; i < data.Count; i++)
            {
                byte t = data.Types[i];
                if (t == (byte)LDrawConnectionType.BottomTube)
                    hasBottomTubes = true;
                if (t == (byte)LDrawConnectionType.Stud)
                {
                    studXZPositions.Add(new Vector2(data.Positions[i * 3], data.Positions[i * 3 + 2]));
                    maxStudTopY = Mathf.Max(maxStudTopY, data.Positions[i * 3 + 1]);
                }
            }

            float bottomY = meshBounds.min.y;
            float topSurfaceY = (maxStudTopY > float.MinValue)
                ? (maxStudTopY - BlockGrid.STUD_HEIGHT)
                : (meshBounds.max.y - BlockGrid.STUD_HEIGHT);
            float bottomCavityDepth = Mathf.Max(0.01f, topSurfaceY - bottomY);

            // Pre-filter: remove TechnicAxleHole entries that are collinear with TechnicPin entries.
            // This handles false positives from stop bush geometry (bush0.dat → bush0a.dat → axlehol5.dat)
            // on pin parts, where the cross-shaped bush collar is not a functional axle connection.
            var skipIndices = new HashSet<int>();
            {
                var pinEntries = new List<(Vector3 pos, Vector3 dir)>();
                for (int i = 0; i < data.Count; i++)
                {
                    if (data.Types[i] == (byte)LDrawConnectionType.TechnicPin)
                    {
                        pinEntries.Add((
                            new Vector3(data.Positions[i * 3], data.Positions[i * 3 + 1], data.Positions[i * 3 + 2]),
                            new Vector3(data.Directions[i * 3], data.Directions[i * 3 + 1], data.Directions[i * 3 + 2])
                        ));
                    }
                }

                if (pinEntries.Count > 0)
                {
                    for (int i = 0; i < data.Count; i++)
                    {
                        if (data.Types[i] != (byte)LDrawConnectionType.TechnicAxleHole)
                            continue;

                        var axlePos = new Vector3(data.Positions[i * 3], data.Positions[i * 3 + 1], data.Positions[i * 3 + 2]);
                        var axleDir = new Vector3(data.Directions[i * 3], data.Directions[i * 3 + 1], data.Directions[i * 3 + 2]);

                        foreach (var (pinPos, pinDir) in pinEntries)
                        {
                            // Directions must be parallel (same axis)
                            if (Mathf.Abs(Vector3.Dot(axleDir, pinDir)) < 0.99f)
                                continue;

                            // Positions must be near-coincident on the same line.
                            // Collinearity alone is too broad and removes valid axle holes
                            // offset along the pin axis (e.g. stop-bush pin parts).
                            Vector3 displacement = axlePos - pinPos;
                            float perpDist = displacement.sqrMagnitude < 0.001f
                                ? 0f
                                : Vector3.Cross(displacement, pinDir).magnitude;
                            float axialDist = Mathf.Abs(Vector3.Dot(displacement, pinDir));

                            if (perpDist < 0.1f && axialDist < 0.1f)
                            {
                                skipIndices.Add(i);
                                break;
                            }
                        }
                    }
                }
            }

            // Half-thickness for offsetting through-hole face positions (standard technic beam = 1 stud wide)
            float halfThickness = 0.5f * BlockGrid.STUD_SPACING;

            // Second pass: process each connection
            for (int i = 0; i < data.Count; i++)
            {
                if (skipIndices.Contains(i))
                    continue;

                byte t = data.Types[i];
                float px = data.Positions[i * 3];
                float py = data.Positions[i * 3 + 1];
                float pz = data.Positions[i * 3 + 2];
                float dx = data.Directions[i * 3];
                float dy = data.Directions[i * 3 + 1];
                float dz = data.Directions[i * 3 + 2];

                if (t == (byte)LDrawConnectionType.Stud ||
                    t == (byte)LDrawConnectionType.TechnicPin)
                {
                    // Pass through as-is; studs get fixed depth, pins/axles get 0 placeholder (computed later)
                    types.Add(t);
                    positions.Add(px); positions.Add(py); positions.Add(pz);
                    directions.Add(dx); directions.Add(dy); directions.Add(dz);
                    depths.Add(t == (byte)LDrawConnectionType.Stud ? BlockGrid.STUD_HEIGHT : 0f);
                }
                else if (t == (byte)LDrawConnectionType.TechnicAxle)
                {
                    // Axle primitives are oriented opposite to gameplay male-direction expectation.
                    // Invert so male axle points outward for compatibility tests.
                    types.Add(t);
                    positions.Add(px); positions.Add(py); positions.Add(pz);
                    directions.Add(-dx); directions.Add(-dy); directions.Add(-dz);
                    depths.Add(0f);
                }
                else if (t == (byte)LDrawConnectionType.BottomTube)
                {
                    // Convert to TechnicHole. stud4 is placed at the TOP of the tube inside the
                    // brick body (Y=4 LDU), not at the bottom opening. Move to the actual bottom
                    // face (meshBounds.min.y) so the connection point is where a pin inserts.
                    // Direction already points outward (downward) from the bottom surface.
                    types.Add((byte)LDrawConnectionType.TechnicHole);
                    positions.Add(px); positions.Add(meshBounds.min.y); positions.Add(pz);
                    directions.Add(dx); directions.Add(dy); directions.Add(dz);
                    depths.Add(bottomCavityDepth);
                }
                else if (t == (byte)LDrawConnectionType.TechnicHoleSurface)
                {
                    // Pegholes are already placed at each face by LDraw parts (2 per hole).
                    // Convert to TechnicHole and pass through without duplication.
                    types.Add((byte)LDrawConnectionType.TechnicHole);
                    positions.Add(px); positions.Add(py); positions.Add(pz);
                    directions.Add(dx); directions.Add(dy); directions.Add(dz);
                    depths.Add(BlockGrid.STUD_SPACING);
                }
                else if (t == (byte)LDrawConnectionType.TechnicHole)
                {
                    // TechnicHole through-holes (beamhole/connhole): center-placed, offset to faces.
                    // Face A: center + dir * halfThickness, direction outward (+dir)
                    types.Add(t);
                    positions.Add(px + dx * halfThickness);
                    positions.Add(py + dy * halfThickness);
                    positions.Add(pz + dz * halfThickness);
                    directions.Add(dx); directions.Add(dy); directions.Add(dz);
                    depths.Add(BlockGrid.STUD_SPACING);

                    // Face B: center - dir * halfThickness, direction outward (-dir)
                    types.Add(t);
                    positions.Add(px - dx * halfThickness);
                    positions.Add(py - dy * halfThickness);
                    positions.Add(pz - dz * halfThickness);
                    directions.Add(-dx); directions.Add(-dy); directions.Add(-dz);
                    depths.Add(BlockGrid.STUD_SPACING);
                }
                else if (t == (byte)LDrawConnectionType.TechnicAxleHole)
                {
                    // TechnicAxleHole: detected position is at one END of the hole.
                    // The transform scale (from holeScales) encodes the hole depth in Unity units.
                    float holeDepth = (holeScales != null && i < holeScales.Length)
                        ? holeScales[i] : BlockGrid.STUD_SPACING;

                    // Through-holes (depth ≈ full stud, 20 LDU) get two faces.
                    // Dead-end sockets (depth < 1 stud, e.g. 19 LDU with closing disc) get
                    // one face at the CLOSED end (back wall = where axle tip seats).
                    bool isThroughHole = holeDepth >= 0.99f * BlockGrid.STUD_SPACING;

                    Debug.Log($"[AxleHole] idx={i} pos=({px:F3},{py:F3},{pz:F3}) dir=({dx:F3},{dy:F3},{dz:F3}) " +
                              $"holeDepth={holeDepth:F4} threshold={0.99f * BlockGrid.STUD_SPACING:F4} " +
                              $"isThroughHole={isThroughHole} scalesNull={holeScales == null} " +
                              $"scalesLen={holeScales?.Length ?? -1}");

                    if (isThroughHole)
                    {
                        // Face A: at the parsed position (one open face), direction outward
                        types.Add((byte)LDrawConnectionType.TechnicAxleHole);
                        positions.Add(px); positions.Add(py); positions.Add(pz);
                        directions.Add(dx); directions.Add(dy); directions.Add(dz);
                        depths.Add(holeDepth);

                        // Face B: at the opposite open face, direction outward (negated)
                        types.Add((byte)LDrawConnectionType.TechnicAxleHole);
                        positions.Add(px - dx * holeDepth);
                        positions.Add(py - dy * holeDepth);
                        positions.Add(pz - dz * holeDepth);
                        directions.Add(-dx); directions.Add(-dy); directions.Add(-dz);
                        depths.Add(holeDepth);
                    }
                    else
                    {
                        // Dead-end socket: parsed position may be either end depending on source primitive
                        // orientation. Evaluate both ends and choose the one nearest the mesh surface as open.
                        Vector3 endA = new Vector3(px, py, pz);
                        Vector3 endB = new Vector3(px - dx * holeDepth, py - dy * holeDepth, pz - dz * holeDepth);

                        float DistanceToBoundsSurface(Bounds b, Vector3 p)
                        {
                            float dxMin = Mathf.Abs(p.x - b.min.x);
                            float dxMax = Mathf.Abs(b.max.x - p.x);
                            float dyMin = Mathf.Abs(p.y - b.min.y);
                            float dyMax = Mathf.Abs(b.max.y - p.y);
                            float dzMin = Mathf.Abs(p.z - b.min.z);
                            float dzMax = Mathf.Abs(b.max.z - p.z);
                            return Mathf.Min(dxMin, dxMax, dyMin, dyMax, dzMin, dzMax);
                        }

                        float distA = DistanceToBoundsSurface(meshBounds, endA);
                        float distB = DistanceToBoundsSurface(meshBounds, endB);
                        bool openIsA = distA <= distB;

                        // For dead-end axle holes, parser direction points inward from the open face.
                        Vector3 openPos = openIsA ? endA : endB;
                        Vector3 inwardDir = openIsA ? new Vector3(dx, dy, dz) : new Vector3(-dx, -dy, -dz);

                        types.Add((byte)LDrawConnectionType.TechnicAxleHole);
                        positions.Add(openPos.x);
                        positions.Add(openPos.y);
                        positions.Add(openPos.z);
                        directions.Add(inwardDir.x); directions.Add(inwardDir.y); directions.Add(inwardDir.z);
                        depths.Add(holeDepth);
                    }
                }
            }

            // Derive anti-stud grid ONLY when bottom tubes are present.
            // Bottom tubes (stud3/stud4) indicate a standard brick underside with cavity.
            // Pure technic parts without bottom tubes don't get anti-studs.
            if (hasBottomTubes)
            {
                float antiStudDepth = bottomCavityDepth;

                if (studXZPositions.Count > 0)
                {
                    // Has male studs: mirror each stud XZ position to the bottom face
                    foreach (var xz in studXZPositions)
                    {
                        types.Add((byte)LDrawConnectionType.AntiStud);
                        positions.Add(xz.x); positions.Add(bottomY); positions.Add(xz.y);
                        directions.Add(0f); directions.Add(-1f); directions.Add(0f);
                        depths.Add(antiStudDepth);
                    }
                }
                else
                {
                    // No male studs (tile, slope, etc.) - estimate grid from mesh bounds
                    int width = Mathf.Max(1, Mathf.RoundToInt(meshBounds.size.x / BlockGrid.STUD_SPACING));
                    int gridDepth = Mathf.Max(1, Mathf.RoundToInt(meshBounds.size.z / BlockGrid.STUD_SPACING));
                    float cx = meshBounds.center.x;
                    float cz = meshBounds.center.z;

                    for (int x = 0; x < width; x++)
                    {
                        for (int z = 0; z < gridDepth; z++)
                        {
                            float apx = cx + (x - width * 0.5f + 0.5f) * BlockGrid.STUD_SPACING;
                            float apz = cz + (z - gridDepth * 0.5f + 0.5f) * BlockGrid.STUD_SPACING;

                            types.Add((byte)LDrawConnectionType.AntiStud);
                            positions.Add(apx); positions.Add(bottomY); positions.Add(apz);
                            directions.Add(0f); directions.Add(-1f); directions.Add(0f);
                            depths.Add(antiStudDepth);
                        }
                    }
                }
            }

            if (types.Count == 0)
                return null;

            // Collinear grouping: compute depth for TechnicPin and TechnicAxle entries.
            // Groups of collinear same-type entries get the span between farthest pair as depth.
            // Singles get default STUD_SPACING.
            {
                var visited = new bool[types.Count];
                for (int i = 0; i < types.Count; i++)
                {
                    byte ti = types[i];
                    if (ti != (byte)LDrawConnectionType.TechnicPin &&
                        ti != (byte)LDrawConnectionType.TechnicAxle)
                        continue;
                    if (visited[i]) continue;

                    var pi = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
                    var di = new Vector3(directions[i * 3], directions[i * 3 + 1], directions[i * 3 + 2]);

                    // Find all same-type entries on the same axis
                    var group = new List<int> { i };
                    for (int j = i + 1; j < types.Count; j++)
                    {
                        if (visited[j] || types[j] != ti) continue;
                        var pj = new Vector3(positions[j * 3], positions[j * 3 + 1], positions[j * 3 + 2]);
                        var dj = new Vector3(directions[j * 3], directions[j * 3 + 1], directions[j * 3 + 2]);

                        // Directions must be parallel
                        if (Mathf.Abs(Vector3.Dot(di, dj)) < 0.99f) continue;

                        // Positions must be collinear
                        Vector3 disp = pj - pi;
                        float perpDist = disp.sqrMagnitude < 0.001f
                            ? 0f
                            : Vector3.Cross(disp, di).magnitude;
                        if (perpDist < 0.1f)
                            group.Add(j);
                    }

                    // Compute span for the group
                    float span;
                    if (group.Count >= 2)
                    {
                        // Project all positions onto the axis and find max span
                        float minProj = float.MaxValue, maxProj = float.MinValue;
                        foreach (int idx in group)
                        {
                            var p = new Vector3(positions[idx * 3], positions[idx * 3 + 1], positions[idx * 3 + 2]);
                            float proj = Vector3.Dot(p, di);
                            if (proj < minProj) minProj = proj;
                            if (proj > maxProj) maxProj = proj;
                        }
                        // Ensure minimum depth even if endpoints are at the same position
                        span = Mathf.Max(maxProj - minProj, BlockGrid.STUD_SPACING);
                    }
                    else
                    {
                        span = BlockGrid.STUD_SPACING;
                    }

                    // Assign depth to all members
                    foreach (int idx in group)
                    {
                        depths[idx] = span;
                        visited[idx] = true;
                    }
                }
            }

            return new LDrawPartCatalog.ConnectionData
            {
                Types = types.ToArray(),
                Positions = positions.ToArray(),
                Directions = directions.ToArray(),
                Depths = depths.ToArray()
            };
        }

        public static void ConvertParts(List<string> partFilePaths, Action<float, string> onProgress = null)
        {
            string ldrawRoot = LDrawLibraryExtractor.GetLDrawRoot();
            var resolver = new LDrawFileResolver(ldrawRoot);
            var parser = new LDrawParser(resolver);

            EnsureDirectoryExists(GeneratedMeshesPath);
            EnsureDirectoryExists(ResourcesPath);

            var catalog = AssetDatabase.LoadAssetAtPath<LDrawPartCatalog>(PartCatalogAssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LDrawPartCatalog>();
                AssetDatabase.CreateAsset(catalog, PartCatalogAssetPath);
            }

            int successCount = 0;
            int failCount = 0;
            var rebuiltPartNumbers = new List<string>(partFilePaths.Count);

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < partFilePaths.Count; i++)
                {
                    float progress = (float)i / partFilePaths.Count;
                    string partName = Path.GetFileNameWithoutExtension(partFilePaths[i]);

                    onProgress?.Invoke(progress, partName);
                    EditorUtility.DisplayProgressBar("Converting LDraw Parts",
                        $"Converting {partName} ({i + 1}/{partFilePaths.Count})",
                        progress);

                    if (ConvertPart(partFilePaths[i], resolver, parser, catalog))
                    {
                        successCount++;
                        rebuiltPartNumbers.Add(partName);
                    }
                    else
                        failCount++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            LDrawPartRefreshEvents.RaisePartsRebuilt(rebuiltPartNumbers);

            Debug.Log($"Batch conversion complete: {successCount} succeeded, {failCount} failed out of {partFilePaths.Count} parts.");
            if (resolver.UnresolvedCount > 0)
                Debug.LogWarning($"{resolver.UnresolvedCount} unique subfile references could not be resolved. Check warnings above.");
        }

        public static LDrawPartCatalog GetOrCreateCatalog()
        {
            EnsureDirectoryExists(ResourcesPath);

            var catalog = AssetDatabase.LoadAssetAtPath<LDrawPartCatalog>(PartCatalogAssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LDrawPartCatalog>();
                AssetDatabase.CreateAsset(catalog, PartCatalogAssetPath);
                AssetDatabase.SaveAssets();
            }
            return catalog;
        }

        /// <summary>
        /// Deletes all generated assets for a converted part: mesh, textured materials, catalog entry.
        /// </summary>
        public static bool DeletePart(string partNumber)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LDrawPartCatalog>(PartCatalogAssetPath);
            var matLib = AssetDatabase.LoadAssetAtPath<LDrawMaterialLibrary>(MaterialLibraryAssetPath);

            // Gather textured material info from catalog before removing
            if (catalog != null)
            {
                var entry = catalog.GetPart(partNumber);
                if (entry != null && entry.SubmeshTextureNames != null && matLib != null)
                {
                    for (int i = 0; i < entry.SubmeshTextureNames.Length; i++)
                    {
                        string texName = entry.SubmeshTextureNames[i];
                        if (string.IsNullOrEmpty(texName)) continue;

                        int colorCode = entry.SubmeshColorCodes[i];
                        string matPath = $"{GeneratedMaterialsPath}/Textured/{colorCode}_{texName}.mat";

                        matLib.RemoveTexturedMaterial(colorCode, texName);
                        AssetDatabase.DeleteAsset(matPath);
                    }

                    EditorUtility.SetDirty(matLib);
                }

                catalog.RemovePart(partNumber);
                EditorUtility.SetDirty(catalog);
            }

            // Delete mesh asset
            string meshAssetPath = $"{GeneratedMeshesPath}/{partNumber}.asset";
            bool deleted = AssetDatabase.DeleteAsset(meshAssetPath);
            string lodMeshAssetPath = $"{GeneratedMeshesPath}/{partNumber}_nostud.asset";
            AssetDatabase.DeleteAsset(lodMeshAssetPath);

            AssetDatabase.SaveAssets();
            return deleted;
        }

        /// <summary>
        /// Deletes all converted parts: meshes, textured materials, and catalog entries.
        /// </summary>
        public static int DeleteAllParts()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LDrawPartCatalog>(PartCatalogAssetPath);
            if (catalog == null || catalog.Parts.Count == 0)
                return 0;

            // Collect all part numbers before modifying
            var partNumbers = new List<string>(catalog.Parts.Count);
            foreach (var entry in catalog.Parts)
                partNumbers.Add(entry.PartNumber);

            int count = 0;
            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var partNumber in partNumbers)
                {
                    if (DeletePart(partNumber))
                        count++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Deleted {count} converted parts.");
            return count;
        }

        /// <summary>
        /// Rebuilds all currently converted parts: deletes and re-converts each one.
        /// Useful after pipeline changes (e.g., adding connection point detection).
        /// </summary>
        public static void RebuildAllParts()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LDrawPartCatalog>(PartCatalogAssetPath);
            if (catalog == null || catalog.Parts.Count == 0)
            {
                Debug.Log("No converted parts to rebuild.");
                return;
            }

            // Collect part numbers and resolve their source .dat files
            string ldrawRoot = LDrawLibraryExtractor.GetLDrawRoot();
            string partsDir = Path.Combine(ldrawRoot, "parts");
            var partPaths = new List<string>();
            var partNumbers = new List<string>();

            foreach (var entry in catalog.Parts)
            {
                partNumbers.Add(entry.PartNumber);
                string datPath = Path.Combine(partsDir, entry.PartNumber + ".dat");
                if (File.Exists(datPath))
                    partPaths.Add(datPath);
                else
                    Debug.LogWarning($"Source .dat not found for part {entry.PartNumber}, skipping rebuild.");
            }

            // Delete all existing
            int deleted = DeleteAllParts();
            Debug.Log($"Deleted {deleted} parts for rebuild.");

            // Re-convert
            if (partPaths.Count > 0)
                ConvertParts(partPaths);

            Debug.Log($"Rebuild complete: re-converted {partPaths.Count} parts.");
        }

        public static bool IsPartConverted(string partNumber)
        {
            string meshAssetPath = $"{GeneratedMeshesPath}/{partNumber}.asset";
            return AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath) != null;
        }

        static void EnsureDirectoryExists(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            return name.Replace(' ', '_').Replace('/', '_').Replace('\\', '_');
        }

        static void CreateTexturedMaterial(int colorCode, string textureName)
        {
            var colorTable = AssetDatabase.LoadAssetAtPath<LDrawColorTable>(ColorTableAssetPath);
            var matLib = AssetDatabase.LoadAssetAtPath<LDrawMaterialLibrary>(MaterialLibraryAssetPath);

            if (colorTable == null || matLib == null)
            {
                Debug.LogWarning($"Cannot create textured material: color table or material library not found");
                return;
            }

            // Ensure Textured subdirectory exists
            EnsureDirectoryExists($"{GeneratedMaterialsPath}/Textured");

            // Check if material already exists
            string matPath = $"{GeneratedMaterialsPath}/Textured/{colorCode}_{textureName}.mat";
            var existingMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (existingMat != null)
            {
                // Material file exists, ensure it's registered in the library
                matLib.SetTexturedMaterial(colorCode, textureName, existingMat);
                EditorUtility.SetDirty(matLib);
                return;
            }

            var texture = LDrawTextureImporter.LoadTexture(textureName);
            if (texture == null)
            {
                Debug.LogWarning($"Texture not found: {textureName}. Run 'Import Textures' first.");
                return;
            }

            var colorEntry = colorTable.GetColor(colorCode);
            if (colorEntry == null)
            {
                Debug.LogWarning($"Color code {colorCode} not found in color table");
                return;
            }

            var mat = LDrawMaterialFactory.CreateTexturedMaterial(colorEntry, texture);
            if (mat == null)
            {
                Debug.LogWarning($"Failed to create textured material for color {colorCode}, texture {textureName}");
                return;
            }

            AssetDatabase.CreateAsset(mat, matPath);
            matLib.SetTexturedMaterial(colorCode, textureName, mat);
            EditorUtility.SetDirty(matLib);
        }

        public static readonly string[] CommonParts = new[]
        {
            "3001", "3002", "3003", "3004", "3005", "3006", "3007", "3008", "3009", "3010",
            "3020", "3021", "3022", "3023", "3024", "3031", "3032", "3033", "3034", "3035",
            "3036", "3037", "3038", "3039", "3040", "3041", "3042", "3043", "3044", "3045",
            "3046", "3048", "3049", "3062", "3063", "3065", "3068", "3069", "3070",
            "2357", "2420", "2431", "2432", "2436", "2441", "2445", "2450", "2453", "2454",
            "2456", "2460", "2476", "2555", "2556", "2780", "2815", "2817",
            "3298", "3460", "3622", "3623", "3624", "3625", "3626", "3660",
            "3666", "3676", "3680", "3700", "3701", "3702", "3703", "3705", "3706", "3707",
            "3710", "3713", "3747", "3794", "3795", "3832",
            "4070", "4073", "4081", "4085", "4150", "4162", "4175", "4176",
            "4286", "4287", "4460", "4477", "4489", "4490", "4510", "4519",
            "6003", "6005", "6014", "6015", "6020", "6091", "6134", "6141", "6143",
            "6215", "6222", "6231", "6232", "6233", "6538", "6541", "6558",
            "3832", "6636", "30136", "30244", "30367", "32000", "32001", "32002",
            "32013", "32014", "32015", "32016", "32017", "32034", "32039", "32054",
            "32056", "32062", "32063", "32064", "32073", "32123", "32124",
            "32184", "32192", "32269", "32270", "32271", "32278", "32291", "32316",
            "32348", "32449", "32523", "32524", "32525", "32526"
        };
    }
}
