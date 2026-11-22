using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace ContextMenuManager.Methods
{
    /// <summary>
    /// パッケージアプリ（MSIX / スパースパッケージ）が AppxManifest の
    /// FileExplorerContextMenus 拡張で登録している右クリックメニュー項目を自動検出する。
    /// Windows 11 の新しいメニューに出る「Copilot に質問」やセキュリティソフトの項目などは
    /// この方式で登録されており、従来の shell / shellex レジストリには現れない。
    /// </summary>
    internal static class PackagedContextMenuHelper
    {
        private const string PackagedComPath = @"PackagedCom\Package";
        private const string PackageRepositoryPath = @"Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\PackageRepository\Packages";

        internal sealed class Entry
        {
            public Guid Guid { get; init; }
            public string PackageFullName { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public string PublisherDisplayName { get; init; } = string.Empty;
            public string IdentityName { get; init; } = string.Empty;
            public string VerbId { get; init; } = string.Empty;
            /// <summary>実際に右クリックメニューに表示される名前（IExplorerCommand.GetTitle で取得）</summary>
            public string Title { get; set; } = string.Empty;
            /// <summary>マニフェストの ItemType（"*"、"Directory"、"Directory\Background"、".docx" など）</summary>
            public List<string> ItemTypes { get; } = new();
        }

        private static List<Entry> cache;
        private static readonly object CacheLock = new();

        /// <summary>検出結果を破棄し、次回アクセス時に再検出させる。</summary>
        public static void Reload()
        {
            lock (CacheLock) cache = null;
        }

        public static IReadOnlyList<Entry> Entries
        {
            get
            {
                lock (CacheLock)
                {
                    cache ??= Scan();
                    return cache;
                }
            }
        }

        /// <summary>指定した場面に表示すべき項目を返す。</summary>
        public static IEnumerable<Entry> GetEntries(Scenes scene, string currentExtension)
        {
            foreach (var entry in Entries)
            {
                if (entry.ItemTypes.Any(type => MatchScene(type, scene, currentExtension))) yield return entry;
            }
        }

        private static bool MatchScene(string itemType, Scenes scene, string currentExtension)
        {
            if (string.IsNullOrEmpty(itemType)) return false;
            var t = itemType.Trim();
            switch (scene)
            {
                case Scenes.File:
                    // 全ファイル（*）に加え、拡張子限定（.docx など）の項目もここにまとめて表示する
                    return t == "*" || t.StartsWith(".");
                case Scenes.Folder:
                    return Eq(t, "Folder") || Eq(t, "Directory");
                case Scenes.Directory:
                    return Eq(t, "Directory");
                case Scenes.Background:
                case Scenes.Desktop:
                    return Eq(t, @"Directory\Background");
                case Scenes.Drive:
                    return Eq(t, "Drive");
                case Scenes.AllObjects:
                    return Eq(t, "AllFilesystemObjects");
                case Scenes.CustomExtension:
                    return !string.IsNullOrEmpty(currentExtension) && Eq(t, currentExtension);
                default:
                    return false;
            }
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static List<Entry> Scan()
        {
            var result = new Dictionary<Guid, Entry>();
            try
            {
                // 読み取り専用で開く（所有権の取得などは行わない）
                using var packagedCom = Registry.ClassesRoot.OpenSubKey(PackagedComPath);
                if (packagedCom == null) return new List<Entry>();
                foreach (var fullName in packagedCom.GetSubKeyNames())
                {
                    try
                    {
                        ScanPackage(fullName, result);
                    }
                    catch
                    {
                        // 1 パッケージの読み込み失敗で全体を止めない
                    }
                }
            }
            catch
            {
            }
            var entries = result.Values.ToList();
            try
            {
                ResolveTitles(entries);
            }
            catch
            {
            }
            return entries;
        }

        #region 実際の表示名の取得

        private static string TitleCachePath => Path.Combine(AppConfig.ConfigDir, "PackagedMenuTitles.txt");

        /// <summary>
        /// 実際の右クリックメニューの表示名を取得する。
        /// 結果はパッケージのフルネーム（バージョンを含む）ごとに保存し、アプリが更新されたときだけ取り直す。
        /// </summary>
        private static void ResolveTitles(List<Entry> entries)
        {
            var cache = LoadTitleCache();
            var requests = new List<(Guid Guid, string ItemPath)>();
            foreach (var entry in entries)
            {
                if (cache.TryGetValue(CacheKey(entry), out var cached))
                {
                    entry.Title = cached;
                }
                else
                {
                    requests.Add((entry.Guid, GetProbeItemPath(entry)));
                }
            }
            if (requests.Count == 0) return;

            var titles = ExplorerCommandTitleProbe.Query(requests);
            foreach (var entry in entries)
            {
                var key = CacheKey(entry);
                if (cache.ContainsKey(key)) continue;
                entry.Title = titles.TryGetValue(entry.Guid, out var title) ? title : string.Empty;
                // 取得できなかった場合も空で保存し、毎回の起動で取り直さないようにする
                cache[key] = entry.Title;
            }
            SaveTitleCache(cache, entries);
        }

        /// <summary>保存済みの表示名を破棄する（次回の検出で取り直す）。</summary>
        public static void ClearTitleCache()
        {
            try { File.Delete(TitleCachePath); } catch { }
            Reload();
        }

        private static string CacheKey(Entry entry) => $"{entry.PackageFullName}|{entry.Guid:B}";

        private static Dictionary<string, string> LoadTitleCache()
        {
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(TitleCachePath)) return cache;
                foreach (var line in File.ReadAllLines(TitleCachePath, System.Text.Encoding.UTF8))
                {
                    var i = line.IndexOf('\t');
                    if (i <= 0) continue;
                    cache[line[..i]] = line[(i + 1)..];
                }
            }
            catch
            {
            }
            return cache;
        }

        private static void SaveTitleCache(Dictionary<string, string> cache, List<Entry> entries)
        {
            try
            {
                // 現在インストールされているパッケージの分だけ残す（古いバージョンの分は捨てる）
                var keys = new HashSet<string>(entries.Select(CacheKey), StringComparer.OrdinalIgnoreCase);
                var lines = cache.Where(kv => keys.Contains(kv.Key)).Select(kv => $"{kv.Key}\t{kv.Value}");
                File.WriteAllLines(TitleCachePath, lines, new System.Text.UTF8Encoding(true));
            }
            catch
            {
            }
        }

        /// <summary>GetTitle に渡す「選択中の項目」として使うファイルやフォルダーを用意する。</summary>
        private static string GetProbeItemPath(Entry entry)
        {
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "ContextMenuManager_probe");
                Directory.CreateDirectory(dir);
                var type = entry.ItemTypes.FirstOrDefault(t => t == "*")
                    ?? entry.ItemTypes.FirstOrDefault(t => t.StartsWith("."))
                    ?? entry.ItemTypes.FirstOrDefault() ?? "*";
                if (type == "*" || type.StartsWith("."))
                {
                    var ext = type == "*" ? ".txt" : type;
                    var file = Path.Combine(dir, "probe" + ext);
                    if (!File.Exists(file)) File.WriteAllBytes(file, Array.Empty<byte>());
                    return file;
                }
                if (Eq(type, "Drive")) return Path.GetPathRoot(Environment.SystemDirectory);
                // Directory、Directory\Background、Folder など
                var folder = Path.Combine(dir, "probe_folder");
                Directory.CreateDirectory(folder);
                return folder;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        private static void ScanPackage(string fullName, Dictionary<Guid, Entry> result)
        {
            var manifestPath = FindManifest(fullName);
            if (manifestPath == null) return;

            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(manifestPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                doc.Load(reader);
            }

            var itemTypeNodes = doc.SelectNodes("//*[local-name()='FileExplorerContextMenus']/*[local-name()='ItemType']");
            if (itemTypeNodes == null || itemTypeNodes.Count == 0) return;

            var identityName = (doc.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']") as XmlElement)?.GetAttribute("Name") ?? string.Empty;
            var displayName = ResolveDisplayName(fullName, identityName,
                doc.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='DisplayName']")?.InnerText);
            var publisherRaw = doc.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='PublisherDisplayName']")?.InnerText;
            var publisherName = string.IsNullOrWhiteSpace(publisherRaw) ? string.Empty : ResolveDisplayName(fullName, identityName, publisherRaw);
            if (publisherName == (string.IsNullOrEmpty(identityName) ? fullName.Split('_')[0] : identityName)) publisherName = string.Empty;

            foreach (XmlElement itemTypeXE in itemTypeNodes)
            {
                var type = itemTypeXE.GetAttribute("Type");
                foreach (XmlNode verbXN in itemTypeXE.SelectNodes("*[local-name()='Verb']"))
                {
                    if (verbXN is not XmlElement verbXE) continue;
                    if (!Guid.TryParse(verbXE.GetAttribute("Clsid"), out var guid)) continue;
                    if (!result.TryGetValue(guid, out var entry))
                    {
                        entry = new Entry
                        {
                            Guid = guid,
                            PackageFullName = fullName,
                            DisplayName = displayName,
                            PublisherDisplayName = publisherName,
                            IdentityName = identityName,
                            VerbId = verbXE.GetAttribute("Id"),
                        };
                        result.Add(guid, entry);
                    }
                    if (!entry.ItemTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) entry.ItemTypes.Add(type);
                }
            }
        }

        /// <summary>パッケージの AppxManifest.xml の場所を探す。</summary>
        private static string FindManifest(string fullName)
        {
            var candidates = new List<string>();
            using (var repoKey = Registry.ClassesRoot.OpenSubKey($@"{PackageRepositoryPath}\{fullName}"))
            {
                var path = repoKey?.GetValue("Path")?.ToString();
                if (!string.IsNullOrEmpty(path)) candidates.Add(Path.Combine(path, "AppxManifest.xml"));
            }
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            candidates.Add(Path.Combine(programFiles, "WindowsApps", fullName, "AppxManifest.xml"));
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            candidates.Add(Path.Combine(programData, @"Microsoft\Windows\AppRepository\Packages", fullName, "AppxManifest.xml"));
            candidates.Add(Path.Combine(programData, @"Microsoft\Windows\AppRepository", fullName + ".xml"));

            foreach (var candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }
            return null;
        }

        private static string ResolveDisplayName(string fullName, string identityName, string raw)
        {
            var fallback = string.IsNullOrEmpty(identityName) ? fullName.Split('_')[0] : identityName;
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            raw = raw.Trim();
            if (!raw.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return raw;

            var res = raw.Substring("ms-resource:".Length);
            var tries = new List<string>();
            if (res.StartsWith("//")) tries.Add($"@{{{fullName}?ms-resource:{res}}}");
            else if (res.StartsWith("/")) tries.Add($"@{{{fullName}?ms-resource://{identityName}{res}}}");
            else
            {
                tries.Add($"@{{{fullName}?ms-resource://{identityName}/Resources/{res}}}");
                tries.Add($"@{{{fullName}?ms-resource://{identityName}/{res}}}");
            }
            foreach (var t in tries)
            {
                try
                {
                    var s = ResourceString.GetDirectString(t);
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
                catch
                {
                }
            }
            return fallback;
        }

        private static readonly string[] GenericNameWords =
        {
            "ShellEx", "Shell Ex", "Shell Extension", "ShellExtension", "Package", "Sparse", "Context Menu", "ContextMenu", "Explorer Command",
        };

        /// <summary>アプリ名として役に立たない汎用的な表示名か</summary>
        private static bool IsGenericName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            return GenericNameWords.Any(w => name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>一覧に表示する名前を作る。</summary>
        public static string GetItemText(Entry entry)
        {
            // 実際の右クリックメニューの表示名が取れていれば、それを最優先で使う
            var text = entry.Title;
            if (string.IsNullOrWhiteSpace(text)) text = GuidInfo.GetText(entry.Guid);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = entry.DisplayName;
                if (!string.IsNullOrWhiteSpace(entry.VerbId) && !Eq(entry.VerbId, entry.DisplayName))
                {
                    text += $" ({entry.VerbId})";
                }
                // 「ShellEx Package」のような汎用的な表示名では、どのアプリの項目か分からないため
                // 発行元を前に、パッケージ名を後ろに付ける
                if (IsGenericName(entry.DisplayName))
                {
                    var packageName = string.IsNullOrWhiteSpace(entry.IdentityName)
                        ? entry.PackageFullName.Split('_')[0]
                        : entry.IdentityName;
                    var publisher = entry.PublisherDisplayName;
                    if (!string.IsNullOrWhiteSpace(publisher)
                        && text.IndexOf(publisher, StringComparison.OrdinalIgnoreCase) < 0
                        && packageName.IndexOf(publisher, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        text = $"{publisher} - {text}";
                    }
                    if (!string.IsNullOrWhiteSpace(packageName) && !Eq(packageName, entry.DisplayName))
                    {
                        text += $" - {packageName}";
                    }
                }
            }
            var exts = entry.ItemTypes.Where(t => t.StartsWith(".")).ToList();
            if (exts.Count > 0 && !entry.ItemTypes.Contains("*"))
            {
                var shown = string.Join(", ", exts.Take(4));
                if (exts.Count > 4) shown += ", …";
                text += $" [{shown}]";
            }
            return text;
        }
    }
}
