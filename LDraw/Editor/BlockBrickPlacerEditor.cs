using UnityEditor;
using UnityEngine;
using LDraw;

namespace LDraw.Editor
{
    [CustomEditor(typeof(BlockBrickPlacer))]
    [CanEditMultipleObjects]
    public class BlockBrickPlacerEditor : UnityEditor.Editor
    {
        private static BlockBrickPlacer currentPlacer;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            BlockBrickPlacer placer = (BlockBrickPlacer)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Brick Selection", EditorStyles.boldLabel);

            var prefabProp = serializedObject.FindProperty("currentBrickPrefab");
            GameObject currentPrefab = prefabProp.objectReferenceValue as GameObject;
            bool hasSelection = currentPrefab != null;

            string buttonText = hasSelection ? currentPrefab.name : "Select Brick...";

            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = hasSelection ? Color.green : Color.white;

            if (GUILayout.Button(buttonText, GUILayout.Height(30)))
            {
                OpenLDrawBrowser(placer);
            }

            GUI.backgroundColor = originalColor;

            if (hasSelection)
            {
                if (GUILayout.Button("Clear", GUILayout.Height(20)))
                {
                    prefabProp.objectReferenceValue = null;
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(placer);
                }
            }
        }

        private void OpenLDrawBrowser(BlockBrickPlacer placer)
        {
            currentPlacer = placer;

            LDrawModelBrowser.OnPartSelected = (GameObject partGO) =>
            {
                OnPartSelected(partGO);
            };

            LDrawModelBrowser.SetSelectionMode(true,
                buttonText: "Select",
                addBuildingComponents: true,
                helpText: "Select a brick for placement");

            var browser = EditorWindow.GetWindow<LDrawModelBrowser>("Select Brick");
            browser.Show();
        }

        private static void OnPartSelected(GameObject partGO)
        {
            if (currentPlacer == null || partGO == null) return;

            // Ensure output directory exists
            if (!AssetDatabase.IsValidFolder("Assets/LDraw/GeneratedPrefabs"))
                AssetDatabase.CreateFolder("Assets/LDraw", "GeneratedPrefabs");

            // Save the GameObject as a prefab asset
            string prefabPath = $"Assets/LDraw/GeneratedPrefabs/{partGO.name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(partGO, prefabPath);

            // Assign the saved prefab to the placer
            var so = new SerializedObject(currentPlacer);
            so.FindProperty("currentBrickPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(currentPlacer);

            // Destroy the temporary scene object
            DestroyImmediate(partGO);

            Debug.Log($"Set brick to {prefab.name} (saved to {prefabPath})");
            currentPlacer = null;
        }
    }
}
