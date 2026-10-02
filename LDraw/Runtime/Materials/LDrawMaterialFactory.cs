using UnityEngine;
using UnityEngine.Rendering;

namespace LDraw
{
    public static class LDrawMaterialFactory
    {
        static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapProp = Shader.PropertyToID("_BaseMap");
        static readonly int SmoothnessProperty = Shader.PropertyToID("_Smoothness");
        static readonly int MetallicProperty = Shader.PropertyToID("_Metallic");
        static readonly int SurfaceTypeProp = Shader.PropertyToID("_Surface");
        static readonly int BlendProp = Shader.PropertyToID("_Blend");
        static readonly int SrcBlendProp = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendProp = Shader.PropertyToID("_DstBlend");
        static readonly int SrcBlendAlphaProp = Shader.PropertyToID("_SrcBlendAlpha");
        static readonly int DstBlendAlphaProp = Shader.PropertyToID("_DstBlendAlpha");
        static readonly int ZWriteProp = Shader.PropertyToID("_ZWrite");
        static readonly int AlphaClipProp = Shader.PropertyToID("_AlphaClip");
        static readonly int QueueOffsetProp = Shader.PropertyToID("_QueueOffset");

        public static Material CreateMaterial(LDrawColorEntry colorEntry)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("URP Lit shader not found. Make sure URP is installed.");
                return null;
            }

            var mat = new Material(shader);
            mat.name = $"LDraw_Color_{colorEntry.Code}_{SanitizeName(colorEntry.Name)}";

            // Base color
            mat.SetColor(BaseColorProp, colorEntry.Value);
            mat.color = colorEntry.Value;

            // Apply finish-specific properties
            switch (colorEntry.Finish)
            {
                case LDrawFinishType.Solid:
                    mat.SetFloat(SmoothnessProperty, 0.7f);
                    mat.SetFloat(MetallicProperty, 0f);
                    break;

                case LDrawFinishType.Transparent:
                    mat.SetFloat(SmoothnessProperty, 0.8f);
                    mat.SetFloat(MetallicProperty, 0f);
                    SetupTransparent(mat);
                    break;

                case LDrawFinishType.Chrome:
                    mat.SetFloat(SmoothnessProperty, 0.95f);
                    mat.SetFloat(MetallicProperty, 1.0f);
                    break;

                case LDrawFinishType.Pearl:
                    mat.SetFloat(SmoothnessProperty, 0.85f);
                    mat.SetFloat(MetallicProperty, 0.3f);
                    break;

                case LDrawFinishType.Rubber:
                    mat.SetFloat(SmoothnessProperty, 0.2f);
                    mat.SetFloat(MetallicProperty, 0f);
                    break;

                case LDrawFinishType.MatteMetallic:
                    mat.SetFloat(SmoothnessProperty, 0.4f);
                    mat.SetFloat(MetallicProperty, 0.8f);
                    break;

                case LDrawFinishType.Metal:
                    mat.SetFloat(SmoothnessProperty, 0.8f);
                    mat.SetFloat(MetallicProperty, 0.9f);
                    break;

                case LDrawFinishType.Glitter:
                    mat.SetFloat(SmoothnessProperty, 0.7f);
                    mat.SetFloat(MetallicProperty, 0.2f);
                    if (colorEntry.Alpha < 255f)
                        SetupTransparent(mat);
                    break;

                case LDrawFinishType.Speckle:
                    mat.SetFloat(SmoothnessProperty, 0.6f);
                    mat.SetFloat(MetallicProperty, 0.3f);
                    break;
            }

            return mat;
        }

        /// <summary>
        /// Creates a material with both color and texture applied.
        /// Used for textured parts (stickers, printed elements).
        /// </summary>
        public static Material CreateTexturedMaterial(LDrawColorEntry colorEntry, Texture2D texture)
        {
            var mat = CreateMaterial(colorEntry);
            if (mat == null) return null;

            mat.name = $"LDraw_Color_{colorEntry.Code}_{texture.name}";
            mat.SetTexture(BaseMapProp, texture);

            // Set base color to white so texture isn't tinted
            // (LDraw textures are typically pre-colored)
            mat.SetColor(BaseColorProp, Color.white);
            mat.color = Color.white;

            // LDraw TEXMAP textures are sticker/print overlays that need alpha blending.
            // Always enable transparency — compressed formats (DXT5, BC7) also carry alpha
            // but don't match uncompressed format checks.
            SetupTransparent(mat);

            return mat;
        }

        static void SetupTransparent(Material mat)
        {
            mat.SetFloat(SurfaceTypeProp, 1); // Transparent
            mat.SetFloat(BlendProp, 0); // Alpha blend
            mat.SetFloat(SrcBlendProp, (float)BlendMode.SrcAlpha);
            mat.SetFloat(DstBlendProp, (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat(SrcBlendAlphaProp, (float)BlendMode.One);
            mat.SetFloat(DstBlendAlphaProp, (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat(ZWriteProp, 0);
            mat.SetFloat(AlphaClipProp, 0);
            mat.SetFloat(QueueOffsetProp, 0);
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            return name.Replace(' ', '_').Replace('/', '_').Replace('\\', '_');
        }
    }
}
