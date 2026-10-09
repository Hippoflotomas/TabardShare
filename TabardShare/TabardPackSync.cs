using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace TabardShare
{
    /// <summary>
    ///     Copies tabard packs from the player-facing drop folder (Documents\Valheim Custom Tabards)
    ///     into the mod's working folder (BepInEx\config\TabardShare\Tabards).
    ///
    ///     Pack layout is fixed, the same as BannerShare and ShieldShare:
    ///       MyTabards.zip
    ///         TabardID/            one folder per tabard, directly inside the zip
    ///           Tabard.json        required
    ///           Pattern1.png ...
    ///         AnotherTabardID/
    ///           ...
    ///     Files at the top of the zip, folders nested deeper, and folders without Tabard.json are
    ///     ignored (with a warning in the log). Loose folders in the drop folder are ignored too -
    ///     only .zip files are read.
    ///
    ///     Every folder we create is tagged with a marker file. On the next launch, tagged folders whose
    ///     zip has gone are removed; untagged folders (added by hand, or the built-in "Missing"
    ///     fallback) are never touched.
    /// </summary>
    internal static class TabardPackSync
    {
        public const string MarkerFileName = ".tabardshare-source";
        public const string TemplatesFolderName = "_Templates";

        public static void Sync(string dropFolder, string tabardsFolder)
        {
            var produced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool anyFailures = false;

            foreach (var zipPath in Directory.GetFiles(dropFolder, "*.zip").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string zipName = Path.GetFileName(zipPath);
                try
                {
                    int count = SyncZip(zipPath, tabardsFolder, produced);
                    if (count == 0)
                        Jotunn.Logger.LogWarning($"[TabardShare] '{zipName}' contains no tabards. Each tabard must be a folder inside the zip, with {TabardPack.JsonFileName} and its images in that folder.");
                    else
                        Jotunn.Logger.LogInfo($"[TabardShare] Synced {count} tabard(s) from '{zipName}'.");
                }
                catch (Exception ex)
                {
                    anyFailures = true;
                    Jotunn.Logger.LogError($"[TabardShare] Failed to read '{zipName}': {ex.Message}");
                }
            }

            foreach (var dir in Directory.GetDirectories(dropFolder))
            {
                string name = Path.GetFileName(dir);
                if (!name.StartsWith("_") && !name.StartsWith("."))
                    Jotunn.Logger.LogWarning($"[TabardShare] The folder '{name}' in the drop folder is ignored - zip it first (the tabard folder goes inside the zip).");
            }

            if (anyFailures)
            {
                Jotunn.Logger.LogWarning("[TabardShare] Skipping clean-up this launch because a zip failed to read.");
                return;
            }

            foreach (var existing in Directory.GetDirectories(tabardsFolder))
            {
                string name = Path.GetFileName(existing);
                if (produced.Contains(name) || !File.Exists(Path.Combine(existing, MarkerFileName)))
                    continue; // still wanted, or not ours to remove

                try
                {
                    Directory.Delete(existing, true);
                    Jotunn.Logger.LogInfo($"[TabardShare] Removed '{name}' - no zip in the drop folder contains it any more.");
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogError($"[TabardShare] Failed to remove '{name}': {ex.Message}");
                }
            }
        }

        private static int SyncZip(string zipPath, string tabardsFolder, HashSet<string> produced)
        {
            string zipName = Path.GetFileName(zipPath);
            int count = 0;

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                // Group entries by their top-level folder. Some zip tools write '\' separators, so normalise.
                var byFolder = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
                var ignoredRootFiles = new List<string>();
                var ignoredNested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in archive.Entries)
                {
                    string full = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (full.EndsWith("/") || full.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
                        continue; // folder entries, macOS junk

                    string[] parts = full.Split('/');
                    if (parts[parts.Length - 1].StartsWith("."))
                        continue; // hidden files (.DS_Store etc.)

                    if (parts.Length == 1)
                    {
                        ignoredRootFiles.Add(full);
                        continue;
                    }
                    if (parts.Length > 2)
                    {
                        ignoredNested.Add(parts[0] + "/" + parts[1]);
                        continue;
                    }

                    List<ZipArchiveEntry> list;
                    if (!byFolder.TryGetValue(parts[0], out list))
                        byFolder[parts[0]] = list = new List<ZipArchiveEntry>();
                    list.Add(entry);
                }

                if (ignoredRootFiles.Count > 0)
                    Jotunn.Logger.LogWarning($"[TabardShare] '{zipName}': files at the top of the zip are ignored ({string.Join(", ", ignoredRootFiles.Take(5))}" +
                                             $"{(ignoredRootFiles.Count > 5 ? ", ..." : "")}). Put them in a folder named after the tabard.");
                foreach (var nested in ignoredNested)
                    Jotunn.Logger.LogWarning($"[TabardShare] '{zipName}': '{nested}/' is ignored - tabard folders must sit directly inside the zip, not inside another folder.");

                foreach (var kv in byFolder)
                {
                    string folderName = kv.Key;

                    if (!kv.Value.Any(e => TabardPack.IsJson(FileNameOf(e))))
                    {
                        Jotunn.Logger.LogWarning($"[TabardShare] '{zipName}': '{folderName}/' is skipped - it has no {TabardPack.JsonFileName}.");
                        continue;
                    }

                    string name = SafeFolderName(folderName);
                    if (name != folderName)
                        Jotunn.Logger.LogInfo($"[TabardShare] '{zipName}': '{folderName}' is used as '{name}' (only letters, digits, '-' and '_' are kept).");
                    if (!produced.Add(name))
                    {
                        Jotunn.Logger.LogWarning($"[TabardShare] Two tabards are called '{name}' - skipping the one in '{zipName}'. Tabard folder names must be unique.");
                        continue;
                    }

                    string target = PrepareTarget(tabardsFolder, name, zipName);
                    foreach (var entry in kv.Value)
                        entry.ExtractToFile(Path.Combine(target, FileNameOf(entry)), true);
                    count++;
                }
            }
            return count;
        }

        private static string FileNameOf(ZipArchiveEntry entry)
        {
            string full = entry.FullName.Replace('\\', '/');
            return full.Substring(full.LastIndexOf('/') + 1);
        }

        private static string PrepareTarget(string tabardsFolder, string name, string source)
        {
            string target = Path.Combine(tabardsFolder, name);
            if (Directory.Exists(target))
                Directory.Delete(target, true);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, MarkerFileName), source);
            return target;
        }

        /// <summary>Folder names become prefab names (the tabard's ID), so keep them to letters, digits, '_' and '-'.</summary>
        public static string SafeFolderName(string name)
        {
            var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray();
            string safe = new string(chars).Trim('_');
            return safe.Length == 0 ? "Tabard" : safe;
        }
    }
}
