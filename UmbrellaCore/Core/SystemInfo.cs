using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using UmbrellaCore.Driver;
using UmbrellaCore.Hardware;
using UmbrellaCore.Models;
using UmbrellaCore.Spoofing;

namespace UmbrellaCore
{
    /// <summary>
    /// Main entry point for system information detection and spoofing.
    /// Coordinates hardware reading, driver communication, and registry spoofing.
    /// </summary>
    public partial class SystemInfo
    {
        private readonly KernelDriverService? _driverService;
        public bool IsWin11 { get; }

        public SystemInfo()
        {
            IsWin11 = DetectWin11();
            try
            {
                _driverService = new KernelDriverService();
                SpoofRegistry.SetDriverService(_driverService);
            }
            catch
            {
                _driverService = null;
                SpoofRegistry.SetDriverService(null);
            }
        }

        /// <summary>
        /// Detects Windows 11 by checking OS build number (build >= 22000).
        /// Also checks product name for "Windows 11".
        /// </summary>
        public static bool DetectWin11()
        {
            try
            {
                var productName = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "ProductName", null)?.ToString() ?? "";
                if (productName.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
            try
            {
                var buildStr = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "CurrentBuildNumber", null)?.ToString();
                if (int.TryParse(buildStr, out int build) && build >= 22000)
                    return true;
            }
            catch { }
            return false;
        }

        public Dictionary<string, string> GetAll(bool spoof = false)
        {
            var info = new Dictionary<string, string>();

            if (spoof)
            {
                var originalEfi = HardwareReader.QueryWmi("Win32_BootConfiguration", "Description") ?? "\\\\Device\\\\Harddisk0\\\\Partition1";
                var originalVolumes = HardwareReader.GetVolumeSerials();

                info["Disk_Serials"] = GetSpoofedDiskSerials();
                info["CPU_Identifier"] = GetSpoofedCpuIdentifier();
                info["GPU_Identifiers"] = GetSpoofedGpuIdentifier();
                info["Volume_Serials"] = GetSpoofedVolumeSerials(originalVolumes);
                info["TPM_Identity"] = SpoofGenerator.GenerateComponentSerial("TPM", 20);
                info["EFI_Boot"] = GetSpoofedEfiBoot(originalEfi);
                info["ARP_Cache"] = HardwareReader.Rng.Next(1, 100).ToString();
                info["MachineGuid"] = GetSpoofedMachineGuid();
                info["BIOS_Serial"] = SpoofGenerator.GetStylePreservedRandom(SmbiosParser.GetSpoofableBiosSerial() ?? SpoofGenerator.GenerateRandomHex(12));
                info["Monitor_Serials"] = GetSpoofedMonitorSerials();
                info["MAC_Addresses"] = SpoofGenerator.GenerateRandomMac();

                if (IsWin11)
                {
                    info["BaseBoard_Serial"] = SpoofGenerator.GetStylePreservedRandom(GetBaseBoardSerialSmbiosWin11() ?? SpoofGenerator.GenerateRandomHex(15));
                    info["EFI_Version"] = SpoofGenerator.GetStylePreservedRandom(GetBiosVersionSmbios() ?? SpoofGenerator.GenerateRandomHex(10));
                    info["RAM_Serials"] = GetSpoofedRamSerials();
                }
                else
                {
                    info["BaseBoard_Serial"] = SpoofGenerator.GetStylePreservedRandom(GetBaseBoardSerialWin10() ?? SpoofGenerator.GenerateRandomHex(15));
                    info["EFI_Version"] = SpoofGenerator.GetStylePreservedRandom(GetEfiVersionWin10() ?? SpoofGenerator.GenerateRandomHex(10));
                    info["RAM_Serials"] = GetSpoofedRamSerials();
                }
            }
            else
            {
                info["Disk_Serials"] = string.Join(", ", HardwareReader.GetDiskSerials());
                info["CPU_Identifier"] = string.Join(", ", HardwareReader.GetCpuIdentifiers());
                info["GPU_Identifiers"] = string.Join(", ", HardwareReader.GetGpuIdentifiers());
                info["Volume_Serials"] = string.Join(", ", HardwareReader.GetVolumeSerials());
                info["EFI_Boot"] = HardwareReader.QueryWmi("Win32_BootConfiguration", "Description") ?? "\\\\Device\\\\Harddisk0\\\\Partition1";
                info["ARP_Cache"] = HardwareReader.GetArpCacheCount().ToString();
                info["MachineGuid"] = HardwareReader.ReadRegistryValue(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid");
                info["BIOS_Serial"] = SmbiosParser.GetBiosSerialDetection() ?? "N/A";
                info["Monitor_Serials"] = string.Join(", ", HardwareReader.GetMonitorSerials());
                info["MAC_Addresses"] = string.Join(", ", HardwareReader.GetMacs());

                if (IsWin11)
                {
                    // Enhanced RAM detection with native SMBIOS fallback chain
                    var ramSerials = GetRamSerialsSmbiosFromRaw();
                    info["RAM_Serials"] = ramSerials.Count > 0 ? string.Join(", ", ramSerials) : "N/A";

                    // Enhanced BaseBoard detection with native SMBIOS
                    var baseBoard = GetBaseBoardSerialSmbiosWin11();
                    info["BaseBoard_Serial"] = baseBoard ?? "N/A";

                    // Enhanced BIOS version detection
                    var efiVersion = GetBiosVersionSmbios();
                    info["EFI_Version"] = efiVersion ?? "N/A";

                    // Enhanced TPM detection
                    var tpm = GetTpmSerialSmbios();
                    info["TPM_Identity"] = tpm ?? "N/A";
                }
                else
                {
                    info["RAM_Serials"] = GetRamSerialsWin10();
                    info["BaseBoard_Serial"] = GetBaseBoardSerialWin10() ?? "N/A";
                    info["EFI_Version"] = GetEfiVersionWin10() ?? "N/A";
                    info["TPM_Identity"] = GetTpmSerialWin10() ?? "N/A";
                }
            }

            return info;
        }

        public Dictionary<string, string> GetAll() => GetAll(false);

        /// <summary>
        /// Spoof all hardware identifiers. Works on both Win10 and Win11.
        /// Uses Registry spoofing as primary method, with WMI refresh where possible.
        /// </summary>
        public bool SpoofAll(Dictionary<string, string> spoofValues, bool spoofMac, string? macInterfaceIndex)
        {
            try
            {
                if (_driverService != null && _driverService.IsDriverConnected)
                {
                    try
                    {
                        var result = _driverService.SpoofAll();
                        if (result.ComponentCount > 0)
                            return true;
                    }
                    catch
                    {
                        foreach (var kv in spoofValues)
                        {
                            switch (kv.Key)
                            {
                                case "MachineGuid":
                                    SpoofRegistry.SpoofMachineGuid(kv.Value);
                                    break;
                                case "BIOS_Serial":
                                    SpoofRegistry.SpoofBiosSerial(kv.Value);
                                    break;
                                case "BaseBoard_Serial":
                                    SpoofRegistry.SpoofBaseBoardSerial(kv.Value);
                                    break;
                                case "EFI_Version":
                                    SpoofRegistry.SpoofEfiVersion(kv.Value);
                                    break;
                                case "Monitor_Serials":
                                    SpoofRegistry.SpoofMonitorSerials(kv.Value);
                                    break;
                                case "RAM_Serials":
                                    SpoofRegistry.SpoofRamSerials(kv.Value);
                                    break;
                                case "Disk_Serials":
                                    SpoofRegistry.SpoofDiskSerials(kv.Value);
                                    break;
                                case "GPU_Identifiers":
                                    SpoofRegistry.SpoofGpuIdentifiers(kv.Value);
                                    break;
                                case "CPU_Identifier":
                                    SpoofRegistry.SpoofCpuIdentifier(kv.Value);
                                    break;
                            }
                        }
                        if (spoofMac && !string.IsNullOrEmpty(macInterfaceIndex))
                        {
                            var newMac = spoofValues.ContainsKey("MAC_Addresses") ? spoofValues["MAC_Addresses"] : "";
                            SpoofRegistry.SpoofMacWin11(newMac, macInterfaceIndex);
                        }
                        return true;
                    }
                }

                // Pure user-mode fallback (Win11 without kernel driver)
                foreach (var kv in spoofValues)
                {
                    switch (kv.Key)
                    {
                        case "MachineGuid": SpoofRegistry.SpoofMachineGuid(kv.Value); break;
                        case "BIOS_Serial": SpoofRegistry.SpoofBiosSerial(kv.Value); break;
                        case "BaseBoard_Serial": SpoofRegistry.SpoofBaseBoardSerial(kv.Value); break;
                        case "EFI_Version": SpoofRegistry.SpoofEfiVersion(kv.Value); break;
                        case "Monitor_Serials": SpoofRegistry.SpoofMonitorSerials(kv.Value); break;
                        case "RAM_Serials": SpoofRegistry.SpoofRamSerials(kv.Value); break;
                        case "Disk_Serials": SpoofRegistry.SpoofDiskSerials(kv.Value); break;
                        case "GPU_Identifiers": SpoofRegistry.SpoofGpuIdentifiers(kv.Value); break;
                        case "CPU_Identifier": SpoofRegistry.SpoofCpuIdentifier(kv.Value); break;
                    }
                }

                if (spoofMac && !string.IsNullOrEmpty(macInterfaceIndex) && spoofValues.ContainsKey("MAC_Addresses"))
                {
                    SpoofRegistry.SpoofMacWin11(spoofValues["MAC_Addresses"], macInterfaceIndex);
                }

                return true;
            }
            catch { return false; }
        }

        public bool IsDriverConnected() => _driverService != null && _driverService.IsDriverConnected;

        #region Individual Spoof Methods (for selective spoofing from UI)

        public string? SpoofMachineGuid(string value) => SpoofRegistry.SpoofMachineGuid(value);
        public string? SpoofBiosSerial(string value) => SpoofRegistry.SpoofBiosSerial(value);
        public string? SpoofBaseBoardSerial(string value) => SpoofRegistry.SpoofBaseBoardSerial(value);
        public string? SpoofEfiVersion(string value) => SpoofRegistry.SpoofEfiVersion(value);
        public string? SpoofMonitorSerials(string value) => SpoofRegistry.SpoofMonitorSerials(value);
        public string? SpoofRamSerials(string value) => SpoofRegistry.SpoofRamSerials(value);
        public string? SpoofDiskSerials(string value) => SpoofRegistry.SpoofDiskSerials(value);
        public string? SpoofGpuIdentifiers(string value) => SpoofRegistry.SpoofGpuIdentifiers(value);
        public string? SpoofCpuIdentifier(string value) => SpoofRegistry.SpoofCpuIdentifier(value);

        public bool RestoreAll()
        {
            if (_driverService != null && _driverService.IsDriverConnected)
            {
                try { var result = _driverService.RestoreAll(); return result.ComponentCount > 0; }
                catch { return false; }
            }
            return false;
        }

        public void RestoreFromPayloadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var payload = System.Text.Json.JsonSerializer.Deserialize<TempRestorePayload>(json);
                if (payload == null) return;
                SpoofAll(payload.Values, !string.IsNullOrEmpty(payload.MacAddress), payload.MacInterfaceIndex);
            }
            catch { }
        }

        public TempRestorePayload GetBackupSnapshot(BackupOptions options)
        {
            var current = GetAll(false);
            var snapshot = new TempRestorePayload { Values = new Dictionary<string, string>() };
            if (options.MachineGuid) snapshot.Values["MachineGuid"] = current["MachineGuid"];
            if (options.BiosSerial) snapshot.Values["BIOS_Serial"] = current["BIOS_Serial"];
            if (options.BaseBoardSerial) snapshot.Values["BaseBoard_Serial"] = current["BaseBoard_Serial"];
            if (options.EfiVersion) snapshot.Values["EFI_Version"] = current["EFI_Version"];
            return snapshot;
        }

        #endregion

        public List<NetworkAdapter> GetNetworkAdapters()
        {
            var adapters = new List<NetworkAdapter>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    adapters.Add(new NetworkAdapter
                    {
                        Name = nic.Name,
                        Description = nic.Description,
                        MacAddress = nic.GetPhysicalAddress().ToString(),
                        InterfaceIndex = nic.GetIPProperties().GetIPv4Properties()?.Index.ToString() ?? "0"
                    });
                }
            }
            catch { }
            return adapters;
        }

        // --- Spoofing helpers ---

        private string GetSpoofedEfiBoot(string originalEfi)
        {
            if (string.IsNullOrWhiteSpace(originalEfi)) originalEfi = "\\\\Device\\\\Harddisk0\\\\Partition1";
            var match = Regex.Match(originalEfi, @"(?i)Harddisk(\d+)");
            if (match.Success)
            {
                int currentDisk = int.Parse(match.Groups[1].Value);
                int newDisk;
                do { newDisk = HardwareReader.Rng.Next(0, 5); } while (newDisk == currentDisk);
                return Regex.Replace(originalEfi, @"(?i)Harddisk\d+", $"Harddisk{newDisk}");
            }
            return originalEfi;
        }

        private string GetSpoofedVolumeSerials(IEnumerable<string> originalSerials)
        {
            var spoofed = originalSerials.Select(s =>
            {
                var match = Regex.Match(s, @"^([A-Z])::", RegexOptions.IgnoreCase);
                if (match.Success)
                    return $"{match.Groups[1].Value}::{SpoofGenerator.GenerateRandomHex(8)}";
                return s;
            });
            return string.Join(", ", spoofed);
        }

        private string GetSpoofedCpuIdentifier()
        {
            var originals = HardwareReader.GetCpuIdentifiers().ToList();
            var count = originals.Count > 0 ? originals.Count : 1;
            return string.Join(", ", Enumerable.Range(0, count).Select(_ => SpoofGenerator.GenerateComponentSerial("CPU", 16)));
        }

        private string GetSpoofedGpuIdentifier()
        {
            var originals = HardwareReader.GetGpuIdentifiers().ToList();
            var count = originals.Count > 0 ? originals.Count : 1;
            return string.Join(", ", Enumerable.Range(0, count).Select(_ => SpoofGenerator.GenerateComponentSerial("GPU", 16)));
        }

        private string GetSpoofedDiskSerials()
        {
            var originals = HardwareReader.GetDiskSerials().ToList();
            var count = originals.Count > 0 ? originals.Count : 1;
            return string.Join(", ", Enumerable.Range(0, count).Select(_ => SpoofGenerator.GenerateComponentSerial("DSK", 16)));
        }

        private string GetSpoofedMonitorSerials()
        {
            var originals = HardwareReader.GetMonitorSerials().ToList();
            var count = originals.Count > 0 ? originals.Count : 1;
            return string.Join(", ", Enumerable.Range(0, count).Select(_ => SpoofGenerator.GenerateComponentSerial("MON", 12)));
        }

        private string GetSpoofedRamSerials()
        {
            var originals = HardwareReader.GetRamSerials().ToList();

            if (originals.Count > 0)
            {
                return string.Join(", ", originals.Select(s => SpoofGenerator.GetStylePreservedRandom(s)));
            }
            else
            {
                int count = HardwareReader.Rng.Next(0, 2) == 0 ? 2 : 4;
                return string.Join(", ", Enumerable.Range(0, count).Select(_ => SpoofGenerator.GenerateRandomHex(8)));
            }
        }
    }
}
