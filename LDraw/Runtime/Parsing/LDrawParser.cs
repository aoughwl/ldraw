using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace LDraw
{
    public class LDrawParser
    {
        const int MaxRecursionDepth = 20;
        const int ColorInherit = 16;
        const int ColorComplement = 24;

        readonly LDrawFileResolver _resolver;
        readonly Dictionary<string, ParsedFile> _fileCache = new Dictionary<string, ParsedFile>();

        public LDrawParser(LDrawFileResolver resolver)
        {
            _resolver = resolver;
        }

        public void ClearCache()
        {
            _fileCache.Clear();
        }

        /// <summary>
        /// Flattens a part file into lists of untextured and textured triangles in LDraw coordinate space.
        /// All subfile references are recursively resolved.
        /// </summary>
        public (List<LDrawTriangle> untextured, List<LDrawTexturedTriangle> textured)
            FlattenPart(string filePath, int parentColor = ColorInherit, bool omitStudGeometry = false)
        {
            var untextured = new List<LDrawTriangle>();
            var textured = new List<LDrawTexturedTriangle>();
            var bfc = LDrawBfcState.Default;
            var texmap = LDrawTexmapState.Default;
            FlattenRecursive(filePath, Matrix4x4.identity, parentColor, bfc, texmap, false,
                untextured, textured, null, omitStudGeometry, 0);
            return (untextured, textured);
        }

        /// <summary>
        /// Flattens a part file and also extracts connection point references (studs, anti-studs,
        /// technic pins, axle holes, etc.) detected from subfile primitive references.
        /// </summary>
        public (List<LDrawTriangle> untextured, List<LDrawTexturedTriangle> textured,
                List<LDrawConnectionReference> connections)
            FlattenPartWithConnections(string filePath, int parentColor = ColorInherit)
        {
            var untextured = new List<LDrawTriangle>();
            var textured = new List<LDrawTexturedTriangle>();
            var connections = new List<LDrawConnectionReference>();
            var bfc = LDrawBfcState.Default;
            var texmap = LDrawTexmapState.Default;
            FlattenRecursive(filePath, Matrix4x4.identity, parentColor, bfc, texmap, false,
                untextured, textured, connections, false, 0);
            return (untextured, textured, connections);
        }

        void FlattenRecursive(string filePath, Matrix4x4 accumulatedTransform, int parentColor,
            LDrawBfcState bfc, LDrawTexmapState texmap, bool invertWinding,
            List<LDrawTriangle> untexturedOutput, List<LDrawTexturedTriangle> texturedOutput,
            List<LDrawConnectionReference> connectionOutput, bool omitStudGeometry, int depth)
        {
            if (depth > MaxRecursionDepth)
            {
                Debug.LogWarning($"LDrawParser: max recursion depth reached for {filePath}");
                return;
            }

            var parsed = ParseFile(filePath);
            if (parsed == null) return;

            string currentDir = Path.GetDirectoryName(filePath);

            // Apply BFC certification from this file
            bool localInvert = invertWinding;
            var localBfc = parsed.IsBfcCertified
                ? new LDrawBfcState { Certified = true, WindingCCW = parsed.BfcWindingCCW, InvertNext = false, Culling = true }
                : bfc;
            var localTexmap = texmap;
            bool inFallback = false;

            foreach (var command in parsed.Commands)
            {
                switch (command.Type)
                {
                    case CommandType.TexmapStart:
                        localTexmap = command.TexmapState;
                        localTexmap.Active = true;
                        inFallback = false;
                        break;

                    case CommandType.TexmapFallback:
                        inFallback = true;
                        break;

                    case CommandType.TexmapEnd:
                        localTexmap = LDrawTexmapState.Default;
                        inFallback = false;
                        break;

                    case CommandType.BfcInvertNext:
                        localBfc.InvertNext = true;
                        break;

                    case CommandType.BfcCW:
                        localBfc.WindingCCW = false;
                        break;

                    case CommandType.BfcCCW:
                        localBfc.WindingCCW = true;
                        break;

                    case CommandType.BfcNoCertify:
                        localBfc.Certified = false;
                        break;

                    case CommandType.Triangle:
                    {
                        if (inFallback) break;  // Skip fallback geometry

                        int color = ResolveColor(command.Triangle.Color, parentColor);
                        var v1 = accumulatedTransform.MultiplyPoint3x4(command.Triangle.V1);
                        var v2 = accumulatedTransform.MultiplyPoint3x4(command.Triangle.V2);
                        var v3 = accumulatedTransform.MultiplyPoint3x4(command.Triangle.V3);

                        bool shouldInvert = localInvert ^ !localBfc.WindingCCW;

                        if (localTexmap.Active)
                        {
                            // Compute UVs in LDraw space BEFORE transform
                            var uv1 = localTexmap.ProjectUV(command.Triangle.V1);
                            var uv2 = localTexmap.ProjectUV(command.Triangle.V2);
                            var uv3 = localTexmap.ProjectUV(command.Triangle.V3);

                            if (shouldInvert)
                            {
                                // Opaque base face underneath the sticker
                                untexturedOutput.Add(new LDrawTriangle(color, v1, v3, v2));
                                // Transparent sticker overlay on top
                                texturedOutput.Add(new LDrawTexturedTriangle(color, v1, v3, v2,
                                    uv1, uv3, uv2, localTexmap.TextureName));
                            }
                            else
                            {
                                untexturedOutput.Add(new LDrawTriangle(color, v1, v2, v3));
                                texturedOutput.Add(new LDrawTexturedTriangle(color, v1, v2, v3,
                                    uv1, uv2, uv3, localTexmap.TextureName));
                            }
                        }
                        else
                        {
                            if (shouldInvert)
                                untexturedOutput.Add(new LDrawTriangle(color, v1, v3, v2));
                            else
                                untexturedOutput.Add(new LDrawTriangle(color, v1, v2, v3));
                        }

                        localBfc.InvertNext = false;
                        break;
                    }

                    case CommandType.Quad:
                    {
                        if (inFallback) break;  // Skip fallback geometry

                        int color = ResolveColor(command.Quad.Color, parentColor);
                        var v1 = accumulatedTransform.MultiplyPoint3x4(command.Quad.V1);
                        var v2 = accumulatedTransform.MultiplyPoint3x4(command.Quad.V2);
                        var v3 = accumulatedTransform.MultiplyPoint3x4(command.Quad.V3);
                        var v4 = accumulatedTransform.MultiplyPoint3x4(command.Quad.V4);

                        bool shouldInvert = localInvert ^ !localBfc.WindingCCW;

                        if (localTexmap.Active)
                        {
                            // Compute UVs in LDraw space BEFORE transform
                            var uv1 = localTexmap.ProjectUV(command.Quad.V1);
                            var uv2 = localTexmap.ProjectUV(command.Quad.V2);
                            var uv3 = localTexmap.ProjectUV(command.Quad.V3);
                            var uv4 = localTexmap.ProjectUV(command.Quad.V4);

                            if (shouldInvert)
                            {
                                // Opaque base faces underneath the sticker
                                untexturedOutput.Add(new LDrawTriangle(color, v1, v3, v2));
                                untexturedOutput.Add(new LDrawTriangle(color, v1, v4, v3));
                                // Transparent sticker overlay on top
                                texturedOutput.Add(new LDrawTexturedTriangle(color, v1, v3, v2,
                                    uv1, uv3, uv2, localTexmap.TextureName));
                                texturedOutput.Add(new LDrawTexturedTriangle(color, v1, v4, v3,
                                    uv1, uv4, uv3, localTexmap.TextureName));
                            }
                            else
                            {
                                untexturedOutput.Add(new LDrawTriangle(color, v1, v2, v3));
                                untexturedOutput.Add(new LDrawTriangle(color, v1, v3, v4));
                                texturedOutput.Add(new LDrawTexturedTriangle(color, v1, v2, v3,
                                    uv1, uv2, uv3, localTexmap.TextureName));
                                texturedOutput.Add(new LDrawTexturedTriangle(color, v1, v3, v4,
                                    uv1, uv3, uv4, localTexmap.TextureName));
                            }
                        }
                        else
                        {
                            if (shouldInvert)
                            {
                                untexturedOutput.Add(new LDrawTriangle(color, v1, v3, v2));
                                untexturedOutput.Add(new LDrawTriangle(color, v1, v4, v3));
                            }
                            else
                            {
                                untexturedOutput.Add(new LDrawTriangle(color, v1, v2, v3));
                                untexturedOutput.Add(new LDrawTriangle(color, v1, v3, v4));
                            }
                        }

                        localBfc.InvertNext = false;
                        break;
                    }

                    case CommandType.SubFileRef:
                    {
                        var subRef = command.SubFileRef;
                        string resolvedPath = _resolver.ResolveWithWarning(
                            subRef.FileName, currentDir, filePath);
                        if (resolvedPath == null)
                        {
                            localBfc.InvertNext = false;
                            break;
                        }

                        int color = ResolveColor(subRef.Color, parentColor);
                        var childTransform = accumulatedTransform * subRef.Transform;

                        // Check if this subfile is a connection primitive (stud, pin, axle, etc.)
                        bool isConnectionPrimitive = false;
                        bool isBushCollar = false;
                        LDrawConnectionType? connType = null;
                        if (connectionOutput != null || omitStudGeometry)
                        {
                            connType = ClassifyConnection(subRef.FileName);
                            if (connType.HasValue)
                            {
                                isConnectionPrimitive = true;
                                if (connectionOutput != null)
                                {
                                    var pos = new Vector3(childTransform.m03, childTransform.m13, childTransform.m23);
                                    // Studs/connections face -Y in local LDraw space
                                    var dir = childTransform.MultiplyVector(new Vector3(0, -1, 0));
                                    connectionOutput.Add(new LDrawConnectionReference
                                    {
                                        Type = connType.Value,
                                        PositionLDraw = pos,
                                        DirectionLDraw = dir
                                    });
                                }
                            }
                            else if (IsBushCollar(subRef.FileName))
                            {
                                // Bush collars contain bush0 → bush0a → axlehol5 internally.
                                // The axle hole IS functional but the collar blocks one face.
                                // Let recursion find the connection, then cap direction magnitude
                                // to force dead-end (single-face) treatment in PostProcess.
                                isBushCollar = true;
                            }
                        }

                        // LOD mesh generation mode: remove top stud primitive geometry at source.
                        // This preserves the original parent surface faces and avoids runtime hacks.
                        if (omitStudGeometry && connType.HasValue && connType.Value == LDrawConnectionType.Stud)
                        {
                            localBfc.InvertNext = false;
                            break;
                        }

                        // Compute child inversion state
                        float det = subRef.Transform.determinant;
                        bool childInvert = localInvert ^ localBfc.InvertNext ^ (det < 0);

                        // Don't detect connections inside connection primitives
                        // (e.g. axle.dat may reference axlehole geometry internally).
                        // Bush collars are NOT connection primitives — we let recursion continue.
                        var childConnOutput = isConnectionPrimitive ? null : connectionOutput;

                        int prevConnCount = (isBushCollar && connectionOutput != null) ? connectionOutput.Count : 0;

                        FlattenRecursive(resolvedPath, childTransform, color, localBfc, localTexmap,
                            childInvert, untexturedOutput, texturedOutput, childConnOutput, omitStudGeometry, depth + 1);

                        // Bush collar post-processing: cap direction magnitude of newly-added
                        // connections to 19 LDU (0.95 studs) to force dead-end treatment.
                        // Standard through-holes are 20 LDU (1.0 stud); 19 LDU falls below
                        // the 0.99 threshold, producing a single-face connection.
                        if (isBushCollar && connectionOutput != null)
                        {
                            const float maxDepthLDU = 19f;
                            for (int ci = prevConnCount; ci < connectionOutput.Count; ci++)
                            {
                                var conn = connectionOutput[ci];
                                float mag = conn.DirectionLDraw.magnitude;
                                if (mag > maxDepthLDU)
                                {
                                    conn.DirectionLDraw = conn.DirectionLDraw * (maxDepthLDU / mag);
                                    connectionOutput[ci] = conn;
                                }
                            }
                        }

                        localBfc.InvertNext = false;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Classifies an LDraw subfile reference as a connection primitive, or returns null if not one.
        /// Covers standard studs, anti-studs, technic pins, pin holes, axles, and axle holes.
        /// </summary>
        static LDrawConnectionType? ClassifyConnection(string fileName)
        {
            // Strip directory prefixes (s/, 8/, 48/) and lowercase for matching
            string name = fileName.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            name = name.ToLowerInvariant();

            // === SKIP: Non-connection primitives ===
            // Duplo/Quatro studs
            if (name.StartsWith("stud7") || name.StartsWith("stud8a") ||
                name.StartsWith("stud20") || name.StartsWith("stud24") ||
                name.StartsWith("stud25") || name.StartsWith("stud27") ||
                name.StartsWith("stud28"))
                return null;
            // Scala stud
            if (name == "stud5.dat") return null;
            // Technic cross studs (not connection points)
            if (name.StartsWith("studx") || name.StartsWith("stud12")) return null;
            // Stud line helper
            if (name.StartsWith("studline")) return null;

            // === BOTTOM TUBES (intermediate type: triggers anti-stud derivation, converts to TechnicHole) ===
            // stud3*.dat = center tube (closed), stud4*.dat = center tube (open bottom)
            // These are round pin holes on the brick underside. Their presence also signals
            // that the brick has a standard bottom cavity, triggering anti-stud grid derivation
            // during post-processing. They are converted to TechnicHole in the final output.
            if (name.StartsWith("stud3") && name.EndsWith(".dat")) return LDrawConnectionType.BottomTube;
            if (name.StartsWith("stud4") && name.EndsWith(".dat")) return LDrawConnectionType.BottomTube;
            if (name == "stud16.dat") return LDrawConnectionType.BottomTube;
            if (name == "stud21a.dat" || name == "stud22a.dat") return LDrawConnectionType.BottomTube;
            if (name.StartsWith("stud23") && name.EndsWith(".dat")) return LDrawConnectionType.BottomTube;

            // === MALE (stud / top protrusion) ===
            // stud.dat, stud2*.dat, stud6.dat, stud9.dat, stud10.dat, stud11.dat
            // stud13.dat, stud15.dat, stud17a.dat, stud26.dat, studp01.dat, logo variants
            if (name == "stud.dat") return LDrawConnectionType.Stud;
            if (name.StartsWith("stud2") && name.EndsWith(".dat")) return LDrawConnectionType.Stud;
            if (name == "stud6.dat" || name == "stud6a.dat") return LDrawConnectionType.Stud;
            if (name == "stud9.dat") return LDrawConnectionType.Stud;
            if (name == "stud10.dat") return LDrawConnectionType.Stud;
            if (name == "stud11.dat") return LDrawConnectionType.Stud;
            if (name == "stud13.dat") return LDrawConnectionType.Stud;
            if (name == "stud14.dat") return LDrawConnectionType.Stud;
            if (name == "stud15.dat") return LDrawConnectionType.Stud;
            if (name.StartsWith("stud17") && name.EndsWith(".dat")) return LDrawConnectionType.Stud;
            if (name == "stud26.dat") return LDrawConnectionType.Stud;
            if (name.StartsWith("studp") && name.EndsWith(".dat")) return LDrawConnectionType.Stud;
            if (name.StartsWith("studlogo") && name.EndsWith(".dat")) return LDrawConnectionType.Stud;
            if (name.StartsWith("stud-") && name.EndsWith(".dat")) return LDrawConnectionType.Stud;
            // Catch remaining stud*.dat that weren't caught above (stud1.dat etc.)
            if (name.StartsWith("stud") && name.EndsWith(".dat") && !name.Contains("line"))
                return LDrawConnectionType.Stud;

            // === TECHNIC PINS (round male) ===
            // connect*.dat = standard pin, confric*.dat = friction-ridge pin
            if (name.StartsWith("connect") && name.EndsWith(".dat")) return LDrawConnectionType.TechnicPin;
            if (name.StartsWith("confric") && name.EndsWith(".dat")) return LDrawConnectionType.TechnicPin;

            // === TECHNIC PIN HOLES — face-placed (peghole placed at each face by LDraw parts) ===
            if (name.StartsWith("peghole") && name.EndsWith(".dat")) return LDrawConnectionType.TechnicHoleSurface;

            // === TECHNIC THROUGH-HOLES — center-placed (need duplication to both faces) ===
            // beamhole*.dat = modern beam hole (accepts pins and axles)
            // connhol*.dat = connector hole variants (connhole.dat, connhol2.dat, etc.)
            // hole1/2/3.dat = legacy beam holes
            if (name.StartsWith("beamhole") && name.EndsWith(".dat")) return LDrawConnectionType.TechnicHole;
            if (name.StartsWith("connhol") && name.EndsWith(".dat")) return LDrawConnectionType.TechnicHole;
            if (name == "hole1.dat" || name == "hole2.dat" || name == "hole3.dat") return LDrawConnectionType.TechnicHole;

            // === TECHNIC AXLES (cross male) ===
            // axleend2.dat = standard axle end cap marker, axleend.dat = deprecated variant
            if (name == "axleend2.dat" || name == "axleend.dat") return LDrawConnectionType.TechnicAxle;

            // === TECHNIC AXLE HOLES (cross female) ===
            // All axle hole primitives use the base type. PostProcess uses the depth
            // heuristic (from transform scale) to decide single face vs through-hole.
            if (name == "axlehole.dat" || name == "axlehol4.dat" || name == "axlehol5.dat")
                return LDrawConnectionType.TechnicAxleHole;
            if (name == "axl2hole.dat" || name == "axl3hole.dat" || name == "axl4hole.dat")
                return LDrawConnectionType.TechnicAxleHole;
            if (name == "axl2hol8.dat") return LDrawConnectionType.TechnicAxleHole;

            return null;
        }

        /// <summary>
        /// Returns true for bush collar primitives (bush.dat, bush2.dat, bush3.dat).
        /// These contain bush0 → bush0a → axlehol5 internally, which IS a functional
        /// axle hole, but the collar blocks one face (single-open-ended).
        /// Connection recursion is allowed but direction magnitude is capped afterwards
        /// to force dead-end treatment in PostProcess.
        /// </summary>
        static bool IsBushCollar(string fileName)
        {
            string name = fileName.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            name = name.ToLowerInvariant();

            if (name == "bush.dat" || name == "bush2.dat" || name == "bush3.dat")
                return true;

            return false;
        }

        static int ResolveColor(int color, int parentColor)
        {
            if (color == ColorInherit)
                return parentColor;
            if (color == ColorComplement)
                return parentColor; // Simplified: use parent color for edge/complement
            return color;
        }

        ParsedFile ParseFile(string filePath)
        {
            string key = filePath.Replace('\\', '/').ToLowerInvariant();
            if (_fileCache.TryGetValue(key, out var cached))
                return cached;

            if (!File.Exists(filePath))
            {
                _fileCache[key] = null;
                return null;
            }

            var parsed = new ParsedFile();
            var lines = File.ReadAllLines(filePath);

            // Get description from first line
            if (lines.Length > 0 && lines[0].StartsWith("0 "))
                parsed.Description = lines[0].Substring(2).Trim();

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                char lineType = line[0];

                switch (lineType)
                {
                    case '0':
                        ParseMetaCommand(line, parsed);
                        break;
                    case '1':
                        ParseSubFileRef(line, parsed);
                        break;
                    case '3':
                        ParseTriangle(line, parsed);
                        break;
                    case '4':
                        ParseQuad(line, parsed);
                        break;
                    // Line types 2 (line) and 5 (optional line) are ignored for mesh geometry
                }
            }

            _fileCache[key] = parsed;
            return parsed;
        }

        void ParseMetaCommand(string line, ParsedFile parsed)
        {
            // Tokenize for precise matching instead of substring Contains
            var tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            // tokens[0] = "0", tokens[1] might be "BFC", "!TEXMAP", "!:", etc.

            if (tokens.Length < 2) return;

            // Handle TEXMAP geometry lines: "0 !: 3 ..." or "0 !: 4 ..."
            // These are geometry lines (triangles/quads) within a TEXMAP section
            if (tokens[1] == "!:")
            {
                if (tokens.Length < 3) return;

                // Extract the geometry line after "0 !:"
                // Example: "0 !: 4 16 x y z ..." becomes "4 16 x y z ..."
                string geometryType = tokens[2];

                if (geometryType == "3" && tokens.Length >= 12)
                {
                    // Triangle: 0 !: 3 color x1 y1 z1 x2 y2 z2 x3 y3 z3
                    int color = ParseInt(tokens[3]);
                    var v1 = new Vector3(ParseFloat(tokens[4]), ParseFloat(tokens[5]), ParseFloat(tokens[6]));
                    var v2 = new Vector3(ParseFloat(tokens[7]), ParseFloat(tokens[8]), ParseFloat(tokens[9]));
                    var v3 = new Vector3(ParseFloat(tokens[10]), ParseFloat(tokens[11]), ParseFloat(tokens[12]));

                    parsed.Commands.Add(new ParsedCommand
                    {
                        Type = CommandType.Triangle,
                        Triangle = new LDrawTriangle(color, v1, v2, v3)
                    });
                }
                else if (geometryType == "4" && tokens.Length >= 15)
                {
                    // Quad: 0 !: 4 color x1 y1 z1 x2 y2 z2 x3 y3 z3 x4 y4 z4
                    int color = ParseInt(tokens[3]);
                    var v1 = new Vector3(ParseFloat(tokens[4]), ParseFloat(tokens[5]), ParseFloat(tokens[6]));
                    var v2 = new Vector3(ParseFloat(tokens[7]), ParseFloat(tokens[8]), ParseFloat(tokens[9]));
                    var v3 = new Vector3(ParseFloat(tokens[10]), ParseFloat(tokens[11]), ParseFloat(tokens[12]));
                    var v4 = new Vector3(ParseFloat(tokens[13]), ParseFloat(tokens[14]), ParseFloat(tokens[15]));

                    parsed.Commands.Add(new ParsedCommand
                    {
                        Type = CommandType.Quad,
                        Quad = new LDrawQuad(color, v1, v2, v3, v4)
                    });
                }
                return;
            }

            // Handle TEXMAP commands
            if (tokens[1] == "!TEXMAP" && tokens.Length >= 3)
            {
                if (tokens[2] == "START")
                {
                    var state = new LDrawTexmapState { Active = true };

                    if (tokens.Length >= 14 && tokens[3] == "PLANAR")
                    {
                        state.ProjectionType = TexmapProjectionType.Planar;
                        state.P1 = new Vector3(ParseFloat(tokens[4]), ParseFloat(tokens[5]), ParseFloat(tokens[6]));
                        state.P2 = new Vector3(ParseFloat(tokens[7]), ParseFloat(tokens[8]), ParseFloat(tokens[9]));
                        state.P3 = new Vector3(ParseFloat(tokens[10]), ParseFloat(tokens[11]), ParseFloat(tokens[12]));
                        state.TextureName = tokens[13];
                    }
                    else if (tokens.Length >= 16 && tokens[3] == "SPHERICAL")
                    {
                        state.ProjectionType = TexmapProjectionType.Spherical;
                        state.P1 = new Vector3(ParseFloat(tokens[4]), ParseFloat(tokens[5]), ParseFloat(tokens[6]));
                        state.P2 = new Vector3(ParseFloat(tokens[7]), ParseFloat(tokens[8]), ParseFloat(tokens[9]));
                        state.P3 = new Vector3(ParseFloat(tokens[10]), ParseFloat(tokens[11]), ParseFloat(tokens[12]));
                        state.Angle1 = ParseFloat(tokens[13]);
                        state.Angle2 = ParseFloat(tokens[14]);
                        state.TextureName = tokens[15];
                    }

                    parsed.Commands.Add(new ParsedCommand
                    {
                        Type = CommandType.TexmapStart,
                        TexmapState = state
                    });
                }
                else if (tokens[2] == "FALLBACK")
                {
                    parsed.Commands.Add(new ParsedCommand { Type = CommandType.TexmapFallback });
                }
                else if (tokens[2] == "END")
                {
                    parsed.Commands.Add(new ParsedCommand { Type = CommandType.TexmapEnd });
                }
                return;
            }

            // Handle BFC commands
            if (tokens[1] != "BFC") return;

            if (tokens.Length >= 3 && tokens[2] == "CERTIFY")
            {
                parsed.IsBfcCertified = true;
                // Check if CW is explicitly specified: "0 BFC CERTIFY CW"
                if (tokens.Length >= 4 && tokens[3] == "CW")
                {
                    parsed.BfcWindingCCW = false;
                    parsed.Commands.Add(new ParsedCommand { Type = CommandType.BfcCW });
                }
                else
                {
                    parsed.BfcWindingCCW = true;
                    parsed.Commands.Add(new ParsedCommand { Type = CommandType.BfcCCW });
                }
            }
            else if (tokens.Length >= 3 && tokens[2] == "NOCERTIFY")
            {
                parsed.Commands.Add(new ParsedCommand { Type = CommandType.BfcNoCertify });
            }
            else if (tokens.Length >= 3 && tokens[2] == "INVERTNEXT")
            {
                parsed.Commands.Add(new ParsedCommand { Type = CommandType.BfcInvertNext });
            }
            else if (tokens.Length >= 3 && tokens[2] == "CW")
            {
                parsed.Commands.Add(new ParsedCommand { Type = CommandType.BfcCW });
            }
            else if (tokens.Length >= 3 && tokens[2] == "CCW")
            {
                parsed.Commands.Add(new ParsedCommand { Type = CommandType.BfcCCW });
            }
        }

        void ParseSubFileRef(string line, ParsedFile parsed)
        {
            var parts = SplitLine(line);
            if (parts.Length < 15) return;

            int color = ParseInt(parts[1]);
            float x = ParseFloat(parts[2]);
            float y = ParseFloat(parts[3]);
            float z = ParseFloat(parts[4]);
            float a = ParseFloat(parts[5]);
            float b = ParseFloat(parts[6]);
            float c = ParseFloat(parts[7]);
            float d = ParseFloat(parts[8]);
            float e = ParseFloat(parts[9]);
            float f = ParseFloat(parts[10]);
            float g = ParseFloat(parts[11]);
            float h = ParseFloat(parts[12]);
            float i = ParseFloat(parts[13]);

            // The filename may contain spaces, so join remaining parts
            string fileName = parts[14];
            for (int idx = 15; idx < parts.Length; idx++)
                fileName += " " + parts[idx];

            var transform = LDrawCoordinates.BuildLDrawMatrix(x, y, z, a, b, c, d, e, f, g, h, i);

            parsed.Commands.Add(new ParsedCommand
            {
                Type = CommandType.SubFileRef,
                SubFileRef = new LDrawSubFileRef(color, transform, fileName.Trim())
            });
        }

        void ParseTriangle(string line, ParsedFile parsed)
        {
            var parts = SplitLine(line);
            if (parts.Length < 11) return;

            int color = ParseInt(parts[1]);
            var v1 = new Vector3(ParseFloat(parts[2]), ParseFloat(parts[3]), ParseFloat(parts[4]));
            var v2 = new Vector3(ParseFloat(parts[5]), ParseFloat(parts[6]), ParseFloat(parts[7]));
            var v3 = new Vector3(ParseFloat(parts[8]), ParseFloat(parts[9]), ParseFloat(parts[10]));

            parsed.Commands.Add(new ParsedCommand
            {
                Type = CommandType.Triangle,
                Triangle = new LDrawTriangle(color, v1, v2, v3)
            });
        }

        void ParseQuad(string line, ParsedFile parsed)
        {
            var parts = SplitLine(line);
            if (parts.Length < 14) return;

            int color = ParseInt(parts[1]);
            var v1 = new Vector3(ParseFloat(parts[2]), ParseFloat(parts[3]), ParseFloat(parts[4]));
            var v2 = new Vector3(ParseFloat(parts[5]), ParseFloat(parts[6]), ParseFloat(parts[7]));
            var v3 = new Vector3(ParseFloat(parts[8]), ParseFloat(parts[9]), ParseFloat(parts[10]));
            var v4 = new Vector3(ParseFloat(parts[11]), ParseFloat(parts[12]), ParseFloat(parts[13]));

            parsed.Commands.Add(new ParsedCommand
            {
                Type = CommandType.Quad,
                Quad = new LDrawQuad(color, v1, v2, v3, v4)
            });
        }

        static string[] SplitLine(string line)
        {
            return line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }

        static float ParseFloat(string s)
        {
            return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static int ParseInt(string s)
        {
            return int.Parse(s, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets the description (first line comment) of a part file without full parsing.
        /// </summary>
        public static string GetPartDescription(string filePath)
        {
            try
            {
                using var reader = new StreamReader(filePath);
                string firstLine = reader.ReadLine();
                if (firstLine != null && firstLine.StartsWith("0 "))
                    return firstLine.Substring(2).Trim();
            }
            catch { }
            return "";
        }

        // Internal types for parsed file representation
        enum CommandType
        {
            Triangle,
            Quad,
            SubFileRef,
            BfcInvertNext,
            BfcCW,
            BfcCCW,
            BfcNoCertify,
            TexmapStart,
            TexmapFallback,
            TexmapEnd
        }

        struct ParsedCommand
        {
            public CommandType Type;
            public LDrawTriangle Triangle;
            public LDrawQuad Quad;
            public LDrawSubFileRef SubFileRef;
            public LDrawTexmapState TexmapState;
        }

        class ParsedFile
        {
            public string Description;
            public bool IsBfcCertified;
            public bool BfcWindingCCW = true;
            public List<ParsedCommand> Commands = new List<ParsedCommand>();
        }
    }
}
