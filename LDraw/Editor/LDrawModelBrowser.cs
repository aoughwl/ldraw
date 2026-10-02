using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using LDraw;

namespace LDraw.Editor
{
    public class LDrawModelBrowser : EditorWindow
    {
        [SerializeField] Vector2 _scrollPos;
        [SerializeField] string _searchText = "";
        [SerializeField] int _selectedColorCode = -1; // -1 = Default: use original part colors from .dat
        [SerializeField] int _currentPage;
        const int PageSize = 40;

        // Building system options
        [SerializeField] bool _addBuildingComponents = true;
        [SerializeField] bool _enableGravity = true;
        [SerializeField, Range(1f, 200f)] float _studlessLodTransitionHeight = 80f;

        [SerializeField] LDrawPartCatalog _catalog;
        [SerializeField] LDrawColorTable _colorTable;
        [SerializeField] LDrawMaterialLibrary _matLib;
        [SerializeField] List<LDrawPartCatalog.PartEntry> _filteredParts;

        // Preview cache - not serialized (regenerates on demand)
        Dictionary<string, Texture2D> _previewCache = new Dictionary<string, Texture2D>();

        // Selection mode (used by character customizer, brick placer, etc.)
        public static System.Action<GameObject> OnPartSelected;
        private static bool _isSelectionMode = false;
        private static string _selectionButtonText = "Select";
        private static bool _selectionAddBuildingComponents = false;
        private static string _selectionHelpText = "";

        public static void SetSelectionMode(bool enabled, string buttonText = "Select",
            bool addBuildingComponents = false, string helpText = "")
        {
            _isSelectionMode = enabled;
            _selectionButtonText = buttonText;
            _selectionAddBuildingComponents = addBuildingComponents;
            _selectionHelpText = helpText;
        }

        [MenuItem("Window/LDraw/Parts")]
        public static void ShowWindow()
        {
            var window = GetWindow<LDrawModelBrowser>("LDraw Parts");
            window.minSize = new Vector2(500, 400);
        }

        void OnEnable()
        {
            LoadResources();
            if (_studlessLodTransitionHeight <= 0f)
                _studlessLodTransitionHeight = 80f;

            // Reapply filter if search text exists but filtered list is empty
            if (!string.IsNullOrWhiteSpace(_searchText) && _filteredParts == null && _catalog != null)
            {
                FilterParts();
            }
        }

        void OnFocus()
        {
            LoadResources();
        }

        void LoadResources()
        {
            // Only reload if null (after deserialization)
            if (_catalog == null)
                _catalog = AssetDatabase.LoadAssetAtPath<LDrawPartCatalog>(
                    "Assets/LDraw/Resources/LDrawPartCatalog.asset");

            if (_colorTable == null)
                _colorTable = AssetDatabase.LoadAssetAtPath<LDrawColorTable>(
                    "Assets/LDraw/Resources/LDrawColorTable.asset");

            if (_matLib == null)
                _matLib = AssetDatabase.LoadAssetAtPath<LDrawMaterialLibrary>(
                    "Assets/LDraw/Resources/LDrawMaterialLibrary.asset");
        }

        void OnGUI()
        {
            if (_catalog == null || _catalog.Parts.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No converted parts found. Use Window > LDraw > Importer to convert parts first.",
                    MessageType.Info);
                if (GUILayout.Button("Open Importer"))
                    LDrawImporterWindow.ShowWindow();
                return;
            }

            // Show selection mode indicator
            if (_isSelectionMode && !string.IsNullOrEmpty(_selectionHelpText))
            {
                EditorGUILayout.HelpBox(_selectionHelpText, MessageType.Info);
            }

            DrawToolbar();
            EditorGUILayout.Space(4);
            DrawPartGrid();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Search
            EditorGUILayout.LabelField("Search:", GUILayout.Width(48));
            string newSearch = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField,
                GUILayout.MinWidth(120));
            if (newSearch != _searchText)
            {
                _searchText = newSearch;
                _currentPage = 0;
                FilterParts();
            }

            GUILayout.FlexibleSpace();

            int convertedCount = _catalog != null ? _catalog.Parts.Count : 0;
            GUI.enabled = convertedCount > 0;
            if (GUILayout.Button(new GUIContent($"Rebuild All ({convertedCount})", "Delete and re-convert all converted parts"), EditorStyles.toolbarButton, GUILayout.Width(120)))
            {
                if (EditorUtility.DisplayDialog("Rebuild All Converted Parts",
                    $"This will delete and re-convert {convertedCount} parts.\nConnection point data will be regenerated.\n\nThis may take a while.",
                    "Rebuild All", "Cancel"))
                {
                    LDrawConversionPipeline.RebuildAllParts();
                    _previewCache.Clear();
                    LoadResources();
                }
            }
            GUI.enabled = true;

            // Building system toggles
            _addBuildingComponents = GUILayout.Toggle(_addBuildingComponents,
                new GUIContent("Building", "Add BlockBrick, Rigidbody, and Collider components"),
                EditorStyles.toolbarButton, GUILayout.Width(60));

            if (_addBuildingComponents)
            {
                _enableGravity = GUILayout.Toggle(_enableGravity,
                    new GUIContent("Gravity", "Enable physics gravity"),
                    EditorStyles.toolbarButton, GUILayout.Width(50));
            }

            // Color picker
            EditorGUILayout.LabelField("Color:", GUILayout.Width(40));
            DrawColorButton();

            EditorGUILayout.EndHorizontal();
        }

        void DrawColorButton()
        {
            string colorName;
            Color swatchColor;

            if (_selectedColorCode == -1)
            {
                colorName = "Default Color";
                swatchColor = Color.gray;
            }
            else
            {
                colorName = "Unknown";
                swatchColor = Color.gray;

                if (_colorTable != null)
                {
                    var entry = _colorTable.GetColor(_selectedColorCode);
                    if (entry != null)
                    {
                        colorName = entry.Name;
                        swatchColor = entry.Value;
                    }
                }
            }

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = swatchColor;
            if (GUILayout.Button(_selectedColorCode == -1 ? colorName : $"{colorName} ({_selectedColorCode})",
                GUILayout.Width(140)))
            {
                ShowColorPopup();
            }
            GUI.backgroundColor = prevBg;
        }

        void ShowColorPopup()
        {
            if (_colorTable == null) return;

            var menu = new GenericMenu();

            // Default Color option — uses the part's own colors as defined in the .dat file
            menu.AddItem(new GUIContent("Default Color"), _selectedColorCode == -1,
                () => _selectedColorCode = -1);
            menu.AddSeparator("");

            // Add common solid colors first
            int[] commonCodes = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
                                  19, 22, 25, 26, 27, 28, 29, 46, 47, 70, 71, 72 };

            foreach (int code in commonCodes)
            {
                var entry = _colorTable.GetColor(code);
                if (entry == null) continue;
                int capturedCode = code;
                menu.AddItem(new GUIContent($"{entry.Name} ({code})"), _selectedColorCode == code,
                    () => _selectedColorCode = capturedCode);
            }

            menu.AddSeparator("");

            // All solid colors
            foreach (var entry in _colorTable.Colors)
            {
                if (entry.Finish != LDrawFinishType.Solid) continue;
                int capturedCode = entry.Code;
                menu.AddItem(new GUIContent($"Solid/{entry.Name} ({entry.Code})"),
                    _selectedColorCode == entry.Code,
                    () => _selectedColorCode = capturedCode);
            }

            // Transparent
            foreach (var entry in _colorTable.Colors)
            {
                if (entry.Finish != LDrawFinishType.Transparent) continue;
                int capturedCode = entry.Code;
                menu.AddItem(new GUIContent($"Transparent/{entry.Name} ({entry.Code})"),
                    _selectedColorCode == entry.Code,
                    () => _selectedColorCode = capturedCode);
            }

            // Special finishes
            foreach (var entry in _colorTable.Colors)
            {
                if (entry.Finish == LDrawFinishType.Solid || entry.Finish == LDrawFinishType.Transparent)
                    continue;
                int capturedCode = entry.Code;
                menu.AddItem(new GUIContent($"Special/{entry.Name} ({entry.Code})"),
                    _selectedColorCode == entry.Code,
                    () => _selectedColorCode = capturedCode);
            }

            menu.ShowAsContext();
        }

        void DrawPartGrid()
        {
            var displayList = _filteredParts ?? _catalog.Parts;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)displayList.Count / PageSize));

            // Pagination
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{displayList.Count} parts", GUILayout.Width(80));

            GUI.enabled = _currentPage > 0;
            if (GUILayout.Button("<", GUILayout.Width(30)))
                _currentPage--;
            GUI.enabled = true;

            EditorGUILayout.LabelField($"{_currentPage + 1} / {totalPages}",
                EditorStyles.centeredGreyMiniLabel, GUILayout.Width(60));

            GUI.enabled = _currentPage < totalPages - 1;
            if (GUILayout.Button(">", GUILayout.Width(30)))
                _currentPage++;
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Part grid
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            int pageStart = _currentPage * PageSize;
            int pageEnd = Mathf.Min(pageStart + PageSize, displayList.Count);

            // Calculate columns based on window width
            float itemWidth = 140;
            int columns = Mathf.Max(1, Mathf.FloorToInt(position.width / itemWidth));

            int col = 0;
            EditorGUILayout.BeginHorizontal();

            for (int i = pageStart; i < pageEnd; i++)
            {
                var part = displayList[i];
                DrawPartCard(part);

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
        }

        void DrawPartCard(LDrawPartCatalog.PartEntry part)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(130), GUILayout.Height(110));

            // Mesh preview
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(part.MeshAssetPath);
            if (mesh != null)
            {
                var preview = GetPreview(part.MeshAssetPath, mesh);
                if (preview != null)
                {
                    var previewRect = GUILayoutUtility.GetRect(120, 60, GUILayout.Width(120));
                    GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
                }
                else
                {
                    GUILayout.Space(60);
                }
            }
            else
            {
                GUILayout.Space(60);
            }

            // Part name
            EditorGUILayout.LabelField(part.PartNumber, EditorStyles.miniLabel);

            // Truncated description
            string desc = part.Description;
            if (desc.Length > 20) desc = desc.Substring(0, 17) + "...";
            EditorGUILayout.LabelField(desc, EditorStyles.miniLabel);

            // Spawn button (dynamic text based on mode)
            string buttonText = _isSelectionMode
                ? _selectionButtonText
                : "Spawn";

            if (GUILayout.Button(buttonText, GUILayout.Height(18)))
            {
                SpawnPart(part);
            }

            EditorGUILayout.EndVertical();
        }

        Texture2D GetPreview(string assetPath, Mesh mesh)
        {
            if (_previewCache.TryGetValue(assetPath, out var cached) && cached != null)
                return cached;

            var preview = AssetPreview.GetAssetPreview(mesh);
            if (preview != null)
                _previewCache[assetPath] = preview;

            // Request preview generation if not ready
            if (preview == null)
                AssetPreview.SetPreviewTextureCacheSize(256);

            return preview;
        }

        void SpawnPart(LDrawPartCatalog.PartEntry part)
        {
            var latestPart = (_catalog != null && !string.IsNullOrWhiteSpace(part.PartNumber))
                ? _catalog.GetPart(part.PartNumber) ?? part
                : part;

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(latestPart.MeshAssetPath);
            if (mesh == null)
            {
                Debug.LogError($"Mesh not found at {latestPart.MeshAssetPath}");
                return;
            }

            // Create the part GameObject
            var go = CreatePartGameObject(latestPart, mesh);

            // If in selection mode, invoke callback and return
            if (_isSelectionMode && OnPartSelected != null)
            {
                // Add building components if the caller requested them
                if (_selectionAddBuildingComponents)
                {
                    var mf = go.GetComponent<MeshFilter>();
                    AddBuildingSystemComponents(go, latestPart, mf);
                }

                OnPartSelected.Invoke(go);
                _isSelectionMode = false;
                OnPartSelected = null;

                // Only close the window if it's undocked (floating)
                if (!docked)
                {
                    Close();
                }
                return;
            }

            // Normal spawn mode: place in scene
            Vector3 spawnPos = Vector3.zero;
            if (SceneView.lastActiveSceneView != null)
            {
                var cam = SceneView.lastActiveSceneView.camera;
                spawnPos = cam.transform.position + cam.transform.forward * 3f;
            }

            go.transform.position = spawnPos;

            // Add building system components if enabled
            if (_addBuildingComponents)
            {
                var meshFilter = go.GetComponent<MeshFilter>();
                AddBuildingSystemComponents(go, latestPart, meshFilter);
            }

            Undo.RegisterCreatedObjectUndo(go, "Spawn LDraw Part");
            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        GameObject CreatePartGameObject(LDrawPartCatalog.PartEntry part, Mesh mesh)
        {
            var go = new GameObject($"LDraw_{part.PartNumber}");

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();

            // Setup LDrawPart component
            var ldrawPart = go.AddComponent<LDrawPart>();
            ldrawPart.PartNumber = part.PartNumber;
            ldrawPart.Description = part.Description;

            int submeshCount = part.SubmeshColorCodes != null ? part.SubmeshColorCodes.Length : 1;
            ldrawPart.Submeshes = new LDrawPart.SubmeshColor[submeshCount];

            for (int i = 0; i < submeshCount; i++)
            {
                int originalCode = part.SubmeshColorCodes != null ? part.SubmeshColorCodes[i] : 16;

                string textureName = (part.SubmeshTextureNames != null && i < part.SubmeshTextureNames.Length)
                    ? part.SubmeshTextureNames[i]
                    : null;

                // Determine assigned color based on user selection
                int assignedCode;
                if (!string.IsNullOrEmpty(textureName))
                {
                    // Textured submesh - always keep original color (texture contains color info)
                    assignedCode = originalCode;
                }
                else if (_selectedColorCode == -1)
                {
                    // "Default Color" (-1) - use exact color from .dat file (catalog)
                    assignedCode = originalCode;
                }
                else
                {
                    // User selected a specific color - override all non-textured submeshes
                    assignedCode = _selectedColorCode;
                }

                string label = $"Submesh {i}";
                if (_colorTable != null)
                {
                    var colorEntry = _colorTable.GetColor(originalCode);
                    if (colorEntry != null) label = colorEntry.Name;
                }

                if (!string.IsNullOrEmpty(textureName))
                    label += $" (Textured: {textureName})";

                ldrawPart.Submeshes[i] = new LDrawPart.SubmeshColor
                {
                    OriginalColorCode = originalCode,
                    CurrentColorCode = assignedCode,
                    TextureName = textureName,
                    Label = label
                };
            }

            // Apply materials (including textured materials)
            if (_matLib != null)
            {
                var mats = new Material[submeshCount];
                for (int i = 0; i < submeshCount; i++)
                {
                    var mat = _matLib.GetMaterial(
                        ldrawPart.Submeshes[i].CurrentColorCode,
                        ldrawPart.Submeshes[i].TextureName);
                    if (mat == null) mat = _matLib.GetMaterial(0);
                    mats[i] = mat;
                }
                meshRenderer.sharedMaterials = mats;
            }

            if (string.IsNullOrWhiteSpace(part.StudlessLodMeshAssetPath))
            {
                Debug.LogWarning($"Stud LOD skipped for {part.PartNumber}: no studless LOD mesh path in catalog entry.");
            }
            else
            {
                var studlessMesh = AssetDatabase.LoadAssetAtPath<Mesh>(part.StudlessLodMeshAssetPath);
                if (studlessMesh == null && !string.IsNullOrWhiteSpace(part.PartNumber))
                {
                    string fallbackPath = $"Assets/LDraw/GeneratedMeshes/parts/{part.PartNumber}_nostud.asset";
                    studlessMesh = AssetDatabase.LoadAssetAtPath<Mesh>(fallbackPath);
                }
                if (studlessMesh == null)
                {
                    Debug.LogWarning($"Stud LOD skipped for {part.PartNumber}: studless mesh asset missing at {part.StudlessLodMeshAssetPath}.");
                }
                else
                {
                    LDrawPartLodUtility.ConfigureStudlessLod(go, studlessMesh, meshRenderer.sharedMaterials,
                        _studlessLodTransitionHeight);
                }
            }

            return go;
        }

        void AddBuildingSystemComponents(GameObject go, LDrawPartCatalog.PartEntry part, MeshFilter meshFilter)
        {
            // Add collider based on mesh bounds
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                var bounds = meshFilter.sharedMesh.bounds;
                var boxCollider = go.AddComponent<BoxCollider>();
                boxCollider.center = bounds.center;
                boxCollider.size = bounds.size;
            }
            else
            {
                var boxCollider = go.AddComponent<BoxCollider>();
                boxCollider.size = Vector3.one;
            }

            // Add rigidbody
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = _enableGravity;
            rb.linearDamping = 1f;
            rb.angularDamping = 5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            if (!_enableGravity)
            {
                rb.isKinematic = true;
            }

            // Add BlockBrick component
            var blockBrick = go.AddComponent<BlockBrick>();
            blockBrick.enableGravity = _enableGravity;

            // Set connection points from parsed LDraw data
            if (part.Connections != null && part.Connections.Count > 0)
            {
                blockBrick.SetConnectionPointsFromData(
                    part.Connections.Types,
                    part.Connections.Positions,
                    part.Connections.Directions,
                    part.Connections.Depths);

                Debug.Log($"Added building components to {go.name}: {part.Connections.Count} connection points from LDraw data, Gravity: {_enableGravity}");
            }
            else
            {
                Debug.LogWarning($"No connection data for {go.name}. Rebuild converted parts to generate connection data.");
            }
        }

        void FilterParts()
        {
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                _filteredParts = null;
                return;
            }

            string search = _searchText.ToLowerInvariant();
            _filteredParts = _catalog.Parts.Where(p =>
                p.PartNumber.ToLowerInvariant().Contains(search) ||
                p.Description.ToLowerInvariant().Contains(search)
            ).ToList();
        }
    }
}














