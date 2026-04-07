using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using UmbrellaCore.Hardware;

namespace UmbrellaCore
{
    /// <summary>
    /// Windows 10 / non-Win11 specific detection methods for SystemInfo.
    /// </summary>
    public partial class SystemInfo
    {
        /// <summary>
        /// Get RAM serials for Win10. On Win10, Win32_PhysicalMemory.SerialNumber usually works.
        /// </summary>
        private string GetRamSerialsWin10()
        {
            var list = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var serial = obj["SerialNumber"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(serial) && serial != "00000001" &&
                        !serial.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                        !serial.Equals("Not Specified", StringComparison.OrdinalIgnoreCase))
                        list.Add(serial);
                    else
                    {
                        var part = obj["PartNumber"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(part) && !part.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
                            !part.Equals("Not Specified", StringComparison.OrdinalIgnoreCase))
                            list.Add(part);
                    }
                }
            }
            catch { }
            return list.Count > 0 ? string.Join(", ", list) : "N/A";
        }

        /// <summary>
        /// Get BaseBoard serial for Win10. Win32_BaseBoard usually works here.
        /// </summary>
        private string? GetBaseBoardSerialWin10()
        {
            var serial = HardwareReader.QueryWmi("Win32_BaseBoard", "SerialNumber")?.Trim();
            if (!string.IsNullOrEmpty(serial) && serial != "None" &&
                !serial.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase))
                return serial;

            return GetBaseBoardSerialSmbiosWin11();
        }

        /// <summary>
        /// Get EFI/BIOS version for Win10.
        /// Uses SMBIOS raw data first, then WMI fallbacks.
        /// </summary>
        private string? GetEfiVersionWin10()
        {
            var version = GetBiosVersionSmbios();
            if (!string.IsNullOrEmpty(version) && version != "N/A")
                return version;

            version = HardwareReader.QueryWmi("Win32_BIOS", "Version")?.Trim();
            if (!string.IsNullOrEmpty(version) && version != "None")
                return version;

            version = HardwareReader.QueryWmi("Win32_BIOS", "SMBIOSBIOSVersion")?.Trim();
            if (!string.IsNullOrEmpty(version) && version != "None")
                return version;

            return null;
        }

        /// <summary>
        /// Get TPM identity for Win10.
        /// </summary>
        private string? GetTpmSerialWin10()
        {
            var mfrId = HardwareReader.QueryWmi("Win32_Tpm", "ManufacturerId", "root\\CIMV2\\Security\\MicrosoftTpm");
            if (!string.IsNullOrEmpty(mfrId) && mfrId != "None")
                return mfrId;

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Tpm", false);
                if (key != null)
                {
                    var v = key.GetValue("FirmwareVersion")?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { }

            return null;
        }
    }
}
