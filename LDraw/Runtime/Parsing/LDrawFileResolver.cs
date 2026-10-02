using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LDraw
{
    public class LDrawFileResolver
    {
        readonly Dictionary<string, string> _fileLookup = new Dictionary<string, string>();
        readonly string _ldrawRoot;
        readonly HashSet<string> _unresolvedWarnings = new HashSet<string>();

        public LDrawFileResolver(string ldrawRoot)
        {
            _ldrawRoot = ldrawRoot;
            BuildIndex();
        }

        void BuildIndex()
        {
            _fileLookup.Clear();

            // Recursively scan ALL .dat files under the ldraw root
            if (!Directory.Exists(_ldrawRoot))
            {
                Debug.LogError($"LDraw root not found: {_ldrawRoot}");
                return;
            }

            foreach (var file in Directory.GetFiles(_ldrawRoot, "*.dat", SearchOption.AllDirectories))
            {
                string relativePath = file.Substring(_ldrawRoot.Length + 1)
                    .Replace('\\', '/').ToLowerInvariant();
                _fileLookup[relativePath] = file;
            }

            // Also index .ldr files (for LDConfig.ldr etc.)
            foreach (var file in Directory.GetFiles(_ldrawRoot, "*.ldr", SearchOption.AllDirectories))
            {
                string relativePath = file.Substring(_ldrawRoot.Length + 1)
                    .Replace('\\', '/').ToLowerInvariant();
                _fileLookup[relativePath] = file;
            }

            Debug.Log($"LDrawFileResolver indexed {_fileLookup.Count} entries from {_ldrawRoot}");
        }

        /// <summary>
        /// Resolves an LDraw subfile reference to an absolute file path.
        /// Per the LDraw spec, search order is:
        /// 1. Same directory as the referring file
        /// 2. p/ (primitives)
        /// 3. parts/ (parts)
        /// 4. parts/s/ (subparts)
        /// </summary>
        /// <param name="reference">The filename reference from the LDraw file (e.g., "stud.dat", "s/3001s01.dat")</param>
        /// <param name="currentFileDir">Absolute path of the directory containing the referring file</param>
        public string Resolve(string reference, string currentFileDir = null)
        {
            if (string.IsNullOrEmpty(reference))
                return null;

            string normalized = reference.Replace('\\', '/').ToLowerInvariant();

            // 1. Search relative to current file's directory first
            // This is critical for p/48/ hi-res primitives referencing each other
            if (currentFileDir != null)
            {
                string candidate = Path.Combine(currentFileDir, reference).Replace('\\', '/');
                if (File.Exists(candidate))
                    return candidate;
            }

            // 2. Try as a relative path from ldraw root (handles "p/stud.dat", "parts/s/foo.dat", etc.)
            if (_fileLookup.TryGetValue(normalized, out var path))
                return path;

            // 3. Try with standard directory prefixes
            // A reference like "stud.dat" → try "p/stud.dat"
            // A reference like "s/3001s01.dat" → try "parts/s/3001s01.dat"
            // A reference like "48/4-4cyli.dat" → try "p/48/4-4cyli.dat"
            string[] prefixes = { "p/", "parts/", "parts/s/" };
            foreach (var prefix in prefixes)
            {
                if (_fileLookup.TryGetValue(prefix + normalized, out path))
                    return path;
            }

            // 4. Try just the filename (last resort)
            string fileNameOnly = Path.GetFileName(normalized);
            foreach (var kvp in _fileLookup)
            {
                if (kvp.Key.EndsWith("/" + fileNameOnly) || kvp.Key == fileNameOnly)
                    return kvp.Value;
            }

            return null;
        }

        /// <summary>
        /// Resolve with warning tracking. Logs each unique unresolved reference once.
        /// </summary>
        public string ResolveWithWarning(string reference, string currentFileDir, string parentFile)
        {
            var result = Resolve(reference, currentFileDir);
            if (result == null)
            {
                string key = reference.ToLowerInvariant();
                if (_unresolvedWarnings.Add(key))
                    Debug.LogWarning($"LDraw: Could not resolve '{reference}' (referenced from {Path.GetFileName(parentFile)})");
            }
            return result;
        }

        public int UnresolvedCount => _unresolvedWarnings.Count;

        public string GetLDrawRoot() => _ldrawRoot;
    }
}
