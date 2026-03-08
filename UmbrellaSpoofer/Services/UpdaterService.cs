using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using UmbrellaSpoofer.Data;

namespace UmbrellaSpoofer.Services
{
    public class UpdaterService
    {
        readonly string configPath;
        readonly string updatesRoot;

        public UpdaterService()
        {
            configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updater.json");
            updatesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmbrellaSpoofer", "updates");
        }

        public UpdaterConfig LoadConfig()
        {
            if (!File.Exists(configPath)) return new UpdaterConfig();
            try
            {
                var json = File.ReadAllText(configPath);
                var cfg = JsonSerializer.Deserialize<UpdaterConfig>(json);
                return cfg ?? new UpdaterConfig();
            }
            catch
            {
                return new UpdaterConfig();
            }
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool force = false)
        {
            var cfg = LoadConfig();
            if (!cfg.IsValid())
                return UpdateCheckResult.CreateInvalidConfig();

            if (!force && !ShouldCheck(cfg))
                return UpdateCheckResult.CreateSkipped();

            var release = await GetLatestReleaseAsync(cfg);
            if (release == null || string.IsNullOrWhiteSpace(release.Tag))
                return UpdateCheckResult.CreateNoUpdate();

            var current = GetCurrentVersionString();
            var updateAvailable = IsNewerVersion(current, release.Tag);
            SaveLastCheck();
            return new UpdateCheckResult(updateAvailable, current, release);
        }

        public async Task<StageResult> DownloadAndStageAsync(UpdaterConfig cfg, ReleaseInfo release)
        {
            if (release.Assets.Count == 0) return StageResult.Fail("No assets available for this release");
            var asset = SelectAsset(cfg, release.Assets);
            if (asset == null) return StageResult.Fail("No matching asset found");

            Directory.CreateDirectory(updatesRoot);
            var tagFolder = Path.Combine(updatesRoot, SanitizeTag(release.Tag ?? "latest"));
            Directory.CreateDirectory(tagFolder);
            var assetPath = Path.Combine(tagFolder, asset.Name);

            var downloadOk = await DownloadAssetAsync(asset.DownloadUrl, assetPath);
            if (!downloadOk.Success) return StageResult.Fail(downloadOk.Error ?? "Download failed");

            var checksumOk = await VerifyChecksumAsync(cfg, release, assetPath);
            if (!checksumOk.Success) return StageResult.Fail(checksumOk.Error ?? "Checksum verification failed");

            var extractedPath = "";
            var isZip = assetPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            if (isZip)
            {
                extractedPath = Path.Combine(tagFolder, "extracted");
                if (Directory.Exists(extractedPath)) Directory.Delete(extractedPath, true);
                ZipFile.ExtractToDirectory(assetPath, extractedPath);
            }

            var pending = new PendingUpdate
            {
                Tag = release.Tag ?? "",
                AssetPath = assetPath,
                ExtractedPath = extractedPath,
                IsZip = isZip,
                InstallMode = cfg.InstallMode ?? "replace"
            };
            var pendingPath = Path.Combine(updatesRoot, "pending.json");
            File.WriteAllText(pendingPath, JsonSerializer.Serialize(pending));
            return StageResult.Ok(pendingPath);
        }

        public static bool TryHandleUpdateMode(string[] args)
        {
            if (args.Length >= 2 && args[0].Equals("--apply-update", StringComparison.OrdinalIgnoreCase))
            {
                var pendingPath = args[1];
                var targetExe = args.Length >= 3 ? args[2] : "";
                ApplyPendingUpdate(pendingPath, targetExe);
                return true;
            }
            return false;
        }

        public static void RestartToApplyUpdate(string pendingPath)
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exe)) return;
            var args = $"--apply-update \"{pendingPath}\" \"{exe}\"";
            Process.Start(new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = true });
            Application.Current.Shutdown();
        }

        static void ApplyPendingUpdate(string pendingPath, string targetExe)
        {
            if (!File.Exists(pendingPath)) return;
            PendingUpdate? pending = null;
            try
            {
                var json = File.ReadAllText(pendingPath);
                pending = JsonSerializer.Deserialize<PendingUpdate>(json);
            }
            catch
            {
                return;
            }
            if (pending == null) return;
            var targetDir = !string.IsNullOrWhiteSpace(targetExe) ? Path.GetDirectoryName(targetExe) : AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrWhiteSpace(targetDir)) return;
            if (pending.IsZip && Directory.Exists(pending.ExtractedPath))
            {
                CopyDirectory(pending.ExtractedPath, targetDir);
            }
            else if (pending.AssetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && pending.InstallMode.Equals("installer", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo { FileName = pending.AssetPath, UseShellExecute = true });
            }
            try
            {
                File.Delete(pendingPath);
            }
            catch
            {
            }
            if (!string.IsNullOrWhiteSpace(targetExe))
            {
                Process.Start(new ProcessStartInfo { FileName = targetExe, UseShellExecute = true });
            }
        }

        async Task<ReleaseInfo?> GetLatestReleaseAsync(UpdaterConfig cfg)
        {
            using var http = CreateHttp();
            if (!cfg.AllowPrerelease)
            {
                var latestUrl = $"https://api.github.com/repos/{cfg.Owner}/{cfg.Repo}/releases/latest";
                var latest = await GetReleaseAsync(http, latestUrl);
                return latest;
            }
            var listUrl = $"https://api.github.com/repos/{cfg.Owner}/{cfg.Repo}/releases?per_page=20";
            var list = await GetReleasesAsync(http, listUrl);
            return list.FirstOrDefault(r => !r.IsDraft && (cfg.AllowPrerelease || !r.IsPrerelease));
        }

        static HttpClient CreateHttp()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("UmbrellaSpoofer");
            return http;
        }

        static async Task<ReleaseInfo?> GetReleaseAsync(HttpClient http, string url)
        {
            var r = await http.GetAsync(url);
            if (!r.IsSuccessStatusCode) return null;
            var json = await r.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return ParseRelease(doc.RootElement);
        }

        static async Task<List<ReleaseInfo>> GetReleasesAsync(HttpClient http, string url)
        {
            var r = await http.GetAsync(url);
            if (!r.IsSuccessStatusCode) return new List<ReleaseInfo>();
            var json = await r.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new List<ReleaseInfo>();
            var list = new List<ReleaseInfo>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var rel = ParseRelease(el);
                if (rel != null) list.Add(rel);
            }
            return list;
        }

        static ReleaseInfo? ParseRelease(JsonElement root)
        {
            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
            var prerelease = root.TryGetProperty("prerelease", out var preEl) && preEl.GetBoolean();
            var draft = root.TryGetProperty("draft", out var draftEl) && draftEl.GetBoolean();
            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assetsEl.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                    var dl = a.TryGetProperty("browser_download_url", out var dlEl) ? dlEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(dl))
                        assets.Add(new ReleaseAsset(name!, dl!));
                }
            }
            return new ReleaseInfo(tag, htmlUrl, prerelease, draft, assets);
        }

        async Task<DownloadResult> DownloadAssetAsync(string downloadUrl, string filePath)
        {
            using var http = CreateHttp();
            using var r = await http.GetAsync(downloadUrl);
            if (!r.IsSuccessStatusCode) return DownloadResult.Fail($"Download failed: {r.StatusCode}");
            await using var fs = File.Create(filePath);
            await r.Content.CopyToAsync(fs);
            return DownloadResult.Ok(filePath);
        }

        async Task<ChecksumResult> VerifyChecksumAsync(UpdaterConfig cfg, ReleaseInfo release, string assetPath)
        {
            var expected = cfg.ExpectedSha256;
            if (string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(cfg.ChecksumAssetName))
            {
                var checksumAsset = release.Assets.FirstOrDefault(a => a.Name.Equals(cfg.ChecksumAssetName, StringComparison.OrdinalIgnoreCase));
                if (checksumAsset != null)
                    expected = await DownloadChecksumAsync(checksumAsset.DownloadUrl);
            }
            if (string.IsNullOrWhiteSpace(expected)) return ChecksumResult.Ok();
            var actual = ComputeSha256(assetPath);
            if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) return ChecksumResult.Ok();
            return ChecksumResult.Fail("Checksum mismatch");
        }

        async Task<string> DownloadChecksumAsync(string url)
        {
            using var http = CreateHttp();
            var r = await http.GetAsync(url);
            if (!r.IsSuccessStatusCode) return "";
            var text = await r.Content.ReadAsStringAsync();
            var parts = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0].Trim() : "";
        }

        static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha.ComputeHash(stream);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        static ReleaseAsset? SelectAsset(UpdaterConfig cfg, List<ReleaseAsset> assets)
        {
            if (!string.IsNullOrWhiteSpace(cfg.AssetName))
            {
                var exact = assets.FirstOrDefault(a => a.Name.Equals(cfg.AssetName, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
                var contains = assets.FirstOrDefault(a => a.Name.IndexOf(cfg.AssetName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (contains != null) return contains;
            }
            var zip = assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (zip != null) return zip;
            return assets.FirstOrDefault();
        }

        static string GetCurrentVersionString()
        {
            var info = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info)) return info;
            var ver = Assembly.GetEntryAssembly()?.GetName().Version;
            return ver?.ToString() ?? "0.0.0";
        }

        static bool IsNewerVersion(string current, string latest)
        {
            var c = ParseVersion(current);
            var l = ParseVersion(latest);
            if (c != null && l != null) return l > c;
            return !string.Equals(NormalizeVersion(current), NormalizeVersion(latest), StringComparison.OrdinalIgnoreCase);
        }

        static Version? ParseVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var v = NormalizeVersion(value);
            if (Version.TryParse(v, out var ver)) return ver;
            return null;
        }

        static string NormalizeVersion(string value)
        {
            var v = value.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v.Substring(1);
            var idx = v.IndexOfAny(new[] { '-', '+' });
            if (idx >= 0) v = v.Substring(0, idx);
            return v;
        }

        static string SanitizeTag(string tag)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(tag.Where(c => !invalid.Contains(c)).ToArray());
        }

        static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceDir, dir);
                Directory.CreateDirectory(Path.Combine(targetDir, rel));
            }
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceDir, file);
                var target = Path.Combine(targetDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
        }

        bool ShouldCheck(UpdaterConfig cfg)
        {
            var store = new SqliteStore();
            store.EnsureCreated();
            var last = store.GetSetting("updater.lastCheckUtc");
            if (!DateTime.TryParse(last, null, DateTimeStyles.AdjustToUniversal, out var lastUtc))
                return true;
            var interval = TimeSpan.FromMinutes(Math.Max(1, cfg.CheckIntervalMinutes));
            return DateTime.UtcNow - lastUtc > interval;
        }

        void SaveLastCheck()
        {
            var store = new SqliteStore();
            store.EnsureCreated();
            store.SetSetting("updater.lastCheckUtc", DateTime.UtcNow.ToString("O"));
        }
    }

    public record ReleaseAsset(string Name, string DownloadUrl);
    public record ReleaseInfo(string? Tag, string? HtmlUrl, bool IsPrerelease, bool IsDraft, List<ReleaseAsset> Assets);

    public record UpdateCheckResult(bool UpdateAvailable, string CurrentVersion, ReleaseInfo? Release, bool Skipped = false, bool InvalidConfig = false)
    {
        public static UpdateCheckResult CreateNoUpdate() => new(false, "", null);
        public static UpdateCheckResult CreateSkipped() => new(false, "", null, true);
        public static UpdateCheckResult CreateInvalidConfig() => new(false, "", null, false, true);
    }

    public record DownloadResult(bool Success, string? FilePath, string? Error)
    {
        public static DownloadResult Ok(string path) => new(true, path, null);
        public static DownloadResult Fail(string error) => new(false, null, error);
    }

    public record StageResult(bool Success, string? PendingPath, string? Error)
    {
        public static StageResult Ok(string path) => new(true, path, null);
        public static StageResult Fail(string error) => new(false, null, error);
    }

    public record ChecksumResult(bool Success, string? Error)
    {
        public static ChecksumResult Ok() => new(true, null);
        public static ChecksumResult Fail(string error) => new(false, error);
    }

    public class PendingUpdate
    {
        public string Tag { get; set; } = "";
        public string AssetPath { get; set; } = "";
        public string ExtractedPath { get; set; } = "";
        public bool IsZip { get; set; }
        public string InstallMode { get; set; } = "replace";
    }
}
