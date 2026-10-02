using UnityEditor;
using UnityEngine;
using LDraw;

namespace LDraw.Editor
{
    [CustomEditor(typeof(BlockFlatWorldGenerator))]
    [CanEditMultipleObjects]
    public class BlockFlatWorldGeneratorEditor : UnityEditor.Editor
    {
        private LDrawColorTable colorTable;
        private bool showColors = true;

        private void OnEnable()
        {
            colorTable = AssetDatabase.LoadAssetAtPath<LDrawColorTable>(
                "Assets/LDraw/Resources/LDrawColorTable.asset");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawProperty("seed");
            DrawProperty("useRandomSeed");

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Flat World", EditorStyles.boldLabel);
            DrawProperty("body1x1PartNumber", "Body 1x1 Part");
            DrawProperty("depthPlates", "Depth (plates)");
            DrawProperty("bedrockThicknessPlates", "Bedrock Thickness");
            DrawProperty("disableBrickPhysics", "Disable Brick Physics");

            EditorGUILayout.Space(8f);
            DrawColorSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawProperty(string propertyName, string label = null)
        {
            SerializedProperty p = serializedObject.FindProperty(propertyName);
            if (p == null) return;
            if (string.IsNullOrEmpty(label)) EditorGUILayout.PropertyField(p);
            else EditorGUILayout.PropertyField(p, new GUIContent(label));
        }

        private void DrawColorSection()
        {
            showColors = EditorGUILayout.Foldout(showColors, "Material Regions", true, EditorStyles.foldoutHeader);
            if (!showColors) return;

            DrawColorCodeRow("Surface", serializedObject.FindProperty("surfaceColorCode"));
            DrawColorCodeRow("Subsurface", serializedObject.FindProperty("subsurfaceColorCode"));
            DrawColorCodeRow("Bedrock", serializedObject.FindProperty("bedrockColorCode"));
        }

        private void DrawColorCodeRow(string label, SerializedProperty colorCodeProp)
        {
            if (colorCodeProp == null) return;

            int code = colorCodeProp.intValue;
            string colorName = $"Code {code}";
            Color swatch = Color.gray;
            if (colorTable != null)
            {
                LDrawColorEntry entry = colorTable.GetColor(code);
                if (entry != null)
                {
                    colorName = entry.Name;
                    swatch = entry.Value;
                }
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            Rect swatchRect = GUILayoutUtility.GetRect(20f, 16f, GUILayout.Width(20f));
            EditorGUI.DrawRect(swatchRect, swatch);
            if (GUILayout.Button($"{colorName} ({code})", GUILayout.Width(220f)))
            {
                ShowColorPopup(colorCodeProp);
            }

            int newCode = EditorGUILayout.IntField(code, GUILayout.Width(70f));
            if (newCode != code)
            {
                colorCodeProp.intValue = newCode;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ShowColorPopup(SerializedProperty targetProperty)
        {
            if (colorTable == null || targetProperty == null) return;

            string propertyPath = targetProperty.propertyPath;
            GenericMenu menu = new GenericMenu();
            foreach (LDrawColorEntry entry in colorTable.Colors)
            {
                if (entry == null) continue;
                int capturedCode = entry.Code;
                string group = entry.Finish == LDrawFinishType.Solid
                    ? "Solid"
                    : entry.Finish == LDrawFinishType.Transparent ? "Transparent" : "Special";
                menu.AddItem(
                    new GUIContent($"{group}/{entry.Name} ({entry.Code})"),
                    targetProperty.intValue == capturedCode,
                    () =>
                    {
                        serializedObject.Update();
                        SerializedProperty p = serializedObject.FindProperty(propertyPath);
                        if (p != null) p.intValue = capturedCode;
                        serializedObject.ApplyModifiedProperties();
                    });
            }

            menu.ShowAsContext();
        }
    }
}
