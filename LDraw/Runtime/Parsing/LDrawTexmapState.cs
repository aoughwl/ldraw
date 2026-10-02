using UnityEngine;

namespace LDraw
{
    public enum TexmapProjectionType { None, Planar, Spherical }

    public struct LDrawTexmapState
    {
        public bool Active;
        public TexmapProjectionType ProjectionType;
        public Vector3 P1, P2, P3;  // Projection points in LDraw space
        public float Angle1, Angle2;  // For SPHERICAL only
        public string TextureName;

        public static LDrawTexmapState Default => new LDrawTexmapState { Active = false };

        /// <summary>
        /// Projects 3D vertex to 2D UV coordinates based on projection type.
        /// Must be called in LDraw coordinate space BEFORE transformation to Unity space.
        /// </summary>
        public Vector2 ProjectUV(Vector3 vertexInLDrawSpace)
        {
            if (ProjectionType == TexmapProjectionType.Planar)
                return ProjectPlanar(vertexInLDrawSpace);
            else if (ProjectionType == TexmapProjectionType.Spherical)
                return ProjectSpherical(vertexInLDrawSpace);
            return Vector2.zero;
        }

        /// <summary>
        /// PLANAR projection: P1→UV(0,0), P2→UV(1,0), P3→UV(0,1)
        /// Projects vertex onto the plane defined by P1, P2, P3.
        /// </summary>
        Vector2 ProjectPlanar(Vector3 vertex)
        {
            Vector3 u = P2 - P1;
            Vector3 v = P3 - P1;
            Vector3 delta = vertex - P1;

            float uLen2 = Vector3.Dot(u, u);
            float vLen2 = Vector3.Dot(v, v);

            if (uLen2 < 1e-8f || vLen2 < 1e-8f)
                return Vector2.zero;

            float uvX = Vector3.Dot(delta, u) / uLen2;
            float uvY = Vector3.Dot(delta, v) / vLen2;

            // LDraw TEXMAP uses image coordinates (origin top-left, Y down)
            // Unity UVs use bottom-left origin (V up), so flip V
            return new Vector2(uvX, 1.0f - uvY);
        }

        /// <summary>
        /// SPHERICAL projection: P1=center, P2=longitude reference, P3=latitude reference
        /// Projects vertex onto sphere and maps to UV using longitude/latitude angles.
        /// </summary>
        Vector2 ProjectSpherical(Vector3 vertex)
        {
            Vector3 delta = vertex - P1;
            Vector3 right = (P2 - P1).normalized;
            Vector3 up = (P3 - P1).normalized;
            Vector3 forward = Vector3.Cross(right, up).normalized;

            float x = Vector3.Dot(delta, right);
            float y = Vector3.Dot(delta, up);
            float z = Vector3.Dot(delta, forward);

            float longitude = Mathf.Atan2(z, x) * Mathf.Rad2Deg;
            float radius = Mathf.Sqrt(x * x + z * z);
            float latitude = Mathf.Atan2(y, radius) * Mathf.Rad2Deg;

            // Avoid division by zero
            float u = Angle1 != 0f ? longitude / Angle1 : 0f;
            float v = Angle2 != 0f ? latitude / Angle2 : 0f;

            // LDraw TEXMAP uses image coordinates (origin top-left, Y down)
            // Unity UVs use bottom-left origin (V up), so flip V
            return new Vector2(u, 1.0f - v);
        }
    }
}
