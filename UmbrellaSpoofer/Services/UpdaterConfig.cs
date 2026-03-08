using System;
using System.Text.Json.Serialization;

namespace UmbrellaSpoofer.Services
{
    public class UpdaterConfig
    {
        [JsonPropertyName("owner")]
        public string Owner { get; set; } = "";

        [JsonPropertyName("repo")]
        public string Repo { get; set; } = "";

        [JsonPropertyName("assetName")]
        public string AssetName { get; set; } = "";

        [JsonPropertyName("autoDownload")]
        public bool AutoDownload { get; set; } = false;

        [JsonPropertyName("autoInstall")]
        public bool AutoInstall { get; set; } = false;

        [JsonPropertyName("allowPrerelease")]
        public bool AllowPrerelease { get; set; } = false;

        [JsonPropertyName("checkIntervalMinutes")]
        public int CheckIntervalMinutes { get; set; } = 360;

        [JsonPropertyName("checksumAssetName")]
        public string ChecksumAssetName { get; set; } = "";

        [JsonPropertyName("expectedSha256")]
        public string ExpectedSha256 { get; set; } = "";

        [JsonPropertyName("installMode")]
        public string InstallMode { get; set; } = "replace";

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repo);
        }
    }
}
