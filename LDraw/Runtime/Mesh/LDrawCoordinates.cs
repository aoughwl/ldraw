using UnityEngine;

namespace LDraw
{
    public static class LDrawCoordinates
    {
        public const float LDUToUnity = 0.05f;

        public static Vector3 ConvertPosition(Vector3 ldrawPos)
        {
            return new Vector3(ldrawPos.x, -ldrawPos.y, -ldrawPos.z) * LDUToUnity;
        }

        public static Vector3 ConvertPosition(float x, float y, float z)
        {
            return new Vector3(x, -y, -z) * LDUToUnity;
        }

        /// <summary>
        /// Converts an LDraw 12-float transform (a b c d e f g h i x y z) into a Unity Matrix4x4.
        /// LDraw line type 1: color x y z a b c d e f g h i file
        /// The LDraw matrix is:
        ///   | a b c x |
        ///   | d e f y |
        ///   | g h i z |
        ///   | 0 0 0 1 |
        /// We apply F * M * F where F = diag(1, -1, -1) to convert coordinate systems,
        /// then scale the translation by LDUToUnity.
        /// </summary>
        public static Matrix4x4 ConvertTransformMatrix(
            float x, float y, float z,
            float a, float b, float c,
            float d, float e, float f,
            float g, float h, float i)
        {
            // LDraw matrix M:
            // | a  b  c  x |
            // | d  e  f  y |
            // | g  h  i  z |
            // | 0  0  0  1 |
            //
            // F = diag(1, -1, -1)
            // Unity matrix = F * M * F
            //
            // F * M * F gives:
            // |  a  -b  -c   x  |
            // | -d   e   f  -y  |
            // | -g   h   i  -z  |
            // |  0   0   0   1  |

            var m = new Matrix4x4();
            m.m00 = a;  m.m01 = -b; m.m02 = -c; m.m03 = x * LDUToUnity;
            m.m10 = -d; m.m11 = e;  m.m12 = f;  m.m13 = -y * LDUToUnity;
            m.m20 = -g; m.m21 = h;  m.m22 = i;  m.m23 = -z * LDUToUnity;
            m.m30 = 0;  m.m31 = 0;  m.m32 = 0;  m.m33 = 1;

            return m;
        }

        /// <summary>
        /// Converts an LDraw transform matrix (already as Matrix4x4 in LDraw space) to Unity space.
        /// </summary>
        public static Matrix4x4 ConvertMatrix(Matrix4x4 ldrawMatrix)
        {
            return ConvertTransformMatrix(
                ldrawMatrix.m03, ldrawMatrix.m13, ldrawMatrix.m23,
                ldrawMatrix.m00, ldrawMatrix.m01, ldrawMatrix.m02,
                ldrawMatrix.m10, ldrawMatrix.m11, ldrawMatrix.m12,
                ldrawMatrix.m20, ldrawMatrix.m21, ldrawMatrix.m22);
        }

        /// <summary>
        /// Builds an LDraw-space Matrix4x4 from the 12 floats (before coordinate conversion).
        /// </summary>
        public static Matrix4x4 BuildLDrawMatrix(
            float x, float y, float z,
            float a, float b, float c,
            float d, float e, float f,
            float g, float h, float i)
        {
            var m = new Matrix4x4();
            m.m00 = a; m.m01 = b; m.m02 = c; m.m03 = x;
            m.m10 = d; m.m11 = e; m.m12 = f; m.m13 = y;
            m.m20 = g; m.m21 = h; m.m22 = i; m.m23 = z;
            m.m30 = 0; m.m31 = 0; m.m32 = 0; m.m33 = 1;
            return m;
        }
    }
}
