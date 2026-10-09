using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TabardShare
{
    public class TabardRequirement
    {
        public string Item { get; set; }
        public int Amount { get; set; } = 1;
        public int AmountPerLevel { get; set; } = 0;
    }

    /// <summary>
    ///     Contents of Tabard.json. The FILE is required (a folder without it is skipped), but every field
    ///     has a default. Property names are matched case-insensitively ("displayName" = "DisplayName").
    /// </summary>
    public class TabardDefinition
    {
        public string DisplayName { get; set; }

        /// <summary>Accepted as another spelling of DisplayName.</summary>
        public string Name
        {
            get => null;
            set { if (string.IsNullOrWhiteSpace(DisplayName)) DisplayName = value; }
        }

        public string Description { get; set; }
        public string CraftingStation { get; set; }
        public int MinStationLevel { get; set; } = 1;
        /// <summary>Optional cap on how many patterns to use. Normally the Pattern files are just counted.</summary>
        public int StyleCount { get; set; }
        public bool Hidden { get; set; }
        public List<TabardRequirement> Requirements { get; set; }
    }

    /// <summary>
    ///     One tabard folder on disk = one crafting entry:
    ///       TabardID/
    ///         Tabard.json            required
    ///         Pattern1.png ...       one per style, numbered from 1 (max 16). Front on the left half, back on the right.
    ///         Icon1.png ...          optional, per style - made from the pattern's front if missing
    ///         BumpMap.png            optional normal map, shared by every style
    ///         MetallicGlossMap.png   optional metal mask, shared by every style
    ///     File names are fixed but not case-sensitive. PNG only.
    /// </summary>
    internal sealed class TabardPack
    {
        public const int MaxStyles = 16;
        public const string DefaultCraftingStation = "piece_workbench";
        public const string JsonFileName = "Tabard.json";
        public const string ImageExtension = ".png";

        public const string BumpMapFile = "BumpMap";
        public const string MetallicGlossMapFile = "MetallicGlossMap";

        private static readonly Regex PatternName = new Regex(@"^pattern(\d+)$", RegexOptions.IgnoreCase);
        private static readonly Regex IconName = new Regex(@"^icon(\d+)$", RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, string> StationAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "workbench", "piece_workbench" },
            { "forge", "forge" },
            { "stonecutter", "piece_stonecutter" },
            { "artisan", "piece_artisanstation" },
            { "artisantable", "piece_artisanstation" },
            { "artisanstation", "piece_artisanstation" },
            { "blackforge", "blackforge" },
            { "blacksmith", "blackforge" },
            { "galdr", "piece_magetable" },
            { "galdrtable", "piece_magetable" },
            { "magetable", "piece_magetable" },
            { "cauldron", "piece_cauldron" },
            { "none", "" },
        };

        /// <summary>The folder name - the tabard's ID.</summary>
        public string Name { get; private set; }
        public string Folder { get; private set; }
        public TabardDefinition Definition { get; private set; }
        /// <summary>Why this folder can't be loaded, or null if it's fine. Folders with a problem are skipped.</summary>
        public string Problem { get; private set; }

        /// <summary>Pattern image paths in style order (style 0 = lowest number).</summary>
        public List<string> PatternPaths { get; } = new List<string>();
        /// <summary>Icon path per style, or null where the icon should be made from the pattern.</summary>
        public List<string> IconPaths { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        private readonly Dictionary<string, string> imagesByStem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string FindImage(string stem) => imagesByStem.TryGetValue(stem, out var path) ? path : null;

        public static bool IsImage(string path) =>
            string.Equals(Path.GetExtension(path), ImageExtension, StringComparison.OrdinalIgnoreCase);

        public static bool IsJson(string fileName) =>
            string.Equals(fileName, JsonFileName, StringComparison.OrdinalIgnoreCase);

        public static TabardPack Load(string folder)
        {
            var pack = new TabardPack { Folder = folder, Name = Path.GetFileName(folder) };

            string jsonPath = null;
            foreach (var file in Directory.GetFiles(folder))
            {
                string fileName = Path.GetFileName(file);
                string ext = Path.GetExtension(file);
                if (IsJson(fileName))
                    jsonPath = file;
                else if (IsImage(file))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (pack.imagesByStem.ContainsKey(stem))
                        pack.Warnings.Add($"Both '{Path.GetFileName(pack.imagesByStem[stem])}' and '{fileName}' exist - using the first.");
                    else
                        pack.imagesByStem[stem] = file;
                }
                else if (ext.Length > 0 && !fileName.StartsWith(".")
                         && !string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase))
                {
                    pack.Warnings.Add($"'{fileName}' is ignored - images must be .png.");
                }
            }

            if (jsonPath == null)
            {
                pack.Problem = $"no {JsonFileName} in the folder";
                return pack;
            }

            try
            {
                pack.Definition = JsonConvert.DeserializeObject<TabardDefinition>(File.ReadAllText(jsonPath)) ?? new TabardDefinition();
            }
            catch (Exception ex)
            {
                pack.Problem = $"{JsonFileName} can't be read: {ex.Message}";
                return pack;
            }

            pack.ApplyDefaults();
            pack.CollectStyles();
            return pack;
        }

        private void ApplyDefaults()
        {
            var d = Definition;
            if (string.IsNullOrWhiteSpace(d.DisplayName))
                d.DisplayName = Name.Replace('_', ' ').Replace('-', ' ');
            if (d.Description == null)
                d.Description = "";

            if (string.IsNullOrWhiteSpace(d.CraftingStation))
                d.CraftingStation = DefaultCraftingStation;
            else if (StationAliases.TryGetValue(d.CraftingStation.Replace(" ", "").Replace("_", ""), out var mapped))
                d.CraftingStation = mapped;
            if (d.MinStationLevel < 1)
                d.MinStationLevel = 1;

            d.Requirements = (d.Requirements ?? new List<TabardRequirement>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Item))
                .ToList();
            if (d.Requirements.Count == 0)
                d.Requirements.Add(new TabardRequirement { Item = "LeatherScraps", Amount = 4 });
            foreach (var r in d.Requirements)
                if (r.Amount < 1) r.Amount = 1;
        }

        private void CollectStyles()
        {
            var patternsByNumber = new SortedDictionary<int, string>();
            var iconsByNumber = new Dictionary<int, string>();

            foreach (var kv in imagesByStem)
            {
                var m = PatternName.Match(kv.Key);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
                {
                    if (patternsByNumber.ContainsKey(n))
                        Warnings.Add($"Two pattern files for style {n} ('{Path.GetFileName(patternsByNumber[n])}' and '{Path.GetFileName(kv.Value)}') - using the first.");
                    else
                        patternsByNumber[n] = kv.Value;
                    continue;
                }

                m = IconName.Match(kv.Key);
                if (m.Success && int.TryParse(m.Groups[1].Value, out n) && !iconsByNumber.ContainsKey(n))
                    iconsByNumber[n] = kv.Value;
            }

            int limit = MaxStyles;
            if (Definition.StyleCount > 0 && Definition.StyleCount < limit)
                limit = Definition.StyleCount;

            foreach (var kv in patternsByNumber)
            {
                if (PatternPaths.Count >= limit)
                {
                    Warnings.Add(limit < MaxStyles
                        ? $"StyleCount is {Definition.StyleCount} in {JsonFileName} - extra pattern files were ignored."
                        : $"Only {MaxStyles} styles fit on one tabard - extra pattern files were ignored. Put the rest in another tabard folder.");
                    break;
                }
                PatternPaths.Add(kv.Value);
                IconPaths.Add(iconsByNumber.TryGetValue(kv.Key, out var icon) ? icon : null);
            }

            if (PatternPaths.Count == 0)
                Warnings.Add("no Pattern1.png (or any PatternN.png) - it will show the magenta and black 'missing' pattern.");
        }
    }
}
