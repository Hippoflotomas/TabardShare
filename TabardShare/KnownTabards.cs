using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace TabardShare
{
    /// <summary>One tabard TabardShare has registered at some point.</summary>
    public class KnownTabard
    {
        public string DisplayName { get; set; }
        /// <summary>Highest style count ever seen (informational).</summary>
        public int Styles { get; set; }
        public string LastSeen { get; set; }
    }

    /// <summary>
    ///     Remembers every pack tabard that has been registered (BepInEx\config\TabardShare\known-tabards.json).
    ///     When a pack's zip is removed, its tabards are still registered as "missing" stand-ins. Otherwise the
    ///     game would silently delete every copy players own the next time their character or a chest loads
    ///     ("Failed to find item prefab"). Delete an entry from the file to forget a tabard for good.
    /// </summary>
    internal sealed class KnownTabards
    {
        private readonly string path;
        private Dictionary<string, KnownTabard> entries = new Dictionary<string, KnownTabard>(StringComparer.OrdinalIgnoreCase);

        public KnownTabards(string path)
        {
            this.path = path;
        }

        public IEnumerable<KeyValuePair<string, KnownTabard>> All => entries;

        public void Load()
        {
            if (!File.Exists(path))
                return;
            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, KnownTabard>>(File.ReadAllText(path));
                if (loaded != null)
                    entries = new Dictionary<string, KnownTabard>(loaded, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                // Keep a copy rather than overwrite it - it's the only record of removed packs.
                string backup = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(path, backup, true); } catch { }
                Jotunn.Logger.LogError($"[TabardShare] '{Path.GetFileName(path)}' could not be read ({ex.Message}); saved a copy as '{Path.GetFileName(backup)}' and started a new list.");
            }
        }

        public void Remember(string id, string displayName, int styles)
        {
            if (!entries.TryGetValue(id, out var entry))
                entries[id] = entry = new KnownTabard();
            entry.DisplayName = displayName;
            entry.Styles = Math.Max(entry.Styles, styles);
            entry.LastSeen = DateTime.Now.ToString("yyyy-MM-dd");
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(entries, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[TabardShare] Could not save '{Path.GetFileName(path)}': {ex.Message}");
            }
        }
    }
}
