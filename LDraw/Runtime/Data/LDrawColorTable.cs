using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LDraw
{
    public enum LDrawFinishType
    {
        Solid,
        Transparent,
        Chrome,
        Pearl,
        Rubber,
        MatteMetallic,
        Metal,
        Glitter,
        Speckle
    }

    [Serializable]
    public class LDrawColorEntry
    {
        public int Code;
        public string Name;
        public Color Value;
        public Color EdgeColor;
        public float Alpha;
        public LDrawFinishType Finish;
    }

    [CreateAssetMenu(fileName = "LDrawColorTable", menuName = "LDraw/Color Table")]
    public class LDrawColorTable : ScriptableObject
    {
        public List<LDrawColorEntry> Colors = new List<LDrawColorEntry>();

        Dictionary<int, LDrawColorEntry> _lookup;

        public void BuildLookup()
        {
            _lookup = new Dictionary<int, LDrawColorEntry>(Colors.Count);
            foreach (var entry in Colors)
                _lookup[entry.Code] = entry;
        }

        public LDrawColorEntry GetColor(int code)
        {
            if (_lookup == null)
                BuildLookup();
            return _lookup.TryGetValue(code, out var entry) ? entry : null;
        }

        public void ParseFromFile(string ldConfigPath)
        {
            Colors.Clear();
            if (!File.Exists(ldConfigPath))
            {
                Debug.LogError($"LDConfig.ldr not found at: {ldConfigPath}");
                return;
            }

            var lines = File.ReadAllLines(ldConfigPath);

            // Match lines like: 0 !COLOUR Black CODE 0 VALUE #1B2A34 EDGE #808080 [ALPHA 128] [CHROME|PEARLESCENT|RUBBER|METAL|MATERIAL ...]
            var regex = new Regex(
                @"0\s+!COLOUR\s+(\S+)\s+CODE\s+(\d+)\s+VALUE\s+#([0-9A-Fa-f]{6})\s+EDGE\s+#([0-9A-Fa-f]{6})(.*)",
                RegexOptions.Compiled);

            foreach (var line in lines)
            {
                var match = regex.Match(line);
                if (!match.Success) continue;

                var entry = new LDrawColorEntry
                {
                    Name = match.Groups[1].Value,
                    Code = int.Parse(match.Groups[2].Value),
                    Value = ParseHexColor(match.Groups[3].Value),
                    EdgeColor = ParseHexColor(match.Groups[4].Value),
                    Alpha = 255f,
                    Finish = LDrawFinishType.Solid
                };

                string remainder = match.Groups[5].Value;

                // Parse ALPHA
                var alphaMatch = Regex.Match(remainder, @"ALPHA\s+(\d+)");
                if (alphaMatch.Success)
                {
                    entry.Alpha = float.Parse(alphaMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    entry.Value = new Color(entry.Value.r, entry.Value.g, entry.Value.b, entry.Alpha / 255f);
                    if (entry.Alpha < 255f)
                        entry.Finish = LDrawFinishType.Transparent;
                }

                // Parse finish types
                if (remainder.Contains("CHROME"))
                    entry.Finish = LDrawFinishType.Chrome;
                else if (remainder.Contains("PEARLESCENT"))
                    entry.Finish = LDrawFinishType.Pearl;
                else if (remainder.Contains("RUBBER"))
                    entry.Finish = LDrawFinishType.Rubber;
                else if (remainder.Contains("MATTE_METALLIC"))
                    entry.Finish = LDrawFinishType.MatteMetallic;
                else if (remainder.Contains("METAL"))
                    entry.Finish = LDrawFinishType.Metal;
                else if (remainder.Contains("GLITTER"))
                    entry.Finish = LDrawFinishType.Glitter;
                else if (remainder.Contains("SPECKLE"))
                    entry.Finish = LDrawFinishType.Speckle;

                Colors.Add(entry);
            }

            BuildLookup();
            Debug.Log($"Parsed {Colors.Count} LDraw colors from {ldConfigPath}");
        }

        static Color ParseHexColor(string hex)
        {
            int r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
            int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
            int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
