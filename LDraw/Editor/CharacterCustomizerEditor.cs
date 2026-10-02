using UnityEditor;
using UnityEngine;

namespace LDraw.Editor
{
    /// <summary>
    /// Custom inspector for CharacterCustomizer that supports:
    /// - Base Clothing asset assignment
    /// - Per-slot LDraw override selection
    /// </summary>
    [CustomEditor(typeof(CharacterCustomizer))]
    public class CharacterCustomizerEditor : UnityEditor.Editor
    {
        private static BodyPartSlot currentSlotBeingEdited;
        private static CharacterCustomizer currentCustomizer;

        public override void OnInspectorGUI()
        {
            CharacterCustomizer customizer = (CharacterCustomizer)target;

            serializedObject.Update();

            EditorGUILayout.LabelField("Character Body Parts", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Assign original meshes/slot transforms. Use Base Clothing as default and per-slot overrides when needed.",
                MessageType.Info);
            EditorGUILayout.Space();

            DrawBaseClothingSection(customizer);
            EditorGUILayout.Space();

            DrawPropertiesExcluding(serializedObject, "m_Script", "baseClothing");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Slot Overrides", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Click a slot to select an LDraw part override for that slot only.",
                MessageType.Info);
            EditorGUILayout.Space();

            DrawSlotButton(customizer, customizer.Head, "Head");
            DrawSlotButton(customizer, customizer.Hat, "Hat");
            EditorGUILayout.Space();
            DrawSlotButton(customizer, customizer.Torso, "Torso");
            DrawSlotButton(customizer, customizer.Hips, "Hips");
            EditorGUILayout.Space();
            DrawSlotButton(customizer, customizer.LeftArm, "Left Arm");
            DrawSlotButton(customizer, customizer.RightArm, "Right Arm");
            EditorGUILayout.Space();
            DrawSlotButton(customizer, customizer.LeftHand, "Left Hand");
            DrawSlotButton(customizer, customizer.RightHand, "Right Hand");
            DrawSlotButton(customizer, customizer.LeftHandItem, "Left Hand Item");
            DrawSlotButton(customizer, customizer.RightHandItem, "Right Hand Item");
            EditorGUILayout.Space();
            DrawSlotButton(customizer, customizer.LeftLeg, "Left Leg");
            DrawSlotButton(customizer, customizer.RightLeg, "Right Leg");

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Clear All Slot Overrides", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog(
                        "Clear All Overrides",
                        "This clears all direct slot overrides and reapplies the base clothing preset.",
                        "Clear", "Cancel"))
                {
                    customizer.ResetAllParts();
                    EditorUtility.SetDirty(customizer);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawBaseClothingSection(CharacterCustomizer customizer)
        {
            SerializedProperty baseClothingProp = serializedObject.FindProperty("baseClothing");
            EditorGUILayout.PropertyField(baseClothingProp, new GUIContent("Base Clothing"));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Base Clothing"))
            {
                customizer.ApplyBaseClothing();
                EditorUtility.SetDirty(customizer);
            }

            if (GUILayout.Button("Clear Base Clothing"))
            {
                Undo.RecordObject(customizer, "Clear Base Clothing");
                customizer.SetBaseClothing(null);
                EditorUtility.SetDirty(customizer);
            }

            if (GUILayout.Button("Open Clothing Asset"))
            {
                if (customizer.BaseClothing != null)
                {
                    Selection.activeObject = customizer.BaseClothing;
                    EditorGUIUtility.PingObject(customizer.BaseClothing);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSlotButton(CharacterCustomizer customizer, BodyPartSlot slot, string displayName)
        {
            EditorGUILayout.BeginHorizontal();

            bool hasOverride = slot.HasManualOverride && (slot.ReplacementMesh != null || slot.ReplacementPrefab != null);
            bool hasBase = slot.BaseClothingMesh != null;

            string status;
            if (hasOverride)
            {
                string partName = slot.ReplacementMesh != null ? slot.ReplacementMesh.name :
                    (slot.ReplacementPrefab != null ? slot.ReplacementPrefab.name : "Override");
                status = "[Override] " + partName;
            }
            else if (hasBase)
            {
                status = "[Base Clothing]";
            }
            else
            {
                status = "Original";
            }

            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = hasOverride ? new Color(0.6f, 0.9f, 0.6f) :
                (hasBase ? new Color(0.75f, 0.85f, 1f) : Color.white);

            if (GUILayout.Button(displayName + ": " + status, GUILayout.Height(25)))
            {
                OpenLDrawBrowserForSlot(customizer, slot);
            }

            GUI.backgroundColor = originalColor;

            if (hasOverride)
            {
                if (GUILayout.Button("Clear", GUILayout.Width(60), GUILayout.Height(25)))
                {
                    customizer.SetBodyPart(slot, null);
                    EditorUtility.SetDirty(customizer);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void OpenLDrawBrowserForSlot(CharacterCustomizer customizer, BodyPartSlot slot)
        {
            currentSlotBeingEdited = slot;
            currentCustomizer = customizer;

            LDrawModelBrowser.OnPartSelected = partGameObject => OnLDrawPartSelected(partGameObject);

            LDrawModelBrowser.SetSelectionMode(true,
                buttonText: $"Set Override: {slot.SlotName}",
                helpText: $"Character Customization: selecting override part for {slot.SlotName}");

            LDrawModelBrowser browser = EditorWindow.GetWindow<LDrawModelBrowser>($"Select Part for {slot.SlotName}");
            browser.Show();
        }

        /// <summary>
        /// Called by LDrawModelBrowser when a part is selected.
        /// </summary>
        public static void OnLDrawPartSelected(GameObject partGameObject)
        {
            if (currentSlotBeingEdited == null || currentCustomizer == null || partGameObject == null)
                return;

            if (!AssetDatabase.IsValidFolder("Assets/LDraw/GeneratedPrefabs"))
                AssetDatabase.CreateFolder("Assets/LDraw", "GeneratedPrefabs");

            string prefabPath = $"Assets/LDraw/GeneratedPrefabs/{partGameObject.name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(partGameObject, prefabPath);
            currentSlotBeingEdited.ReplacementPrefab = prefab;
            currentSlotBeingEdited.HasManualOverride = true;

            currentCustomizer.SetBodyPart(currentSlotBeingEdited, partGameObject);
            EditorUtility.SetDirty(currentCustomizer);

            Debug.Log($"Assigned {prefab.name} override to {currentSlotBeingEdited.SlotName}");

            currentSlotBeingEdited = null;
            currentCustomizer = null;
        }
    }
}
