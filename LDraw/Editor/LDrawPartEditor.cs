using UnityEditor;
using UnityEngine;
using LDraw;

namespace LDraw.Editor
{
    [CustomEditor(typeof(LDrawPart))]
    public class LDrawPartEditor : UnityEditor.Editor
    {
        LDrawColorTable _colorTable;
        LDrawMaterialLibrary _matLib;
        bool _showColorPicker;
        int _editingSubmeshIndex = -1;
        Vector2 _colorScrollPos;
        string _colorSearch = "";

        void OnEnable()
        {
            _colorTable = AssetDatabase.LoadAssetAtPath<LDrawColorTable>(
                "Assets/LDraw/Resources/LDrawColorTable.asset");
            _matLib = AssetDatabase.LoadAssetAtPath<LDrawMaterialLibrary>(
                "Assets/LDraw/Resources/LDrawMaterialLibrary.asset");
        }

        public override void OnInspectorGUI()
        {
            var part = (LDrawPart)target;

            // Part info header
            EditorGUILayout.LabelField("Part Info", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Part Number", part.PartNumber);
            EditorGUILayout.LabelField("Description", part.Description);
            EditorGUILayout.Space(8);

            // Submesh color assignments
            EditorGUILayout.LabelField("Submesh Materials", EditorStyles.boldLabel);

            if (part.Submeshes == null || part.Submeshes.Length == 0)
            {
                EditorGUILayout.HelpBox("No submesh data. Reconvert this part.", MessageType.Warning);
                return;
            }

            for (int i = 0; i < part.Submeshes.Length; i++)
            {
                var submesh = part.Submeshes[i];
                DrawSubmeshRow(part, i, submesh);
            }

            EditorGUILayout.Space(8);

            // Quick color setter
            EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Set All Colors..."))
            {
                _editingSubmeshIndex = -2; // Special: main color mode
                _showColorPicker = true;
                _colorSearch = "";
            }
            if (GUILayout.Button("Refresh Materials"))
            {
                part.ApplyColors();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // Color picker panel
            if (_showColorPicker)
                DrawColorPicker(part);
        }

        void DrawSubmeshRow(LDrawPart part, int index, LDrawPart.SubmeshColor submesh)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // Color swatch
            string colorName = "Unknown";
            Color swatchColor = Color.gray;
            if (_colorTable != null)
            {
                var entry = _colorTable.GetColor(submesh.CurrentColorCode);
                if (entry != null)
                {
                    colorName = entry.Name;
                    swatchColor = entry.Value;
                }
            }

            // Draw color preview rect
            var rect = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24));
            EditorGUI.DrawRect(rect, swatchColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), Color.black);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Color.black);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), Color.black);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), Color.black);

            // Label
            EditorGUILayout.LabelField($"{submesh.Label}: {colorName} ({submesh.CurrentColorCode})");

            // Change button
            if (GUILayout.Button("Change", GUILayout.Width(60)))
            {
                _editingSubmeshIndex = index;
                _showColorPicker = true;
                _colorSearch = "";
            }

            EditorGUILayout.EndHorizontal();
        }

        void DrawColorPicker(LDrawPart part)
        {
            if (_colorTable == null)
            {
                EditorGUILayout.HelpBox("Color table not loaded.", MessageType.Error);
                return;
            }

            string title = _editingSubmeshIndex == -2
                ? "Pick Color (applies to all submeshes)"
                : $"Pick Color for Submesh {_editingSubmeshIndex}";

            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _colorSearch = EditorGUILayout.TextField("Search:", _colorSearch);
            if (GUILayout.Button("Close", GUILayout.Width(50)))
            {
                _showColorPicker = false;
                _editingSubmeshIndex = -1;
            }
            EditorGUILayout.EndHorizontal();

            string search = _colorSearch.ToLowerInvariant();

            _colorScrollPos = EditorGUILayout.BeginScrollView(_colorScrollPos, GUILayout.MaxHeight(300));

            int columns = 6;
            int col = 0;
            EditorGUILayout.BeginHorizontal();

            foreach (var colorEntry in _colorTable.Colors)
            {
                if (!string.IsNullOrEmpty(search))
                {
                    if (!colorEntry.Name.ToLowerInvariant().Contains(search) &&
                        !colorEntry.Code.ToString().Contains(search))
                        continue;
                }

                // Skip transparent colors from quick list unless searching for them
                if (string.IsNullOrEmpty(search) && colorEntry.Finish == LDrawFinishType.Transparent)
                    continue;

                var style = new GUIStyle(GUI.skin.button)
                {
                    fixedWidth = 56,
                    fixedHeight = 40,
                    fontSize = 8,
                    alignment = TextAnchor.LowerCenter,
                    padding = new RectOffset(1, 1, 1, 2)
                };

                // Draw button with color background
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = colorEntry.Value;

                string label = colorEntry.Code.ToString();
                if (GUILayout.Button(label, style))
                {
                    Undo.RecordObject(part, "Change LDraw Part Color");
                    if (_editingSubmeshIndex == -2)
                        part.SetAllColors(colorEntry.Code);
                    else
                        part.SetSubmeshColor(_editingSubmeshIndex, colorEntry.Code);
                    EditorUtility.SetDirty(part);
                    _showColorPicker = false;
                    _editingSubmeshIndex = -1;
                }

                GUI.backgroundColor = prevBg;

                col++;
                if (col >= columns)
                {
                    col = 0;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();

            // Also show a text field for entering a code directly
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Or enter code:", GUILayout.Width(90));
            string codeStr = EditorGUILayout.TextField("", GUILayout.Width(60));
            if (!string.IsNullOrEmpty(codeStr) && int.TryParse(codeStr, out int code))
            {
                if (_colorTable.GetColor(code) != null)
                {
                    Undo.RecordObject(part, "Change LDraw Part Color");
                    if (_editingSubmeshIndex == -2)
                        part.SetAllColors(code);
                    else
                        part.SetSubmeshColor(_editingSubmeshIndex, code);
                    EditorUtility.SetDirty(part);
                    _showColorPicker = false;
                    _editingSubmeshIndex = -1;
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
