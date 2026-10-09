using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TabardShare
{
    /// <summary>
    ///     The tabard items the game knows about, and the material for each of their styles.
    ///     One item per tabard folder (TabardPack); each PatternN.png is one style (ItemData.m_variant).
    ///     The pattern image IS the texture - the model's UVs are ours, so nothing is baked or remapped.
    /// </summary>
    internal static class TabardItems
    {
        /// <summary>The vanilla item a tabard item is cloned from: the linen cape gives a folded-cloth ground model.</summary>
        private const string BaseItemPrefab = "CapeLinen";

        /// <summary>Custom data on every tabard item: its ID (the folder name). Used by the missing-pack handling (Milestone 3).</summary>
        public const string ItemIdKey = "TabardShare.Id";

        /// <summary>The built-in missing tabard (never craftable): stand-ins for unknown tabards are cloned from it.</summary>
        public const string MissingTemplatePrefabName = TabardShare.ItemPrefabPrefix + "_Missing";

        private const string DropFolderName = "Valheim Custom Tabards";
        private const int IconSize = 128;

        internal sealed class TabardDef
        {
            public string PrefabName;
            public readonly List<Material> Styles = new List<Material>();
        }

        private static readonly Dictionary<string, TabardDef> byPrefab = new Dictionary<string, TabardDef>(StringComparer.Ordinal);
        private static Texture2D missingPattern;
        private static Material missingMaterial;
        private static Sprite missingIcon;

        public static bool IsTabardPrefab(string prefabName) =>
            !string.IsNullOrEmpty(prefabName) && byPrefab.ContainsKey(prefabName);

        public static bool IsTabard(ItemDrop.ItemData item) =>
            item != null && item.m_dropPrefab != null && byPrefab.ContainsKey(item.m_dropPrefab.name);

        /// <summary>The material for a tabard's style, or null if the tabard isn't installed here.</summary>
        public static Material GetMaterial(string prefabName, int variant)
        {
            if (string.IsNullOrEmpty(prefabName) || !byPrefab.TryGetValue(prefabName, out var def) || def.Styles.Count == 0)
                return null;
            if (variant < 0 || variant >= def.Styles.Count)
                variant = 0;
            return def.Styles[variant];
        }

        // ------------------------------------------------------------------------------------------
        // TabardPacks
        // ------------------------------------------------------------------------------------------

        public static string GetTabardsFolderPath()
        {
            string path = Path.Combine(BepInEx.Paths.ConfigPath, TabardShare.PluginName, "Tabards");
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetDropFolderPath()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string dropFolder = Path.Combine(documents, DropFolderName);
            Directory.CreateDirectory(dropFolder);
            return dropFolder;
        }

        /// <summary>
        ///     Syncs the zips from Documents, registers one item per tabard folder, then a stand-in for every
        ///     tabard seen before whose pack is gone (so nobody's copies get deleted).
        /// </summary>
        public static void LoadPacks()
        {
            string tabardsFolder = GetTabardsFolderPath();
            string dropFolder = GetDropFolderPath();

            WriteAuthorHelp(dropFolder);

            try
            {
                TabardPackSync.Sync(dropFolder, tabardsFolder);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[TabardShare] Syncing from '{dropFolder}' failed: {ex}");
            }

            RegisterMissingTemplate();

            var known = new KnownTabards(Path.Combine(Path.GetDirectoryName(tabardsFolder), "known-tabards.json"));
            known.Load();
            var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var folders = Directory.GetDirectories(tabardsFolder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            Jotunn.Logger.LogInfo($"[TabardShare] Found {folders.Length} tabard folder(s) in {tabardsFolder}");

            foreach (var folder in folders)
            {
                string name = Path.GetFileName(folder);
                if (name.StartsWith("_") || name.StartsWith("."))
                    continue;
                try
                {
                    var pack = TabardPack.Load(folder);
                    int styles = RegisterPack(pack);
                    if (styles > 0)
                    {
                        string id = TabardPackSync.SafeFolderName(pack.Name);
                        registered.Add(id);
                        known.Remember(id, pack.Definition.DisplayName, styles);
                    }
                }
                catch (Exception ex)
                {
                    // One broken pack must never stop the others from loading.
                    Jotunn.Logger.LogError($"[TabardShare] Tabard '{name}' failed to load: {ex}");
                }
            }

            RegisterStandIns(known, registered);
            known.Save();
        }

        /// <summary>Stand-ins for tabards whose pack has been removed: the missing pattern, under the old name.</summary>
        private static void RegisterStandIns(KnownTabards known, HashSet<string> registered)
        {
            foreach (var kv in known.All.ToArray())
            {
                if (registered.Contains(kv.Key))
                    continue;
                string name = string.IsNullOrWhiteSpace(kv.Value.DisplayName) ? kv.Key : kv.Value.DisplayName;
                try
                {
                    var def = new TabardDef { PrefabName = TabardShare.ItemPrefabPrefix + kv.Key };
                    def.Styles.Add(GetMissingMaterial());
                    if (RegisterItem(def, kv.Key, name + " (missing)",
                            $"The TabardPack for this tabard ('{kv.Key}') is no longer installed. Put its zip back in 'Valheim Custom Tabards' to restore it.",
                            "piece_workbench", 1, new[] { new RequirementConfig("Wood", 1, 0, true) }, new[] { GetMissingIcon() }, false))
                        Jotunn.Logger.LogWarning($"[TabardShare] '{kv.Key}' is no longer installed - registered a stand-in so existing copies aren't deleted.");
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogError($"[TabardShare] Stand-in for '{kv.Key}' failed: {ex}");
                }
            }
        }

        /// <summary>The hidden built-in tabard that MissingTabards clones for tabards it meets in game.</summary>
        private static void RegisterMissingTemplate()
        {
            var def = new TabardDef { PrefabName = MissingTemplatePrefabName };
            def.Styles.Add(GetMissingMaterial());
            RegisterItem(def, "_Missing", "Missing tabard", "TabardShare's built-in fallback pattern.",
                "piece_workbench", 1, new[] { new RequirementConfig("Wood", 1, 0, true) }, new[] { GetMissingIcon() }, false);
        }

        /// <summary>Called by MissingTabards when it registers a stand-in mid-game: make it a tabard that draws the missing pattern.</summary>
        public static void AddStandIn(string prefabName)
        {
            if (byPrefab.ContainsKey(prefabName))
                return;
            var def = new TabardDef { PrefabName = prefabName };
            def.Styles.Add(GetMissingMaterial());
            byPrefab[prefabName] = def;
        }

        /// <summary>Registers one tabard folder. Returns its number of styles, or -1 if it wasn't registered.</summary>
        private static int RegisterPack(TabardPack pack)
        {
            foreach (var warning in pack.Warnings)
                Jotunn.Logger.LogWarning($"[TabardShare] '{pack.Name}': {warning}");
            if (pack.Problem != null)
            {
                Jotunn.Logger.LogError($"[TabardShare] Skipping '{pack.Name}': {pack.Problem}.");
                return -1;
            }

            var d = pack.Definition;
            string id = TabardPackSync.SafeFolderName(pack.Name);
            var def = new TabardDef { PrefabName = TabardShare.ItemPrefabPrefix + id };

            // Shared by every style.
            var bump = TextureIO.Load(pack.FindImage(TabardPack.BumpMapFile), linear: true);
            var metal = TextureIO.Load(pack.FindImage(TabardPack.MetallicGlossMapFile), linear: true);
            Finish(bump, compress: false);
            Finish(metal, compress: true);

            var icons = new List<Sprite>();
            for (int i = 0; i < pack.PatternPaths.Count; i++)
            {
                var pattern = TextureIO.Load(pack.PatternPaths[i]);
                if (pattern == null)
                {
                    Jotunn.Logger.LogWarning($"[TabardShare] '{pack.Name}': {Path.GetFileName(pack.PatternPaths[i])} can't be read - style {i + 1} shows the 'missing' pattern.");
                    pattern = null;
                }
                else if (pattern.width != pattern.height)
                {
                    Jotunn.Logger.LogWarning($"[TabardShare] '{pack.Name}': {Path.GetFileName(pack.PatternPaths[i])} is {pattern.width} x {pattern.height} - patterns should be square, so it will look stretched.");
                }

                var iconTex = TextureIO.Load(pack.IconPaths[i], mipmaps: false);
                if (pattern == null)
                {
                    icons.Add(iconTex != null ? TextureIO.ToSprite(iconTex) : GetMissingIcon());
                    def.Styles.Add(GetMissingMaterial());
                    continue;
                }
                icons.Add(iconTex != null ? TextureIO.ToSprite(iconTex) : MakeIcon(pattern));
                Finish(pattern, compress: true);
                def.Styles.Add(TabardModel.CreateMaterial(pattern, bump, metal, $"{def.PrefabName}_{i + 1}"));
            }

            if (def.Styles.Count == 0)
            {
                def.Styles.Add(GetMissingMaterial());
                icons.Add(GetMissingIcon());
            }

            var requirements = d.Requirements.Select(r => new RequirementConfig(r.Item, r.Amount, r.AmountPerLevel, true)).ToArray();
            if (!RegisterItem(def, id, d.DisplayName, d.Description, d.CraftingStation, d.MinStationLevel, requirements, icons.ToArray(), !d.Hidden))
                return -1;

            Jotunn.Logger.LogInfo($"[TabardShare] Registered '{d.DisplayName}' from '{pack.Name}' ({def.Styles.Count} style(s)" +
                                  $"{(bump != null ? ", bump map" : "")}{(metal != null ? ", metal map" : "")}{(d.Hidden ? ", not craftable" : "")}).");
            return def.Styles.Count;
        }

        /// <summary>
        ///     Built-in test tabard: the UV layout as two styles (diagnostic blue front / red back, and plain white).
        ///     Always registered, so saved test tabards keep working; only craftable when <paramref name="craftable"/>.
        /// </summary>
        public static void RegisterTestTabard(bool craftable)
        {
            const string id = "Test";
            string prefabName = TabardShare.ItemPrefabPrefix + id;

            var plain = TextureIO.LoadBytes(TabardModel.ReadResource("TabardUVLayout.png"), "TabardShare_Test_1");
            if (plain == null)
            {
                Jotunn.Logger.LogError("[TabardShare] The built-in test pattern couldn't be read - no test tabard.");
                return;
            }
            // The UV layout is mostly transparent, and transparency cuts holes - flatten it onto solid backgrounds:
            //   style 1 "diagnostic": front half blue, back half red, both bright at the top and dark at the bottom;
            //   style 2 "plain": the layout lines on white.
            var diagnostic = Flatten(plain, true, "TabardShare_Test_1");
            var white = Flatten(plain, false, "TabardShare_Test_2");
            UnityEngine.Object.Destroy(plain);

            var def = new TabardDef { PrefabName = prefabName };
            def.Styles.Add(TabardModel.CreateMaterial(diagnostic, null, null, "TabardShare_Test_1"));
            def.Styles.Add(TabardModel.CreateMaterial(white, null, null, "TabardShare_Test_2"));
            var icons = new[] { MakeIcon(diagnostic), MakeIcon(white) };

            if (RegisterItem(def, id, "Test Tabard", "A plain test tabard. Shows the UV layout so the mapping can be checked in game.",
                    "piece_workbench", 1, new[] { new RequirementConfig("Wood", 1, 0, true) }, icons, craftable))
                Jotunn.Logger.LogInfo($"[TabardShare] Registered '{prefabName}' ({(craftable ? "craftable" : "not craftable - see the config")}).");
        }

        private static bool RegisterItem(TabardDef def, string id, string displayName, string description, string station, int stationLevel,
            RequirementConfig[] requirements, Sprite[] icons, bool craftable)
        {
            if (byPrefab.ContainsKey(def.PrefabName) || PrefabManager.Instance.GetPrefab(def.PrefabName) != null)
            {
                Jotunn.Logger.LogWarning($"[TabardShare] An item called '{def.PrefabName}' already exists - skipping. Tabard folder names must be unique (and 'Test' is taken).");
                return false;
            }

            var prefab = PrefabManager.Instance.CreateClonedPrefab(def.PrefabName, BaseItemPrefab);
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                Jotunn.Logger.LogError($"[TabardShare] Couldn't clone '{BaseItemPrefab}' for '{def.PrefabName}'.");
                return false;
            }

            // Turn the cape clone into a plain, cosmetic item in our own slot: no armour, no effects, no set bonus,
            // no durability. Misc stops vanilla equipping it as a cape; TabardEquip makes it equipable again.
            var shared = drop.m_itemData.m_shared;
            shared.m_itemType = ItemDrop.ItemData.ItemType.Misc;
            shared.m_armor = 0f;
            shared.m_armorPerLevel = 0f;
            shared.m_equipStatusEffect = null;
            shared.m_setStatusEffect = null;
            shared.m_setName = "";
            shared.m_setSize = 0;
            shared.m_damageModifiers = new List<HitData.DamageModPair>();
            shared.m_movementModifier = 0f;
            shared.m_eitrRegenModifier = 0f;
            shared.m_heatResistanceModifier = 0f;
            shared.m_maxQuality = 1;
            shared.m_useDurability = false;
            shared.m_weight = 1f;
            shared.m_teleportable = true;

            // Every copy of the item carries its ID (ItemData.Clone copies custom data).
            if (drop.m_itemData.m_customData == null)
                drop.m_itemData.m_customData = new Dictionary<string, string>();
            drop.m_itemData.m_customData[ItemIdKey] = id;

            var config = new ItemConfig
            {
                Name = displayName,
                Description = description,
                CraftingStation = station,
                MinStationLevel = stationLevel,
                Icons = icons,
                Requirements = requirements,
                Enabled = craftable,
            };
            ItemManager.Instance.AddItem(new CustomItem(prefab, false, config));

            if (icons != null && icons.Length > 1)
                shared.m_variants = icons.Length;

            byPrefab[def.PrefabName] = def;
            return true;
        }

        // ------------------------------------------------------------------------------------------
        // Textures and icons
        // ------------------------------------------------------------------------------------------

        /// <summary>
        ///     Once a texture's pixels have been read (icons made), compress it on the GPU (4x smaller: a 2048
        ///     pattern drops from ~21 MB to ~5 MB) and free the CPU copy.
        /// </summary>
        private static void Finish(Texture2D tex, bool compress)
        {
            if (tex == null)
                return;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 4;
            if (compress && tex.width % 4 == 0 && tex.height % 4 == 0)
                tex.Compress(true);
            tex.Apply(false, true);
        }

        /// <summary>A square icon showing the front panel (left half of the pattern), on a transparent background.</summary>
        private static Sprite MakeIcon(Texture2D pattern)
        {
            float srcW = pattern.width * 0.5f, srcH = pattern.height;
            float scale = Mathf.Min(IconSize / srcW, IconSize / srcH) * 0.96f;
            float drawW = srcW * scale, drawH = srcH * scale;
            float x0 = (IconSize - drawW) * 0.5f, y0 = (IconSize - drawH) * 0.5f;
            // Sample from a smaller mip level so the shrink doesn't alias (the pattern has mipmaps).
            int mip = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(1f / scale, 2f)), 0, Mathf.Max(0, pattern.mipmapCount - 1));

            var px = new Color32[IconSize * IconSize];
            for (int y = 0; y < IconSize; y++)
            {
                float v = (y + 0.5f - y0) / drawH;
                for (int x = 0; x < IconSize; x++)
                {
                    float u = (x + 0.5f - x0) / drawW;
                    if (u < 0f || u > 1f || v < 0f || v > 1f)
                        continue; // transparent
                    Color c = pattern.GetPixelBilinear(u * 0.5f, v, mip);
                    c.a = c.a >= 0.5f ? 1f : 0f; // same cut-out as in game
                    px[y * IconSize + x] = c;
                }
            }
            var tex = TextureIO.FromPixels(px, IconSize, IconSize, false);
            tex.name = pattern.name + "_icon";
            return TextureIO.ToSprite(tex);
        }

        /// <summary>
        ///     The "missing" pattern: each panel quarterly magenta and sable (1st and 4th magenta, 1st at the
        ///     top left as you look at the panel). Used by stand-ins and by folders with no readable patterns.
        /// </summary>
        public static Texture2D GetMissingPattern()
        {
            if (missingPattern != null)
                return missingPattern;
            const int size = 256, half = size / 2;
            var px = new Color32[size * size];
            var magenta = new Color32(255, 0, 255, 255);
            var sable = new Color32(0, 0, 0, 255);
            for (int y = 0; y < size; y++)          // row 0 is the BOTTOM of the image
                for (int x = 0; x < size; x++)
                {
                    bool left = (x % half) < half / 2;   // each panel is one half of the image
                    bool top = y >= size / 2;
                    px[y * size + x] = left == top ? magenta : sable;
                }
            missingPattern = TextureIO.FromPixels(px, size, size, true);
            missingPattern.name = "TabardShare_Missing";
            missingPattern.filterMode = FilterMode.Point;
            return missingPattern;
        }

        public static Material GetMissingMaterial()
        {
            if (missingMaterial == null)
                missingMaterial = TabardModel.CreateMaterial(GetMissingPattern(), null, null, "TabardShare_Missing");
            return missingMaterial;
        }

        private static Sprite GetMissingIcon()
        {
            if (missingIcon == null)
                missingIcon = MakeIcon(GetMissingPattern());
            return missingIcon;
        }

        /// <summary>
        ///     Composites a (partly transparent) image over a solid background and returns an opaque copy.
        ///     <paramref name="diagnostic"/>: left half blue, right half red, with a top-bright vertical gradient;
        ///     otherwise plain white.
        /// </summary>
        private static Texture2D Flatten(Texture2D source, bool diagnostic, string name)
        {
            int w = source.width, h = source.height;
            var px = source.GetPixels32(); // row 0 is the BOTTOM of the image
            for (int y = 0; y < h; y++)
            {
                float brightness = diagnostic ? 0.3f + 0.7f * y / (h - 1f) : 1f;
                for (int x = 0; x < w; x++)
                {
                    Color background = diagnostic
                        ? (x < w / 2 ? new Color(0.25f, 0.45f, 1f) : new Color(1f, 0.3f, 0.25f)) * brightness
                        : Color.white;
                    int i = y * w + x;
                    Color src = px[i];
                    Color outColor = Color.Lerp(background, new Color(src.r, src.g, src.b), src.a);
                    outColor.a = 1f;
                    px[i] = outColor;
                }
            }
            var tex = TextureIO.FromPixels(px, w, h, true);
            tex.name = name;
            return tex;
        }

        // ------------------------------------------------------------------------------------------
        // Help for pack authors, written into the drop folder
        // ------------------------------------------------------------------------------------------

        private static void WriteAuthorHelp(string dropFolder)
        {
            try
            {
                File.WriteAllText(Path.Combine(dropFolder, AuthorGuide.FileName), AuthorGuide.Text);
                string templates = Path.Combine(dropFolder, TabardPackSync.TemplatesFolderName);
                Directory.CreateDirectory(templates);
                var layout = TabardModel.ReadResource("TabardUVLayout.png");
                if (layout != null)
                    File.WriteAllBytes(Path.Combine(templates, AuthorGuide.UvLayoutFileName), layout);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[TabardShare] Could not write the pack author guide: {ex.Message}");
            }
        }
    }
}
