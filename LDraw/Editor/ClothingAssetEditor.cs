using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using LDraw;

namespace LDraw.Editor
{
    [CustomEditor(typeof(ClothingAsset))]
    public class ClothingAssetEditor : UnityEditor.Editor
    {
        private static ClothingAsset editingAsset;
        private static string editingSlotName;
        private LDrawColorTable colorTable;
        private readonly Dictionary<string, bool> slotFoldouts = new Dictionary<string, bool>();

        private static readonly string[] SlotNames =
        {
            "Head", "Hat", "Torso", "Hips", "Left Arm", "Right Arm",
            "Left Hand", "Right Hand", "Left Hand Item", "Right Hand Item",
            "Left Leg", "Right Leg"
        };

        public override void OnInspectorGUI()
        {
            ClothingAsset asset = (ClothingAsset)target;

            EditorGUILayout.HelpBox(
                "Assign LDraw model prefabs per slot. Use Change Colors to edit submesh color codes and see results.",
                MessageType.Info);
            EditorGUILayout.Space();

            for (int i = 0; i < SlotNames.Length; i++)
            {
                DrawSlotCard(asset, SlotNames[i]);
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Preview Outfit", GUILayout.Height(28)))
            {
                ClothingOutfitPreviewWindow.ShowWindow(asset);
            }
        }

        private void OnEnable()
        {
            colorTable = AssetDatabase.LoadAssetAtPath<LDrawColorTable>("Assets/LDraw/Resources/LDrawColorTable.asset");
        }

        private void DrawSlotCard(ClothingAsset asset, string slotName)
        {
            ClothingSlotDefinition slot = asset.GetSlotByName(slotName);
            if (slot == null)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(slotName, EditorStyles.boldLabel);

            GameObject updatedPrefab = (GameObject)EditorGUILayout.ObjectField(
                "Prefab", slot.ReplacementPrefab, typeof(GameObject), false);
            if (updatedPrefab != slot.ReplacementPrefab)
            {
                Undo.RecordObject(asset, "Set Clothing Slot Prefab");
                slot.ReplacementPrefab = updatedPrefab;
                SyncSubmeshOverridesFromPrefab(slot);
                EditorUtility.SetDirty(asset);
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Select LDraw Model"))
            {
                OpenLDrawBrowserForSlot(asset, slotName);
            }

            GUI.enabled = slot.ReplacementPrefab != null;
            if (GUILayout.Button("Change Colors"))
            {
                ClothingSlotPreviewWindow.ShowWindow(asset, slotName);
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            if (slot.ReplacementPrefab != null)
            {
                EditorGUILayout.LabelField("Part", string.IsNullOrWhiteSpace(slot.PartNumber) ? "-" : slot.PartNumber);
                EditorGUILayout.LabelField("Description", string.IsNullOrWhiteSpace(slot.PartDescription) ? "-" : slot.PartDescription);
                EditorGUILayout.LabelField("Submesh Overrides", slot.SubmeshColors != null ? slot.SubmeshColors.Count.ToString() : "0");
                DrawSubmeshColorList(slotName, slot);
            }
            else
            {
                EditorGUILayout.LabelField("Status", "No base part assigned");
            }

            EditorGUILayout.EndVertical();
        }

        private static void OpenLDrawBrowserForSlot(ClothingAsset asset, string slotName)
        {
            editingAsset = asset;
            editingSlotName = slotName;

            LDrawModelBrowser.OnPartSelected = partGameObject => OnLDrawPartSelected(partGameObject);
            LDrawModelBrowser.SetSelectionMode(true,
                buttonText: $"Set Clothing {slotName}",
                helpText: $"Clothing Asset: selecting base part for {slotName}");

            LDrawModelBrowser browser = EditorWindow.GetWindow<LDrawModelBrowser>($"Select Clothing {slotName}");
            browser.Show();
        }

        public static void OnLDrawPartSelected(GameObject partGameObject)
        {
            if (editingAsset == null || string.IsNullOrWhiteSpace(editingSlotName) || partGameObject == null)
                return;

            ClothingSlotDefinition slot = editingAsset.GetSlotByName(editingSlotName);
            if (slot == null)
                return;

            EnsureFolder("Assets/LDraw/GeneratedPrefabs");
            EnsureFolder("Assets/LDraw/GeneratedPrefabs/Clothing");

            string prefabPath = $"Assets/LDraw/GeneratedPrefabs/Clothing/{partGameObject.name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(partGameObject, prefabPath);

            Undo.RecordObject(editingAsset, "Set Clothing Slot Model");
            slot.ReplacementPrefab = prefab;
            SyncSubmeshOverridesFromInstance(slot, partGameObject);
            EditorUtility.SetDirty(editingAsset);

            Debug.Log($"ClothingAsset: assigned {prefab.name} to {editingSlotName}");

            Object.DestroyImmediate(partGameObject);

            editingAsset = null;
            editingSlotName = null;
        }

        private static void SyncSubmeshOverridesFromPrefab(ClothingSlotDefinition slot)
        {
            if (slot == null)
                return;

            slot.SubmeshColors.Clear();

            if (slot.ReplacementPrefab == null)
            {
                slot.PartNumber = null;
                slot.PartDescription = null;
                return;
            }

            LDrawPart prefabPart = slot.ReplacementPrefab.GetComponent<LDrawPart>();
            if (prefabPart == null || prefabPart.Submeshes == null)
                return;

            slot.PartNumber = prefabPart.PartNumber;
            slot.PartDescription = prefabPart.Description;

            for (int i = 0; i < prefabPart.Submeshes.Length; i++)
            {
                LDrawPart.SubmeshColor submesh = prefabPart.Submeshes[i];
                slot.SubmeshColors.Add(new ClothingSubmeshColorOverride
                {
                    SubmeshIndex = i,
                    Label = submesh.Label,
                    OriginalColorCode = submesh.OriginalColorCode,
                    ColorCode = submesh.CurrentColorCode,
                    TextureName = submesh.TextureName
                });
            }
        }

        private static void SyncSubmeshOverridesFromInstance(ClothingSlotDefinition slot, GameObject instance)
        {
            slot.SubmeshColors.Clear();

            LDrawPart part = instance != null ? instance.GetComponent<LDrawPart>() : null;
            if (part == null || part.Submeshes == null)
            {
                slot.PartNumber = null;
                slot.PartDescription = null;
                return;
            }

            slot.PartNumber = part.PartNumber;
            slot.PartDescription = part.Description;

            for (int i = 0; i < part.Submeshes.Length; i++)
            {
                LDrawPart.SubmeshColor submesh = part.Submeshes[i];
                slot.SubmeshColors.Add(new ClothingSubmeshColorOverride
                {
                    SubmeshIndex = i,
                    Label = submesh.Label,
                    OriginalColorCode = submesh.OriginalColorCode,
                    ColorCode = submesh.CurrentColorCode,
                    TextureName = submesh.TextureName
                });
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string name = path.Substring(slash + 1);
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private void DrawSubmeshColorList(string slotName, ClothingSlotDefinition slot)
        {
            if (slot.SubmeshColors == null || slot.SubmeshColors.Count == 0)
                return;

            bool isOpen = slotFoldouts.TryGetValue(slotName, out bool state) && state;
            isOpen = EditorGUILayout.Foldout(isOpen, "Colors", true);
            slotFoldouts[slotName] = isOpen;
            if (!isOpen)
                return;

            for (int i = 0; i < slot.SubmeshColors.Count; i++)
            {
                ClothingSubmeshColorOverride colorOverride = slot.SubmeshColors[i];
                EditorGUILayout.BeginHorizontal();

                Color swatch = Color.gray;
                string colorName = $"Code {colorOverride.ColorCode}";
                if (colorTable != null)
                {
                    LDrawColorEntry entry = colorTable.GetColor(colorOverride.ColorCode);
                    if (entry != null)
                    {
                        swatch = entry.Value;
                        colorName = $"{entry.Name} ({entry.Code})";
                    }
                }

                Rect swatchRect = GUILayoutUtility.GetRect(16f, 16f, GUILayout.Width(16f));
                EditorGUI.DrawRect(swatchRect, swatch);

                string label = string.IsNullOrWhiteSpace(colorOverride.Label)
                    ? $"Submesh {colorOverride.SubmeshIndex}"
                    : colorOverride.Label;
                EditorGUILayout.LabelField($"{label}: {colorName}");
                EditorGUILayout.EndHorizontal();
            }
        }
    }

    public class ClothingSlotPreviewWindow : EditorWindow
    {
        private static readonly string[] SlotNames =
        {
            "Head", "Hat", "Torso", "Hips", "Left Arm", "Right Arm",
            "Left Hand", "Right Hand", "Left Hand Item", "Right Hand Item",
            "Left Leg", "Right Leg"
        };

        private ClothingAsset clothingAsset;
        private int selectedSlotIndex;
        private Vector2 submeshScroll;

        private PreviewRenderUtility previewUtility;
        private GameObject previewInstance;
        private LDrawPart previewPart;
        private readonly List<Material> previewMaterials = new List<Material>();
        private Quaternion previewRotation = Quaternion.Euler(18f, -30f, 0f);
        private float previewDistance = 2.8f;
        private Vector2 lastDragMouse;
        private bool isDragging;

        private LDrawColorTable colorTable;
        private Dictionary<int, string> colorNameCache = new Dictionary<int, string>();

        public static void ShowWindow(ClothingAsset asset, string slotName)
        {
            ClothingSlotPreviewWindow window = GetWindow<ClothingSlotPreviewWindow>("Clothing Preview");
            window.minSize = new Vector2(520f, 420f);
            window.Initialize(asset, slotName);
            window.Show();
        }

        private void Initialize(ClothingAsset asset, string slotName)
        {
            clothingAsset = asset;
            selectedSlotIndex = Mathf.Max(0, System.Array.IndexOf(SlotNames, slotName));
            colorTable = AssetDatabase.LoadAssetAtPath<LDrawColorTable>("Assets/LDraw/Resources/LDrawColorTable.asset");
            RebuildPreview();
        }

        private void OnEnable()
        {
            previewUtility = new PreviewRenderUtility();
            previewUtility.cameraFieldOfView = 30f;
            previewUtility.lights[0].intensity = 1.2f;
            previewUtility.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
            previewUtility.lights[1].intensity = 1f;
        }

        private void OnDisable()
        {
            ClearPreview();
            if (previewUtility != null)
            {
                previewUtility.Cleanup();
                previewUtility = null;
            }
        }

        private void OnGUI()
        {
            if (clothingAsset == null)
            {
                EditorGUILayout.HelpBox("Select a Clothing asset first.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            int newIndex = EditorGUILayout.Popup("Slot", selectedSlotIndex, SlotNames);
            if (newIndex != selectedSlotIndex)
            {
                selectedSlotIndex = newIndex;
                RebuildPreview();
            }

            if (GUILayout.Button("Reload", GUILayout.Width(70f)))
            {
                RebuildPreview();
            }
            EditorGUILayout.EndHorizontal();

            DrawPreviewArea();

            EditorGUILayout.Space(6f);
            DrawSubmeshControls();
        }

        private void DrawPreviewArea()
        {
            Rect previewRect = GUILayoutUtility.GetRect(10, 260, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, new Color(0.14f, 0.14f, 0.14f));

            HandlePreviewInput(previewRect);

            if (previewUtility == null || previewInstance == null)
                return;

            Bounds bounds = GetPreviewBounds();
            Vector3 center = bounds.center;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.2f);

            Vector3 camOffset = previewRotation * (Vector3.back * (previewDistance + radius));
            Vector3 camPos = center + camOffset;

            previewUtility.BeginPreview(previewRect, GUIStyle.none);
            previewUtility.camera.transform.position = camPos;
            previewUtility.camera.transform.LookAt(center);
            previewUtility.camera.nearClipPlane = 0.01f;
            previewUtility.camera.farClipPlane = 100f;

            // Rotate lighting rig 180 degrees around the preview center.
            previewUtility.lights[0].transform.position = center + new Vector3(-1f, 2f, 1f);
            previewUtility.lights[1].transform.position = center + new Vector3(1f, 1f, -1f);

            previewUtility.Render();
            Texture previewTexture = previewUtility.EndPreview();
            GUI.DrawTexture(previewRect, previewTexture, ScaleMode.StretchToFill, false);
        }

        private void DrawSubmeshControls()
        {
            if (previewPart == null || previewPart.Submeshes == null || previewPart.Submeshes.Length == 0)
            {
                EditorGUILayout.HelpBox("No preview mesh loaded for this slot.", MessageType.Info);
                return;
            }

            ClothingSlotDefinition slot = clothingAsset.GetSlotByName(SlotNames[selectedSlotIndex]);
            if (slot == null)
                return;

            EditorGUILayout.LabelField("Submesh Colors", EditorStyles.boldLabel);
            submeshScroll = EditorGUILayout.BeginScrollView(submeshScroll);

            for (int i = 0; i < previewPart.Submeshes.Length; i++)
            {
                DrawSubmeshRow(slot, i, previewPart.Submeshes[i]);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSubmeshRow(ClothingSlotDefinition slot, int submeshIndex, LDrawPart.SubmeshColor previewSubmesh)
        {
            ClothingSubmeshColorOverride colorOverride = GetOrCreateOverride(slot, submeshIndex, previewSubmesh);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            Color swatch = GetColor(colorOverride.ColorCode, out string colorName);
            Rect swatchRect = GUILayoutUtility.GetRect(20f, 20f, GUILayout.Width(20f));
            EditorGUI.DrawRect(swatchRect, swatch);

            string label = string.IsNullOrWhiteSpace(colorOverride.Label) ? $"Submesh {submeshIndex}" : colorOverride.Label;
            EditorGUILayout.LabelField($"{label} [{colorName}]", GUILayout.MinWidth(150f));

            int newCode = EditorGUILayout.IntField(colorOverride.ColorCode, GUILayout.Width(56f));
            if (newCode != colorOverride.ColorCode)
            {
                SetSubmeshColor(slot, submeshIndex, newCode);
            }

            Rect pickRect = GUILayoutUtility.GetRect(new GUIContent("Pick"), GUI.skin.button, GUILayout.Width(45f));
            if (GUI.Button(pickRect, "Pick"))
            {
                ShowColorMenu(pickRect, slot, submeshIndex);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ShowColorMenu(Rect buttonRect, ClothingSlotDefinition slot, int submeshIndex)
        {
            if (colorTable == null)
                return;

            PopupWindow.Show(buttonRect, new ClothingColorPickerPopup(
                colorTable,
                selectedCode => SetSubmeshColor(slot, submeshIndex, selectedCode)));
        }

        private void SetSubmeshColor(ClothingSlotDefinition slot, int submeshIndex, int colorCode)
        {
            Undo.RecordObject(clothingAsset, "Set Clothing Submesh Color");
            ClothingSubmeshColorOverride colorOverride = GetOrCreateOverride(slot, submeshIndex, previewPart.Submeshes[submeshIndex]);
            colorOverride.ColorCode = colorCode;

            EditorUtility.SetDirty(clothingAsset);

            if (previewPart != null && submeshIndex >= 0 && submeshIndex < previewPart.Submeshes.Length)
            {
                previewPart.Submeshes[submeshIndex].CurrentColorCode = colorCode;
                UpdatePreviewRendererMaterials(previewPart);
            }

            Repaint();
        }

        private ClothingSubmeshColorOverride GetOrCreateOverride(
            ClothingSlotDefinition slot, int submeshIndex, LDrawPart.SubmeshColor previewSubmesh)
        {
            for (int i = 0; i < slot.SubmeshColors.Count; i++)
            {
                ClothingSubmeshColorOverride existing = slot.SubmeshColors[i];
                if (existing.SubmeshIndex == submeshIndex)
                    return existing;
            }

            ClothingSubmeshColorOverride created = new ClothingSubmeshColorOverride
            {
                SubmeshIndex = submeshIndex,
                Label = previewSubmesh.Label,
                OriginalColorCode = previewSubmesh.OriginalColorCode,
                ColorCode = previewSubmesh.CurrentColorCode,
                TextureName = previewSubmesh.TextureName
            };
            slot.SubmeshColors.Add(created);
            EditorUtility.SetDirty(clothingAsset);
            return created;
        }

        private void RebuildPreview()
        {
            ClearPreview();

            if (clothingAsset == null || previewUtility == null)
                return;

            ClothingSlotDefinition slot = clothingAsset.GetSlotByName(SlotNames[selectedSlotIndex]);
            if (slot == null || slot.ReplacementPrefab == null)
                return;

            previewInstance = Object.Instantiate(slot.ReplacementPrefab);
            previewInstance.hideFlags = HideFlags.HideAndDontSave;
            previewInstance.transform.position = Vector3.zero;
            previewInstance.transform.rotation = Quaternion.identity;
            previewInstance.transform.localScale = Vector3.one;

            previewPart = previewInstance.GetComponent<LDrawPart>();
            if (previewPart != null)
            {
                for (int i = 0; i < slot.SubmeshColors.Count; i++)
                {
                    ClothingSubmeshColorOverride c = slot.SubmeshColors[i];
                    if (c.SubmeshIndex < 0 || c.SubmeshIndex >= previewPart.Submeshes.Length)
                        continue;
                    previewPart.Submeshes[c.SubmeshIndex].CurrentColorCode = c.ColorCode;
                }
                UpdatePreviewRendererMaterials(previewPart);
            }

            previewUtility.AddSingleGO(previewInstance);
        }

        private void ClearPreview()
        {
            if (previewInstance != null)
            {
                DestroyImmediate(previewInstance);
                previewInstance = null;
            }

            for (int i = 0; i < previewMaterials.Count; i++)
            {
                if (previewMaterials[i] != null)
                    DestroyImmediate(previewMaterials[i]);
            }
            previewMaterials.Clear();

            previewPart = null;
        }

        private Bounds GetPreviewBounds()
        {
            if (previewInstance == null)
                return new Bounds(Vector3.zero, Vector3.one);

            Renderer[] renderers = previewInstance.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                combined.Encapsulate(renderers[i].bounds);

            return combined;
        }

        private void HandlePreviewInput(Rect rect)
        {
            Event evt = Event.current;
            if (!rect.Contains(evt.mousePosition))
                return;

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                isDragging = true;
                lastDragMouse = evt.mousePosition;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && isDragging)
            {
                Vector2 delta = evt.mousePosition - lastDragMouse;
                lastDragMouse = evt.mousePosition;
                previewRotation = Quaternion.Euler(
                    previewRotation.eulerAngles.x - delta.y * 0.3f,
                    previewRotation.eulerAngles.y + delta.x * 0.4f,
                    0f);
                Repaint();
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                isDragging = false;
            }
            else if (evt.type == EventType.ScrollWheel)
            {
                previewDistance = Mathf.Clamp(previewDistance + evt.delta.y * 0.05f, 0.5f, 8f);
                Repaint();
                evt.Use();
            }
        }

        private Color GetColor(int colorCode, out string colorName)
        {
            colorName = $"Code {colorCode}";

            if (colorTable == null)
                return Color.gray;

            if (!colorNameCache.TryGetValue(colorCode, out string cachedName))
            {
                LDrawColorEntry entry = colorTable.GetColor(colorCode);
                if (entry != null)
                {
                    colorNameCache[colorCode] = entry.Name;
                    cachedName = entry.Name;
                }
            }

            if (!string.IsNullOrWhiteSpace(cachedName))
                colorName = cachedName + $" ({colorCode})";

            LDrawColorEntry colorEntry = colorTable.GetColor(colorCode);
            return colorEntry != null ? colorEntry.Value : Color.gray;
        }

        private void UpdatePreviewRendererMaterials(LDrawPart part)
        {
            MeshRenderer renderer = part != null ? part.GetComponent<MeshRenderer>() : null;
            if (renderer == null || part.Submeshes == null)
                return;

            for (int i = 0; i < previewMaterials.Count; i++)
            {
                if (previewMaterials[i] != null)
                    DestroyImmediate(previewMaterials[i]);
            }
            previewMaterials.Clear();

            Material[] mats = new Material[part.Submeshes.Length];
            for (int i = 0; i < part.Submeshes.Length; i++)
            {
                int code = part.Submeshes[i].CurrentColorCode;
                Color color = Color.gray;
                if (colorTable != null)
                {
                    LDrawColorEntry entry = colorTable.GetColor(code);
                    if (entry != null)
                        color = entry.Value;
                }

                Material mat = CreatePreviewMaterial(color);
                mats[i] = mat;
                previewMaterials.Add(mat);
            }

            renderer.sharedMaterials = mats;
        }

        private Material CreatePreviewMaterial(Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Diffuse");

            Material mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", 0.2f);

            return mat;
        }
    }

    public class ClothingColorPickerPopup : PopupWindowContent
    {
        private readonly LDrawColorTable colorTable;
        private readonly System.Action<int> onSelected;
        private Vector2 scrollPos;
        private string search = "";

        public ClothingColorPickerPopup(LDrawColorTable colorTable, System.Action<int> onSelected)
        {
            this.colorTable = colorTable;
            this.onSelected = onSelected;
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(320f, 360f);
        }

        public override void OnGUI(Rect rect)
        {
            if (colorTable == null || colorTable.Colors == null)
            {
                EditorGUILayout.HelpBox("Color table not found.", MessageType.Warning);
                return;
            }

            search = EditorGUILayout.TextField("Search", search);
            string term = string.IsNullOrWhiteSpace(search) ? null : search.ToLowerInvariant();

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            for (int i = 0; i < colorTable.Colors.Count; i++)
            {
                LDrawColorEntry entry = colorTable.Colors[i];
                if (term != null &&
                    !entry.Name.ToLowerInvariant().Contains(term) &&
                    !entry.Code.ToString().Contains(term))
                {
                    continue;
                }

                Rect rowRect = EditorGUILayout.GetControlRect(false, 20f);
                Rect swatchRect = new Rect(rowRect.x, rowRect.y + 2f, 16f, 16f);
                EditorGUI.DrawRect(swatchRect, entry.Value);
                Rect buttonRect = new Rect(rowRect.x + 22f, rowRect.y, rowRect.width - 22f, rowRect.height);

                if (GUI.Button(buttonRect, $"{entry.Name} ({entry.Code})", EditorStyles.miniButton))
                {
                    onSelected?.Invoke(entry.Code);
                    editorWindow.Close();
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }

    public class ClothingOutfitPreviewWindow : EditorWindow
    {
        private static readonly (string slotName, Vector3 position, Vector3 rotation)[] SlotLayout =
        {
            ("Head",      new Vector3(0f, 1.55f, 0f), Vector3.zero),
            ("Hat",       new Vector3(0f, 1.78f, 0f), Vector3.zero),
            ("Torso",     new Vector3(0f, 1.20f, 0f), Vector3.zero),
            ("Hips",      new Vector3(0f, 0.92f, 0f), Vector3.zero),
            ("Left Arm",  new Vector3(-0.42f, 1.22f, 0f), new Vector3(0f, 0f, 8f)),
            ("Right Arm", new Vector3(0.42f, 1.22f, 0f), new Vector3(0f, 0f, -8f)),
            ("Left Hand", new Vector3(-0.48f, 0.90f, 0f), Vector3.zero),
            ("Right Hand",new Vector3(0.48f, 0.90f, 0f), Vector3.zero),
            ("Left Hand Item", new Vector3(-0.62f, 0.92f, 0.05f), Vector3.zero),
            ("Right Hand Item",new Vector3(0.62f, 0.92f, 0.05f), Vector3.zero),
            ("Left Leg",  new Vector3(-0.16f, 0.45f, 0f), Vector3.zero),
            ("Right Leg", new Vector3(0.16f, 0.45f, 0f), Vector3.zero)
        };

        private ClothingAsset clothingAsset;
        private GameObject previewCharacterPrefab;
        private PreviewRenderUtility previewUtility;
        private GameObject previewRoot;
        private readonly List<Material> previewMaterials = new List<Material>();
        private Quaternion previewRotation = Quaternion.Euler(15f, -25f, 0f);
        private float previewDistance = 3.2f;
        private float lightYaw = 180f;
        private Vector2 lastDragMouse;
        private bool isDragging;
        private LDrawColorTable colorTable;

        public static void ShowWindow(ClothingAsset asset)
        {
            ClothingOutfitPreviewWindow window = GetWindow<ClothingOutfitPreviewWindow>("Clothing Outfit Preview");
            window.minSize = new Vector2(560f, 420f);
            window.Initialize(asset);
            window.Show();
        }

        private void Initialize(ClothingAsset asset)
        {
            clothingAsset = asset;
            colorTable = AssetDatabase.LoadAssetAtPath<LDrawColorTable>("Assets/LDraw/Resources/LDrawColorTable.asset");
            previewCharacterPrefab = FindDefaultCharacterPrefab();
            RebuildPreview();
        }

        private void OnEnable()
        {
            previewUtility = new PreviewRenderUtility();
            previewUtility.cameraFieldOfView = 30f;
            previewUtility.lights[0].intensity = 1.2f;
            previewUtility.lights[1].intensity = 1f;
        }

        private void OnDisable()
        {
            ClearPreview();
            if (previewUtility != null)
            {
                previewUtility.Cleanup();
                previewUtility = null;
            }
        }

        private void OnGUI()
        {
            if (clothingAsset == null)
            {
                EditorGUILayout.HelpBox("No clothing asset selected.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload", GUILayout.Width(90f)))
            {
                RebuildPreview();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(clothingAsset.name, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            GameObject updatedPreviewCharacter = (GameObject)EditorGUILayout.ObjectField(
                "Preview Character", previewCharacterPrefab, typeof(GameObject), false);
            if (updatedPreviewCharacter != previewCharacterPrefab)
            {
                previewCharacterPrefab = updatedPreviewCharacter;
                RebuildPreview();
            }

            lightYaw = EditorGUILayout.Slider("Light Yaw", lightYaw, 0f, 360f);

            Rect previewRect = GUILayoutUtility.GetRect(10, 360, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(previewRect, new Color(0.13f, 0.13f, 0.13f));
            HandlePreviewInput(previewRect);

            if (previewUtility == null || previewRoot == null)
                return;

            Bounds bounds = GetPreviewBounds();
            Vector3 center = bounds.center;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);

            Vector3 camPos = center + (previewRotation * (Vector3.back * (previewDistance + radius)));

            previewUtility.BeginPreview(previewRect, GUIStyle.none);
            previewUtility.camera.transform.position = camPos;
            previewUtility.camera.transform.LookAt(center);
            previewUtility.camera.nearClipPlane = 0.01f;
            previewUtility.camera.farClipPlane = 100f;

            Quaternion lightRotation = Quaternion.Euler(0f, lightYaw, 0f);
            previewUtility.lights[0].transform.rotation = lightRotation * Quaternion.Euler(35f, 35f, 0f);
            previewUtility.lights[1].transform.rotation = lightRotation * Quaternion.Euler(340f, 210f, 0f);

            previewUtility.Render();
            GUI.DrawTexture(previewRect, previewUtility.EndPreview(), ScaleMode.StretchToFill, false);
        }

        private void RebuildPreview()
        {
            ClearPreview();
            if (clothingAsset == null || previewUtility == null)
                return;

            previewRoot = new GameObject("__ClothingOutfitPreviewRoot");
            previewRoot.hideFlags = HideFlags.HideAndDontSave;

            bool builtFromCharacter = TryBuildFromCharacterPrefab();
            if (!builtFromCharacter)
                BuildFallbackSlotLayout();

            previewUtility.AddSingleGO(previewRoot);
        }

        private bool TryBuildFromCharacterPrefab()
        {
            if (previewCharacterPrefab == null || previewRoot == null)
                return false;

            GameObject characterInstance = Object.Instantiate(previewCharacterPrefab, previewRoot.transform);
            if (characterInstance == null)
                return false;

            characterInstance.hideFlags = HideFlags.HideAndDontSave;
            characterInstance.transform.localPosition = Vector3.zero;
            characterInstance.transform.localRotation = Quaternion.identity;
            characterInstance.transform.localScale = Vector3.one;

            CharacterCustomizer customizer = characterInstance.GetComponentInChildren<CharacterCustomizer>();
            if (customizer == null)
            {
                DestroyImmediate(characterInstance);
                return false;
            }

            customizer.SetBaseClothing(clothingAsset);
            customizer.ApplyBaseClothing();

            NormalizeCharacterRendererMaterials(characterInstance);

            LDrawPart[] ldrawParts = characterInstance.GetComponentsInChildren<LDrawPart>(true);
            for (int i = 0; i < ldrawParts.Length; i++)
                UpdatePreviewRendererMaterials(ldrawParts[i]);

            return true;
        }

        private void NormalizeCharacterRendererMaterials(GameObject characterRoot)
        {
            if (characterRoot == null)
                return;

            Renderer[] renderers = characterRoot.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null)
                    continue;

                Material[] sourceMats = renderer.sharedMaterials;
                if (sourceMats == null || sourceMats.Length == 0)
                    continue;

                Material[] previewMats = new Material[sourceMats.Length];
                for (int m = 0; m < sourceMats.Length; m++)
                {
                    Material src = sourceMats[m];
                    Color srcColor = Color.gray;
                    Texture srcTexture = null;

                    if (src != null)
                    {
                        if (src.HasProperty("_BaseColor"))
                            srcColor = src.GetColor("_BaseColor");
                        else if (src.HasProperty("_Color"))
                            srcColor = src.GetColor("_Color");

                        if (src.HasProperty("_BaseMap"))
                            srcTexture = src.GetTexture("_BaseMap");
                        else if (src.HasProperty("_MainTex"))
                            srcTexture = src.GetTexture("_MainTex");
                    }

                    Material previewMat = CreatePreviewMaterial(srcColor);
                    if (srcTexture != null)
                    {
                        if (previewMat.HasProperty("_BaseMap"))
                            previewMat.SetTexture("_BaseMap", srcTexture);
                        if (previewMat.HasProperty("_MainTex"))
                            previewMat.SetTexture("_MainTex", srcTexture);
                    }

                    previewMats[m] = previewMat;
                    previewMaterials.Add(previewMat);
                }

                renderer.sharedMaterials = previewMats;
            }
        }

        private void BuildFallbackSlotLayout()
        {
            for (int i = 0; i < SlotLayout.Length; i++)
            {
                (string slotName, Vector3 localPos, Vector3 localRot) = SlotLayout[i];
                ClothingSlotDefinition slot = clothingAsset.GetSlotByName(slotName);
                if (slot == null || slot.ReplacementPrefab == null)
                    continue;

                GameObject slotObject = Object.Instantiate(slot.ReplacementPrefab, previewRoot.transform);
                slotObject.hideFlags = HideFlags.HideAndDontSave;
                slotObject.transform.localPosition = localPos;
                slotObject.transform.localRotation = Quaternion.Euler(localRot);
                slotObject.transform.localScale = Vector3.one;

                LDrawPart part = slotObject.GetComponent<LDrawPart>();
                if (part != null && part.Submeshes != null)
                {
                    for (int c = 0; c < slot.SubmeshColors.Count; c++)
                    {
                        ClothingSubmeshColorOverride colorOverride = slot.SubmeshColors[c];
                        if (colorOverride.SubmeshIndex < 0 || colorOverride.SubmeshIndex >= part.Submeshes.Length)
                            continue;
                        part.Submeshes[colorOverride.SubmeshIndex].CurrentColorCode = colorOverride.ColorCode;
                    }
                    UpdatePreviewRendererMaterials(part);
                }
            }
        }

        private void ClearPreview()
        {
            if (previewRoot != null)
            {
                DestroyImmediate(previewRoot);
                previewRoot = null;
            }

            for (int i = 0; i < previewMaterials.Count; i++)
            {
                if (previewMaterials[i] != null)
                    DestroyImmediate(previewMaterials[i]);
            }
            previewMaterials.Clear();
        }

        private Bounds GetPreviewBounds()
        {
            if (previewRoot == null)
                return new Bounds(Vector3.zero, Vector3.one);

            Renderer[] renderers = previewRoot.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                combined.Encapsulate(renderers[i].bounds);
            return combined;
        }

        private void HandlePreviewInput(Rect rect)
        {
            Event evt = Event.current;
            if (!rect.Contains(evt.mousePosition))
                return;

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                isDragging = true;
                lastDragMouse = evt.mousePosition;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && isDragging)
            {
                Vector2 delta = evt.mousePosition - lastDragMouse;
                lastDragMouse = evt.mousePosition;
                previewRotation = Quaternion.Euler(
                    previewRotation.eulerAngles.x - delta.y * 0.3f,
                    previewRotation.eulerAngles.y + delta.x * 0.4f,
                    0f);
                Repaint();
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                isDragging = false;
            }
            else if (evt.type == EventType.ScrollWheel)
            {
                previewDistance = Mathf.Clamp(previewDistance + evt.delta.y * 0.06f, 1f, 10f);
                Repaint();
                evt.Use();
            }
        }

        private void UpdatePreviewRendererMaterials(LDrawPart part)
        {
            MeshRenderer renderer = part != null ? part.GetComponent<MeshRenderer>() : null;
            if (renderer == null || part.Submeshes == null)
                return;

            Material[] mats = new Material[part.Submeshes.Length];
            for (int i = 0; i < part.Submeshes.Length; i++)
            {
                int code = part.Submeshes[i].CurrentColorCode;
                Color color = Color.gray;
                if (colorTable != null)
                {
                    LDrawColorEntry entry = colorTable.GetColor(code);
                    if (entry != null)
                        color = entry.Value;
                }

                Material mat = CreatePreviewMaterial(color);
                mats[i] = mat;
                previewMaterials.Add(mat);
            }

            renderer.sharedMaterials = mats;
        }

        private Material CreatePreviewMaterial(Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Diffuse");

            Material mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", 0.2f);
            return mat;
        }

        private GameObject FindDefaultCharacterPrefab()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                if (prefab.GetComponentInChildren<CharacterCustomizer>() != null)
                    return prefab;
            }

            return null;
        }
    }
}
