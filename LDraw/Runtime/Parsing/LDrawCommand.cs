using UnityEngine;

namespace LDraw
{
    public struct LDrawTriangle
    {
        public int Color;
        public Vector3 V1;
        public Vector3 V2;
        public Vector3 V3;

        public LDrawTriangle(int color, Vector3 v1, Vector3 v2, Vector3 v3)
        {
            Color = color;
            V1 = v1;
            V2 = v2;
            V3 = v3;
        }
    }

    public struct LDrawQuad
    {
        public int Color;
        public Vector3 V1;
        public Vector3 V2;
        public Vector3 V3;
        public Vector3 V4;

        public LDrawQuad(int color, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4)
        {
            Color = color;
            V1 = v1;
            V2 = v2;
            V3 = v3;
            V4 = v4;
        }
    }

    public struct LDrawSubFileRef
    {
        public int Color;
        public Matrix4x4 Transform;
        public string FileName;

        public LDrawSubFileRef(int color, Matrix4x4 transform, string fileName)
        {
            Color = color;
            Transform = transform;
            FileName = fileName;
        }
    }

    public struct LDrawTexturedTriangle
    {
        public int Color;
        public Vector3 V1, V2, V3;
        public Vector2 UV1, UV2, UV3;
        public string TextureName;

        public LDrawTexturedTriangle(int color, Vector3 v1, Vector3 v2, Vector3 v3,
            Vector2 uv1, Vector2 uv2, Vector2 uv3, string textureName)
        {
            Color = color;
            V1 = v1; V2 = v2; V3 = v3;
            UV1 = uv1; UV2 = uv2; UV3 = uv3;
            TextureName = textureName;
        }
    }

    public struct LDrawTexturedQuad
    {
        public int Color;
        public Vector3 V1, V2, V3, V4;
        public Vector2 UV1, UV2, UV3, UV4;
        public string TextureName;

        public LDrawTexturedQuad(int color, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
            Vector2 uv1, Vector2 uv2, Vector2 uv3, Vector2 uv4, string textureName)
        {
            Color = color;
            V1 = v1; V2 = v2; V3 = v3; V4 = v4;
            UV1 = uv1; UV2 = uv2; UV3 = uv3; UV4 = uv4;
            TextureName = textureName;
        }
    }

    /// <summary>
    /// Type of connection point detected from LDraw subfile primitives.
    /// Values map directly to ConnectionType enum indices for easy conversion.
    /// </summary>
    public enum LDrawConnectionType : byte
    {
        Stud = 0,            // stud.dat variants (male, top of brick)
        AntiStud = 1,        // Derived from stud positions (female, bottom of brick)
        TechnicHole = 2,     // beamhole.dat / connhole.dat (round female, center-placed through-holes)
        TechnicPin = 3,      // connect.dat / confric.dat (round male)
        TechnicAxleHole = 4, // axlehole.dat variants (cross female); depth heuristic decides single vs through
        TechnicAxle = 5,     // axleend.dat / axleend2.dat (cross male)
        BottomTube = 6,      // stud3/stud4 center tubes (intermediate: triggers anti-stud derivation, converts to TechnicHole)
        TechnicHoleSurface = 7, // peghole*.dat (face-placed pin hole; intermediate: converts to TechnicHole without duplication)
    }

    /// <summary>
    /// A connection point reference detected during LDraw parsing.
    /// Position and direction are in LDraw coordinate space.
    /// </summary>
    public struct LDrawConnectionReference
    {
        public LDrawConnectionType Type;
        public Vector3 PositionLDraw;
        public Vector3 DirectionLDraw;
    }
}
