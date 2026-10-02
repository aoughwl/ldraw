using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using LDraw;

namespace LDraw.Editor
{
    public class LDrawImporterWindow : EditorWindow
    {
        // Part index entry - MUST be Serializable for Unity to save it
        [System.Serializable]
        struct PartIndexEntry
        {
            public string FileName;
            public string PartNumber;
            public string Description;
            public string FullPath;
        }

        // State
        [SerializeField] Vector2 _scrollPos;
        [SerializeField] string _searchText = "";
        [SerializeField] int _currentPage;
        const int PageSize = 50;

        // Part index - SERIALIZED to persist between recompiles
        [SerializeField] List<PartIndexEntry> _partIndex;
        [SerializeField] List<PartIndexEntry> _filteredParts;
        [SerializeField] bool _indexBuilt;
        [SerializeField] bool _isBuilding;

        // Library status
        [SerializeField] bool _libraryExtracted;
        [SerializeField] LDrawLibraryExtractor.LibraryStats _libraryStats;

        // Selection
        [SerializeField] List<string> _selectedPartsList = new List<string>();
        HashSet<string> _selectedParts = new HashSet<string>();

        // Sections
        [SerializeField] bool _showLibrary = true;
        [SerializeField] bool _showColors = true;
        [SerializeField] bool _showBrowser = true;
        [SerializeField] bool _showBatch = true;

        [MenuItem("Window/LDraw/Importer")]
        public static void ShowWindow()
        {
            var window = GetWindow<LDrawImporterWindow>("LDraw Importer");
            window.minSize = new Vector2(400, 500);
        }

        void OnEnable()
        {
            RefreshLibraryStatus();

            // Rebuild HashSet from serialized list
            _selectedParts = new HashSet<string>(_selectedPartsList ?? new List<string>());

            // Reapply filter if search text exists but filtered list is null
            if (!string.IsNullOrWhiteSpace(_searchText) && _filteredParts == null && _partIndex != null && _partIndex.Count > 0)
            {
                FilterParts();
            }
        }

        void OnDisable()
        {
            // Save HashSet to serialized list
            _selectedPartsList = _selectedParts.ToList();
        }

        void RefreshLibraryStatus()
        {
            _libraryExtracted = LDrawLibraryExtractor.IsExtracted();
            if (_libraryExtracted)
                _libraryStats = LDrawLibraryExtractor.GetStats();
        }

        void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawLibrarySection();
            EditorGUILayout.Space(8);
            DrawColorSection();
            EditorGUILayout.Space(8);
            DrawBatchSection();
            EditorGUILayout.Space(8);
            DrawBrowserSection();

            EditorGUILayout.EndScrollView();
        }

        void DrawLibrarySection()
        {
            _showLibrary = EditorGUILayout.BeginFoldoutHeaderGroup(_showLibrary, "Library");
            if (!_showLibrary)
            {
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            EditorGUI.indentLevel++;

            if (_libraryExtracted)
            {
                EditorGUILayout.LabelField("Status", "Extracted");
                EditorGUILayout.LabelField("Path", LDrawLibraryExtractor.GetLDrawRoot());
                EditorGUILayout.LabelField("Parts", _libraryStats.PartCount.ToString());
                EditorGUILayout.LabelField("Subparts", _libraryStats.SubpartCount.ToString());
                EditorGUILayout.LabelField("Primitives", _libraryStats.PrimitiveCount.ToString());
                EditorGUILayout.LabelField("Color Config", _libraryStats.HasColorConfig ? "Found" : "Missing");
            }
            else
            {
                EditorGUILayout.HelpBox("LDraw library not extracted. Click Extract to unpack complete.zip.", MessageType.Info);
            }

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Extract Library from complete.zip"))
            {
                LDrawLibraryExtractor.Extract();
                RefreshLibraryStatus();
                _indexBuilt = false;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawColorSection()
        {
            _showColors = EditorGUILayout.BeginFoldoutHeaderGroup(_showColors, "Colors & Materials");
            if (!_showColors)
            {
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            EditorGUI.indentLevel++;

            // Show current status
            var colorTable = AssetDatabase.LoadAssetAtPath<LDrawColorTable>("Assets/LDraw/Resources/LDrawColorTable.asset");
            if (colorTable != null)
                EditorGUILayout.LabelField("Colors Loaded", colorTable.Colors.Count.ToString());
            else
                EditorGUILayout.LabelField("Colors Loaded", "None");

            var matLib = AssetDatabase.LoadAssetAtPath<LDrawMaterialLibrary>("Assets/LDraw/Resources/LDrawMaterialLibrary.asset");
            if (matLib != null)
                EditorGUILayout.LabelField("Materials", matLib.Materials.Count.ToString());
            else
                EditorGUILayout.LabelField("Materials", "None");

            // Texture import status
            int textureCount = LDrawTextureImporter.GetImportedTextureCount();
            if (textureCount >= 135)
                EditorGUILayout.HelpBox($"Textures imported: {textureCount} / 135", MessageType.Info);
            else if (textureCount > 0)
                EditorGUILayout.HelpBox($"Textures imported: {textureCount} / 135 (incomplete)", MessageType.Warning);
            else
                EditorGUILayout.HelpBox("No textures imported. Click 'Import Textures' to enable decorated parts.", MessageType.Info);

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Import Textures from LDraw Library", GUILayout.Height(40)))
            {
                if (!_libraryExtracted)
                {
                    EditorUtility.DisplayDialog("LDraw", "Extract the library first.", "OK");
                }
                else
                {
                    LDrawTextureImporter.ImportAllTextures();
                    Repaint();
                }
            }

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Generate Colors & Materials"))
            {
                if (!_libraryExtracted)
                {
                    EditorUtility.DisplayDialog("LDraw", "Extract the library first.", "OK");
                    return;
                }

                var table = LDrawConversionPipeline.GenerateColorTable();
                if (table != null)
                    LDrawConversionPipeline.GenerateMaterials(table);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawBatchSection()
        {
            _showBatch = EditorGUILayout.BeginFoldoutHeaderGroup(_showBatch, "Batch Actions");
            if (!_showBatch)
            {
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            EditorGUI.indentLevel++;

            if (GUILayout.Button("Convert Common Parts (~120 parts)"))
            {
                if (!_libraryExtracted)
                {
                    EditorUtility.DisplayDialog("LDraw", "Extract the library first.", "OK");
                    return;
                }

                ConvertCommonParts();
            }

            if (_selectedParts.Count > 0)
            {
                if (GUILayout.Button($"Convert Selected ({_selectedParts.Count} parts)"))
                {
                    ConvertSelectedParts();
                }
            }

            EditorGUILayout.Space(8);

            // Rebuild / Delete section
            var catalog = AssetDatabase.LoadAssetAtPath<LDrawPartCatalog>("Assets/LDraw/Resources/LDrawPartCatalog.asset");
            int convertedCount = catalog != null ? catalog.Parts.Count : 0;

            GUI.enabled = convertedCount > 0;
            if (GUILayout.Button($"Rebuild All Converted ({convertedCount} parts)"))
            {
                if (EditorUtility.DisplayDialog("Rebuild All Converted Parts",
                    $"This will delete and re-convert {convertedCount} parts.\nConnection point data will be regenerated.\n\nThis may take a while.",
                    "Rebuild All", "Cancel"))
                {
                    LDrawConversionPipeline.RebuildAllParts();
                }
            }

            if (GUILayout.Button($"Delete All Converted ({convertedCount} parts)"))
            {
                if (EditorUtility.DisplayDialog("Delete All Converted Parts",
                    $"This will delete {convertedCount} converted meshes, their textured materials, and catalog entries.\n\nThis cannot be undone.",
                    "Delete All", "Cancel"))
                {
                    LDrawConversionPipeline.DeleteAllParts();
                }
            }
            GUI.enabled = true;

            EditorGUI.indentLevel--;
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawBrowserSection()
        {
            _showBrowser = EditorGUILayout.BeginFoldoutHeaderGroup(_showBrowser, "Part Browser");
            if (!_showBrowser)
            {
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            if (!_libraryExtracted)
            {
                EditorGUILayout.HelpBox("Extract the library first to browse parts.", MessageType.Info);
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            if (!_indexBuilt && !_isBuilding)
            {
                if (GUILayout.Button("Build Part Index"))
                {
                    BuildPartIndex();
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            if (_partIndex == null)
            {
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            // Search bar
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            string newSearch = EditorGUILayout.TextField(_searchText);
            if (newSearch != _searchText)
            {
                _searchText = newSearch;
                _currentPage = 0;
                FilterParts();
            }
            EditorGUILayout.EndHorizontal();

            var displayList = _filteredParts ?? _partIndex;
            int totalPages = Mathf.CeilToInt((float)displayList.Count / PageSize);

            EditorGUILayout.LabelField($"Showing {displayList.Count} parts (page {_currentPage + 1}/{Mathf.Max(1, totalPages)})");

            // Pagination
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _currentPage > 0;
            if (GUILayout.Button("< Prev", GUILayout.Width(80)))
                _currentPage--;
            GUI.enabled = _currentPage < totalPages - 1;
            if (GUILayout.Button("Next >", GUILayout.Width(80)))
                _currentPage++;
            GUI.enabled = true;
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Select All on Page", GUILayout.Width(130)))
            {
                int start = _currentPage * PageSize;
                int end = Mathf.Min(start + PageSize, displayList.Count);
                for (int i = start; i < end; i++)
                    _selectedParts.Add(displayList[i].FullPath);
            }
            if (GUILayout.Button("Clear Selection", GUILayout.Width(110)))
                _selectedParts.Clear();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Part list
            int pageStart = _currentPage * PageSize;
            int pageEnd = Mathf.Min(pageStart + PageSize, displayList.Count);

            for (int i = pageStart; i < pageEnd; i++)
            {
                var part = displayList[i];
                bool isConverted = LDrawConversionPipeline.IsPartConverted(part.PartNumber);
                bool isSelected = _selectedParts.Contains(part.FullPath);

                EditorGUILayout.BeginHorizontal();

                // Selection toggle
                bool newSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(20));
                if (newSelected != isSelected)
                {
                    if (newSelected) _selectedParts.Add(part.FullPath);
                    else _selectedParts.Remove(part.FullPath);
                }

                // Part info
                string statusIcon = isConverted ? "[OK]" : "[--]";
                EditorGUILayout.LabelField($"{statusIcon} {part.PartNumber}", GUILayout.Width(100));
                EditorGUILayout.LabelField(part.Description);

                // Convert / Delete buttons
                if (isConverted)
                {
                    if (GUILayout.Button("Delete", GUILayout.Width(65)))
                    {
                        LDrawConversionPipeline.DeletePart(part.PartNumber);
                    }
                }
                else
                {
                    if (GUILayout.Button("Convert", GUILayout.Width(65)))
                    {
                        ConvertSinglePart(part.FullPath);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void BuildPartIndex()
        {
            _isBuilding = true;
            _partIndex = new List<PartIndexEntry>();

            string partsDir = Path.Combine(LDrawLibraryExtractor.GetLDrawRoot(), "parts");
            if (!Directory.Exists(partsDir))
            {
                _isBuilding = false;
                return;
            }

            var files = Directory.GetFiles(partsDir, "*.dat");

            for (int i = 0; i < files.Length; i++)
            {
                if (i % 500 == 0)
                {
                    EditorUtility.DisplayProgressBar("Building Part Index",
                        $"Scanning {i}/{files.Length}...",
                        (float)i / files.Length);
                }

                string file = files[i];
                string partNumber = Path.GetFileNameWithoutExtension(file);
                string description = LDrawParser.GetPartDescription(file);

                _partIndex.Add(new PartIndexEntry
                {
                    FileName = Path.GetFileName(file),
                    PartNumber = partNumber,
                    Description = description,
                    FullPath = file
                });
            }

            EditorUtility.ClearProgressBar();

            // Sort by part number
            _partIndex.Sort((a, b) => string.Compare(a.PartNumber, b.PartNumber, StringComparison.OrdinalIgnoreCase));

            _indexBuilt = true;
            _isBuilding = false;
            _filteredParts = null;

            Debug.Log($"Built part index with {_partIndex.Count} entries.");
        }

        void FilterParts()
        {
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                _filteredParts = null;
                return;
            }

            string search = _searchText.ToLowerInvariant();
            _filteredParts = _partIndex.Where(p =>
                p.PartNumber.ToLowerInvariant().Contains(search) ||
                p.Description.ToLowerInvariant().Contains(search)
            ).ToList();
        }

        void ConvertSinglePart(string partFilePath)
        {
            LDrawConversionPipeline.ConvertParts(new List<string> { partFilePath });
        }

        void ConvertSelectedParts()
        {
            if (_selectedParts.Count == 0) return;
            LDrawConversionPipeline.ConvertParts(_selectedParts.ToList());
            _selectedParts.Clear();
        }

        void ConvertCommonParts()
        {
            string partsDir = Path.Combine(LDrawLibraryExtractor.GetLDrawRoot(), "parts");
            var paths = new List<string>();

            foreach (var partNum in LDrawConversionPipeline.CommonParts)
            {
                string path = Path.Combine(partsDir, partNum + ".dat");
                if (File.Exists(path))
                    paths.Add(path);
                else
                    Debug.LogWarning($"Common part {partNum}.dat not found.");
            }

            if (paths.Count > 0)
                LDrawConversionPipeline.ConvertParts(paths);
        }
    }
}
