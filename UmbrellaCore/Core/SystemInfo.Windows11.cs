using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using Microsoft.Win32;
using UmbrellaCore.Hardware;
using UmbrellaCore.Spoofing;

namespace UmbrellaCore
{
    /// <summary>
    /// Windows 11 specific detection methods for SystemInfo.
    /// Uses raw SMBIOS parsing where WMI returns empty/generic values.
    /// </summary>
    public partial class SystemInfo
    {
        /// <summary>
        /// Get BaseBoard serial from raw SMBIOS Type 2 structure.
        /// More reliable than Win32_BaseBoard.SerialNumber on Win11.
        /// </summary>
        private string? GetBaseBoardSerialSmbiosWin11()
        {
            var rawSmbios = SmbiosParser.GetRawSmbiosData();
            if (rawSmbios != null)
            {
                int pos = 8;
                while (pos + 4 <= rawSmbios.Length)
                {
                    byte type = rawSmbios[pos];
                    int length = rawSmbios[pos + 1];
                    if (length < 4 || pos + length > rawSmbios.Length) break;

                    if (type == 2 && length >= 9)
                    {
                        int serialIndex = rawSmbios[pos + 8];
                        if (serialIndex > 0)
                        {
                            int stringsStart = pos + length;
                            string? bSerial = SmbiosParser.GetSmbiosString(rawSmbios, stringsStart, serialIndex);
                            if (!string.IsNullOrEmpty(bSerial) && bSerial != "None" &&
                                !bSerial.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) &&
                                bSerial != "Not Specified")
                                return bSerial;
                        }
                    }

                    int end = pos + length;
                    while (end + 1 < rawSmbios.Length)
                    {
                        if (rawSmbios[end] == 0 && rawSmbios[end + 1] == 0) { end += 2; break; }
                        end++;
                    }
                    pos = end;
                    if (type == 127) break;
                }
            }

            // Fallback: original WMI approach
            var serial = HardwareReader.QueryWmi("Win32_BaseBoard", "SerialNumber")?.Trim();
            if (!string.IsNullOrEmpty(serial) && serial != "None" &&
                !serial.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase))
                return serial;

            // Second fallback
            serial = HardwareReader.QueryWmi("Win32_SystemEnclosure", "SerialNumber")?.Trim();
            if (!string.IsNullOrEmpty(serial) && serial != "None")
                return serial;

            // Native SMBIOS fallback (direct from kernel32 GetSystemFirmwareTable)
            // Useful when WMI returns empty data but native API still works
            var nativeRaw = Hardware.NativeSmbiosReader.GetRawSmbiosData();
            if (nativeRaw != null)
            {
                int pos = 8;
                while (pos + 4 <= nativeRaw.Length)
                {
                    byte type = nativeRaw[pos];
                    int length = nativeRaw[pos + 1];
                    if (length < 4 || pos + length > nativeRaw.Length) break;
                    if (type == 2 && length >= 9)
                    {
                        int serialIndex = nativeRaw[pos + 8];
                        if (serialIndex > 0)
                        {
                            int stringsStart = pos + length;
                            string? bSerial = SmbiosParser.GetSmbiosString(nativeRaw, stringsStart, serialIndex);
                            if (!string.IsNullOrEmpty(bSerial) && bSerial != "None" &&
                                !bSerial.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase))
                                return bSerial;
                        }
                    }
                    int end = pos + length;
                    while (end + 1 < nativeRaw.Length)
                    {
                        if (nativeRaw[end] == 0 && nativeRaw[end + 1] == 0) { end += 2; break; }
                        end++;
                    }
                    pos = end;
                    if (type == 127) break;
                }
            }

            // Registry fallback
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS", false);
                if (key != null)
                {
                    var v = key.GetValue("BaseBoardVersion")?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(v)) return v;
                    v = key.GetValue("BIOSVersion")?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(v) && v.Length > 4) return v;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Get BIOS/EFI version from raw SMBIOS Type 0 (BIOS Information) structure.
        /// </summary>
        private string? GetBiosVersionSmbios()
        {
            var rawSmbios = SmbiosParser.GetRawSmbiosData();
            if (rawSmbios != null)
            {
                int pos = 8;
                while (pos + 4 <= rawSmbios.Length)
                {
                    byte type = rawSmbios[pos];
                    int length = rawSmbios[pos + 1];
                    if (length < 4 || pos + length > rawSmbios.Length) break;

                    if (type == 0 && length >= 6)
                    {
                        int vendorIndex = rawSmbios[pos + 4];
                        int versionIndex = rawSmbios[pos + 5];

                        int stringsStart = pos + length;
                        string? vendor = vendorIndex > 0 ? SmbiosParser.GetSmbiosString(rawSmbios, stringsStart, vendorIndex) : null;
                        string? bVersion = versionIndex > 0 ? SmbiosParser.GetSmbiosString(rawSmbios, stringsStart, versionIndex) : null;

                        if (!string.IsNullOrEmpty(bVersion) && bVersion != "None" && bVersion != "Not Specified")
                        {
                            return !string.IsNullOrEmpty(vendor) ? $"{vendor} {bVersion}" : bVersion;
                        }
                    }

                    int end = pos + length;
                    while (end + 1 < rawSmbios.Length)
                    {
                        if (rawSmbios[end] == 0 && rawSmbios[end + 1] == 0) { end += 2; break; }
                        end++;
                    }
                    pos = end;
                    if (type == 127) break;
                }
            }

            // Fallback: WMI BIOS version
            var version = HardwareReader.QueryWmi("Win32_BIOS", "Version")?.Trim();
            if (!string.IsNullOrEmpty(version) && version != "None")
                return version;

            // Second fallback: SMBIOS BIOS version
            version = HardwareReader.QueryWmi("Win32_BIOS", "SMBIOSBIOSVersion")?.Trim();
            if (!string.IsNullOrEmpty(version) && version != "None")
                return version;

            // Registry fallback
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS", false);
                if (key != null)
                {
                    var v = key.GetValue("BIOSVersion")?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Get TPM serial number from raw SMBIOS + WMI + registry.
        /// </summary>
        private string? GetTpmSerialSmbios()
        {
            var raw = SmbiosParser.GetRawSmbiosData();
            if (raw != null)
            {
                int pos = 8;
                while (pos + 4 <= raw.Length)
                {
                    byte type = raw[pos];
                    int length = raw[pos + 1];
                    if (length < 4 || pos + length > raw.Length) break;

                    if (type == 3 && length >= 13)
                    {
                        int serialIndex = raw[pos + 0x13];
                        if (serialIndex > 0)
                        {
                            int stringsStart = pos + length;
                            string? s = SmbiosParser.GetSmbiosString(raw, stringsStart, serialIndex);
                            if (!string.IsNullOrEmpty(s) && s != "None" && s != "Not Specified" &&
                                !s.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase))
                                return $"TPM-{s}";
                        }
                    }

                    int end = pos + length;
                    while (end + 1 < raw.Length)
                    {
                        if (raw[end] == 0 && raw[end + 1] == 0) { end += 2; break; }
                        end++;
                    }
                    pos = end;
                    if (type == 127) break;
                }
            }

            // Win11: TPM ManufacturerId as serial identifier
            var mfrId = HardwareReader.QueryWmi("Win32_Tpm", "ManufacturerId", "root\\CIMV2\\Security\\MicrosoftTpm");
            if (!string.IsNullOrEmpty(mfrId) && mfrId != "None")
                return mfrId;

            // Registry: TPM FirmwareVersion
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

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TPM\WMI", false);
                if (key != null)
                {
                    var v = key.GetValue("ManufacturerId")?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Wrapper: fetches raw SMBIOS data and parses RAM serials from Type 17 structures.
        /// Tries SmbiosParser.GetRawSmbiosData() (WMI → native fallback), then native directly.
        /// </summary>
        private List<string> GetRamSerialsSmbiosFromRaw()
        {
            // Try SmbiosParser route (WMI first, then native fallback via GetRawSmbiosData)
            var rawFromParser = SmbiosParser.GetRawSmbiosData();
            if (rawFromParser != null)
            {
                var ramSmbios = SmbiosParser.GetRamSerialsSmbios(rawFromParser).ToList();
                if (ramSmbios.Count > 0) return ramSmbios;
            }

            // Direct native fallback
            var nativeRaw = Hardware.NativeSmbiosReader.GetRawSmbiosData();
            if (nativeRaw != null)
            {
                var ramSmbios = SmbiosParser.GetRamSerialsSmbios(nativeRaw).ToList();
                if (ramSmbios.Count > 0) return ramSmbios;
            }

            // SMBIOS parsing returned empty — try WMI directly as last resort
            return HardwareReader.GetRamSerials().ToList();
        }

        /// <summary>
        /// Get machine guid with style-preserved random for spoofing.
        /// </summary>
        private string GetSpoofedMachineGuid()
        {
            var original = HardwareReader.ReadRegistryValue(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid");
            if (!string.IsNullOrEmpty(original))
                return SpoofGenerator.GetStylePreservedRandom(original);
            return Guid.NewGuid().ToString();
        }
    }
}
