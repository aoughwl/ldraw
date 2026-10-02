using System;
using System.Collections.Generic;
using UnityEngine;

namespace LDraw
{
    [CreateAssetMenu(fileName = "LDrawPartCatalog", menuName = "LDraw/Part Catalog")]
    public class LDrawPartCatalog : ScriptableObject
    {
        [Serializable]
        public class ConnectionData
        {
            public byte[] Types;       // LDrawConnectionType values (0=Stud, 1=AntiStud, 2=TechnicHole, etc.)
            public float[] Positions;  // Flattened xyz in Unity space (length = Count * 3)
            public float[] Directions; // Flattened xyz normalized in Unity space (length = Count * 3)
            public float[] Depths;     // Per-connection depth in Unity units (length = Count)
            public int Count => Types != null ? Types.Length : 0;
        }

        [Serializable]
        public class PartEntry
        {
            public string PartNumber;
            public string Description;
            public string MeshAssetPath;
            public string StudlessLodMeshAssetPath;
            public Vector3 BoundsSize;
            public int[] SubmeshColorCodes;
            public string[] SubmeshTextureNames;  // Parallel to SubmeshColorCodes, null entries for untextured
            public ConnectionData Connections;
        }

        public List<PartEntry> Parts = new List<PartEntry>();

        Dictionary<string, PartEntry> _lookup;

        public void BuildLookup()
        {
            _lookup = new Dictionary<string, PartEntry>(Parts.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Parts)
                _lookup[entry.PartNumber] = entry;
        }

        public PartEntry GetPart(string partNumber)
        {
            if (_lookup == null)
                BuildLookup();
            return _lookup.TryGetValue(partNumber, out var entry) ? entry : null;
        }

        public bool HasPart(string partNumber)
        {
            if (_lookup == null)
                BuildLookup();
            return _lookup.ContainsKey(partNumber);
        }

        public bool RemovePart(string partNumber)
        {
            if (_lookup == null)
                BuildLookup();

            _lookup.Remove(partNumber);

            for (int i = Parts.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Parts[i].PartNumber, partNumber, StringComparison.OrdinalIgnoreCase))
                {
                    Parts.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        public void SetPart(string partNumber, string description, string meshAssetPath,
            string studlessLodMeshAssetPath,
            Vector3 boundsSize, int[] submeshColorCodes, string[] submeshTextureNames = null,
            ConnectionData connectionData = null)
        {
            if (_lookup == null)
                BuildLookup();

            var entry = new PartEntry
            {
                PartNumber = partNumber,
                Description = description,
                MeshAssetPath = meshAssetPath,
                StudlessLodMeshAssetPath = studlessLodMeshAssetPath,
                BoundsSize = boundsSize,
                SubmeshColorCodes = submeshColorCodes,
                SubmeshTextureNames = submeshTextureNames,
                Connections = connectionData
            };

            _lookup[partNumber] = entry;

            for (int i = 0; i < Parts.Count; i++)
            {
                if (string.Equals(Parts[i].PartNumber, partNumber, StringComparison.OrdinalIgnoreCase))
                {
                    Parts[i] = entry;
                    return;
                }
            }

            Parts.Add(entry);
        }
    }
}
