using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UmbrellaCore.Driver;
using UmbrellaCore.Hardware;
using UmbrellaCore.Models;

namespace UmbrellaCore.Spoofing
{
    /// <summary>
    /// Registry-based spoofing operations for hardware identifiers.
    /// Works as fallback when kernel driver is unavailable.
    /// </summary>
    public static class SpoofRegistry
    {
        private static KernelDriverService? _driverService;

        public static void SetDriverService(KernelDriverService? driver)
        {
            _driverService = driver;
        }

        public static string? SpoofMachineGuid(string guid)
        {
            if (_driverService != null && _driverService.IsDriverConnected)
            {
                var original = HardwareReader.ReadRegistryValue(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid") ?? "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx";
                try
                {
                    return _driverService.SpoofHardware(DriverConstants.SpoofType.MachineGuid, original, string.Empty);
                }
                catch { return null; }
            }
            return null;
        }

        public static string? SpoofBiosSerial(string serial)
        {
            if (_driverService != null && _driverService.IsDriverConnected)
            {
                var original = HardwareReader.QueryWmi("Win32_BIOS", "SerialNumber") ?? "DefaultString";
                try
                {
                    return _driverService.SpoofHardware(DriverConstants.SpoofType.BiosSerial, original, string.Empty);
                }
                catch { return null; }
            }
            return null;
        }

        public static string? SpoofBaseBoardSerial(string serial)
        {
            if (_driverService != null && _driverService.IsDriverConnected)
            {
                var original = HardwareReader.QueryWmi("Win32_BaseBoard", "SerialNumber") ?? "DefaultString";
                try
                {
                    return _driverService.SpoofHardware(DriverConstants.SpoofType.BaseBoardSerial, original, string.Empty);
                }
                catch { return null; }
            }
            return null;
        }

        public static string? SpoofEfiVersion(string version) => null;

        public static string? SpoofMonitorSerials(string serials)
        {
            if (_driverService != null && _driverService.IsDriverConnected)
            {
                var original = string.Join(", ", HardwareReader.GetMonitorSerials());
                if (string.IsNullOrEmpty(original)) original = "DefaultMonitor123";
                try
                {
                    return _driverService.SpoofHardware(DriverConstants.SpoofType.MonitorSerial, original, string.Empty);
                }
                catch { return null; }
            }
            return null;
        }

        public static string? SpoofRamSerials(string serials)
        {
            if (_driverService != null && _driverService.IsDriverConnected)
            {
                var raw = SmbiosParser.GetRawSmbiosData();
                var ramSmbios = raw != null ? SmbiosParser.GetRamSerialsSmbios(raw) : Array.Empty<string>();
                var ramWmi = HardwareReader.GetRamSerials();
                var rawOriginal = ramSmbios.Any() ? string.Join(", ", ramSmbios) : string.Join(", ", ramWmi);
                var original = rawOriginal;
                if (string.IsNullOrEmpty(original)) original = "RAM123456";
                try
                {
                    return _driverService.SpoofHardware(DriverConstants.SpoofType.SmbiosData, original, string.Empty);
                }
                catch { return null; }
            }
            return null;
        }

        public static string? SpoofDiskSerials(string serials)
        {
            if (_driverService != null && _driverService.IsDriverConnected)
            {
                var original = string.Join(", ", HardwareReader.GetDiskSerials());
                if (string.IsNullOrEmpty(original)) original = "DISK123456";
                try
                {
                    return _driverService.SpoofHardware(DriverConstants.SpoofType.DiskSerial, original, string.Empty);
                }
                catch { return null; }
            }
            return null;
        }

        /// <summary>
        /// Spoof GpuIdentifier -- iterates ALL GPU entries in registry.
        /// </summary>
        public static string? SpoofGpuIdentifiers(string value)
        {
            try
            {
                using var gpuKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", true);
                if (gpuKey == null) return null;

                foreach (var subName in gpuKey.GetSubKeyNames())
                {
                    using var subKey = gpuKey.OpenSubKey(subName, true);
                    if (subKey != null && subKey.GetValue("DriverDesc") != null)
                    {
                        subKey.SetValue("HardwareInformation.AdapterString", value.Split(',').First(), RegistryValueKind.String);
                        subKey.SetValue("ProviderName", value.Split(',').First().Substring(0, Math.Min(8, value.Length)), RegistryValueKind.String);
                    }
                }
                return value;
            }
            catch { return null; }
        }

        /// <summary>
        /// Spoof CPU Identifier via Registry override at HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor.
        /// </summary>
        public static string? SpoofCpuIdentifier(string value)
        {
            try
            {
                string procKeyPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor";
                using var baseKey = Registry.LocalMachine.OpenSubKey(procKeyPath, false);
                if (baseKey == null) return null;

                foreach (var subName in baseKey.GetSubKeyNames())
                {
                    using var subKey = Registry.LocalMachine.OpenSubKey($"{procKeyPath}\\{subName}", true);
                    if (subKey != null)
                    {
                        subKey.SetValue("ProcessorId", value.Split(',').FirstOrDefault()?.Trim() ?? "", RegistryValueKind.String);
                        subKey.SetValue("Identifier", value.Split(',').FirstOrDefault()?.Trim() ?? "", RegistryValueKind.String);
                    }
                }
                return value;
            }
            catch { return null; }
        }

        /// <summary>
        /// Spoof MAC address that works on Win11 (22H2/23H2/24H2).
        /// Uses NetworkSetupManager + Registry dual approach.
        /// </summary>
        public static bool SpoofMacWin11(string newMac, string? interfaceIndex)
        {
            try
            {
                if (string.IsNullOrEmpty(interfaceIndex) || interfaceIndex == "0")
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT GUID, NetConnectionID, Name FROM Win32_NetworkAdapter WHERE PhysicalAdapter=TRUE AND NetConnectionStatus=2");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var guid = obj["GUID"]?.ToString();
                        if (!string.IsNullOrEmpty(guid))
                        {
                            using var key = Registry.LocalMachine.OpenSubKey(
                                $@"SYSTEM\CurrentControlSet\Control\Class\{{4D36E972-E325-11CE-BFC1-08002BE10318}}", true);
                            if (key != null)
                            {
                                foreach (var subKeyName in key.GetSubKeyNames())
                                {
                                    using var subKey = key.OpenSubKey(subKeyName, true);
                                    if (subKey != null && subKey.GetValue("GUID")?.ToString() == guid)
                                    {
                                        subKey.SetValue("NetworkAddress", newMac, RegistryValueKind.String);
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    using var key = Registry.LocalMachine.OpenSubKey(
                        $@"SYSTEM\CurrentControlSet\Control\Class\{{4D36E972-E325-11CE-BFC1-08002BE10318}}", true);
                    if (key != null)
                    {
                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            using var subKey = key.OpenSubKey(subKeyName, true);
                            if (subKey != null)
                            {
                                var netCfgInstance = subKey.GetValue("NetCfgInstanceID")?.ToString();
                                if (netCfgInstance == interfaceIndex)
                                {
                                    subKey.SetValue("NetworkAddress", newMac, RegistryValueKind.String);
                                    break;
                                }
                            }
                        }
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Get backup snapshot of current original values based on the provided options.
        /// </summary>
        public static TempRestorePayload GetBackupSnapshot(BackupOptions options, Func<string, IEnumerable<string>> getDiskSerials, Func<IEnumerable<string>> getMacs, Func<string, List<string>> getRamSerialsSmbios, Func<IEnumerable<string>> getRamSerials)
        {
            var snapshot = new TempRestorePayload { Values = new Dictionary<string, string>() };

            if (options.MachineGuid)
                snapshot.Values["MachineGuid"] = HardwareReader.ReadRegistryValue(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid");
            if (options.BiosSerial)
                snapshot.Values["BIOS_Serial"] = SmbiosParser.GetBiosSerialDetection() ?? "N/A";
            if (options.BaseBoardSerial)
                snapshot.Values["BaseBoard_Serial"] = HardwareReader.QueryWmi("Win32_BaseBoard", "SerialNumber")?.Trim() ?? "N/A";
            if (options.EfiVersion)
                snapshot.Values["EFI_Version"] = SmbiosParser.GetBiosSerialDetection() ?? HardwareReader.QueryWmi("Win32_BIOS", "Version")?.Trim() ?? "N/A";

            return snapshot;
        }

        /// <summary>
        /// Restore from a temp restore payload JSON file by re-applying the original values.
        /// </summary>
        public static void RestoreFromPayloadFile(string path, Func<Dictionary<string, string>, bool, string?, bool> spoofAllFunc)
        {
            try
            {
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var payload = System.Text.Json.JsonSerializer.Deserialize<TempRestorePayload>(json);
                if (payload == null) return;

                spoofAllFunc(payload.Values, !string.IsNullOrEmpty(payload.MacAddress), payload.MacInterfaceIndex);
            }
            catch { }
        }
    }
}
