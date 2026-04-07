using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace UmbrellaCore.Hardware
{
    /// <summary>
    /// Reads hardware identifiers from WMI, registry, network interfaces, and system APIs.
    /// </summary>
    public static class HardwareReader
    {
        public static Random Rng { get; } = new Random();

        /// <summary>
        /// Get disk serial numbers from Win32_DiskDrive.
        /// </summary>
        public static IEnumerable<string> GetDiskSerials()
        {
            var list = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive");
                foreach (ManagementObject obj in searcher.Get()) list.Add(obj["SerialNumber"]?.ToString()?.Trim() ?? "");
            }
            catch { }
            return list;
        }

        /// <summary>
        /// Get CPU identifiers (ProcessorId) from Win32_Processor, with registry fallback.
        /// </summary>
        public static IEnumerable<string> GetCpuIdentifiers()
        {
            var list = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var value = obj["ProcessorId"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value)) list.Add(value);
                }
            }
            catch { }

            // Fallback: registry (works on Win11 where WMI may be restricted)
            if (list.Count == 0)
            {
                var processorId = ReadRegistryValue(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorId");
                if (!string.IsNullOrWhiteSpace(processorId))
                {
                    list.Add(processorId);
                }
            }

            if (list.Count == 0) list.Add("BFEBFBFF000906A0");
            return list;
        }

        /// <summary>
        /// Get GPU identifiers (PNPDeviceID) from Win32_VideoController.
        /// </summary>
        public static IEnumerable<string> GetGpuIdentifiers()
        {
            var list = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var value = obj["PNPDeviceID"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(value)) list.Add(value);
                }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// Get volume serials for all fixed drives.
        /// </summary>
        public static IEnumerable<string> GetVolumeSerials()
        {
            var list = new List<string>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        var serial = GetVolumeSerial(drive.Name.Substring(0, 1));
                        list.Add($"{drive.Name.Substring(0, 1)}::{serial}");
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// Get volume serial for a specific drive letter (without colon).
        /// </summary>
        public static string GetVolumeSerial(string driveLetter)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID = '{driveLetter}:'");
                foreach (ManagementObject obj in searcher.Get())
                    return obj["VolumeSerialNumber"]?.ToString() ?? "00000000";
            }
            catch { }
            return "00000000";
        }

        /// <summary>
        /// Get monitor serials from WmiMonitorID (EDID), with SMBIOS and Win32_DesktopMonitor fallbacks.
        /// </summary>
        public static IEnumerable<string> GetMonitorSerials()
        {
            var list = new List<string>();

            // Primary: WmiMonitorID via root\WMI (EDID serial)
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT SerialNumberID FROM WmiMonitorID");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["SerialNumberID"] is ushort[] serialArray)
                    {
                        var serial = new string(serialArray.Where(c => c > 0).Select(c => (char)c).ToArray()).Trim();
                        if (!string.IsNullOrEmpty(serial) && serial != "Not Available" && serial != "None")
                            list.Add(serial);
                    }
                    else
                    {
                        var raw = obj["SerialNumberID"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(raw)) list.Add(raw);
                    }
                }
            }
            catch { }

            // Fallback: native SMBIOS Type 7 (Display Device / Cache)
            if (list.Count == 0)
            {
                var nativeRaw = NativeSmbiosReader.GetRawSmbiosData();
                if (nativeRaw != null)
                {
                    var nativeSerials = SmbiosParser.ParseMonitorSerialsFromRawData(nativeRaw);
                    list.AddRange(nativeSerials);
                }
            }

            // Last fallback: Win32 raw SMBIOS (legacy path)
            if (list.Count == 0)
                list.AddRange(SmbiosParser.GetMonitorSerialsSmbios());

            // Last fallback: Win32_DesktopMonitor
            if (list.Count == 0)
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT SerialNumberID FROM Win32_DesktopMonitor");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var serial = obj["SerialNumberID"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(serial)) list.Add(serial);
                    }
                }
                catch { }
            }

            if (list.Count == 0) list.Add("N/A");
            return list;
        }

        /// <summary>
        /// Get MAC addresses of all non-loopback network interfaces.
        /// </summary>
        public static IEnumerable<string> GetMacs() => NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(nic => nic.GetPhysicalAddress().ToString());

        /// <summary>
        /// Get ARP cache entry count.
        /// </summary>
        public static int GetArpCacheCount()
        {
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "arp",
                        Arguments = "-a",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output.Split('\n').Length;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Get RAM serials. Tries multiple WMI classes with PartNumber/Capacity fallback.
        /// </summary>
        public static IEnumerable<string> GetRamSerials()
        {
            var list = new List<string>();

            // Try Win32_PhysicalMemory first with PartNumber+Capacity fallback
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber, PartNumber, Capacity FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var serial = obj["SerialNumber"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(serial) && serial != "00000001" && !serial.Equals("None", StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(serial);
                    }
                    else
                    {
                        var part = obj["PartNumber"]?.ToString()?.Trim() ?? "";
                        var cap = obj["Capacity"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(cap))
                        {
                            long capMb = long.Parse(cap) / (1024 * 1024);
                            list.Add(string.IsNullOrEmpty(part) ? $"{capMb}MB" : $"{part}-{capMb}MB");
                        }
                        else if (!string.IsNullOrEmpty(part))
                        {
                            list.Add(part);
                        }
                    }
                }
            }
            catch { }

            // Try MemoryChip WMI class (root\wmi)
            if (list.Count == 0)
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT SerialNumber FROM MemoryChip");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var serial = obj["SerialNumber"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(serial) && serial != "00000001" && !serial.Equals("None", StringComparison.OrdinalIgnoreCase))
                            list.Add(serial);
                    }
                }
                catch { }
            }

            // Fallback: native SMBIOS Type 17 (works on Win11)
            if (list.Count == 0)
            {
                var nativeRaw = NativeSmbiosReader.GetRawSmbiosData();
                if (nativeRaw != null)
                {
                    var nativeSerials = SmbiosParser.GetRamSerialsSmbios(nativeRaw);
                    list.AddRange(nativeSerials.Where(s => !s.Equals("00000001") && !s.Equals("None", StringComparison.OrdinalIgnoreCase)));
                }
            }

            // Last resort - MSStorageDriver_PhysicalMedia
            if (list.Count == 0)
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT SerialNumber FROM MSStorageDriver_PhysicalMedia");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var serial = obj["SerialNumber"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(serial) && serial.Length > 4)
                            list.Add(serial);
                    }
                }
                catch { }
            }

            return list;
        }

        /// <summary>
        /// Query a single WMI property from a class.
        /// </summary>
        public static string? QueryWmi(string queryClass, string property, string scope = "root\\CIMV2")
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(scope, $"SELECT {property} FROM {queryClass}");
                foreach (ManagementObject obj in searcher.Get()) return obj[property]?.ToString()?.Trim() ?? "";
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Read a registry value from HKLM.
        /// </summary>
        public static string ReadRegistryValue(string keyPath, string valueName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                return key?.GetValue(valueName)?.ToString() ?? "";
            }
            catch { return ""; }
        }
    }
}
