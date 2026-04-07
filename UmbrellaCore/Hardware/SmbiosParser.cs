using System;
using System.Collections.Generic;
using System.Management;
using System.Text;

namespace UmbrellaCore.Hardware
{
    /// <summary>
    /// Raw SMBIOS table parsing utilities.
    /// Parses MSSMBios_RawSMBiosTables and extracts data from SMBIOS structure types.
    /// </summary>
    public static class SmbiosParser
    {
        /// <summary>
        /// Fetches raw SMBIOS data. Tries WMI first, then falls back to native API.
        /// WMI may fail on Windows 11 due to VBS/HVCI restrictions.
        /// </summary>
        public static byte[]? GetRawSmbiosData()
        {
            // Primary: WMI MSSMBios_RawSMBiosTables
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT SMBiosData FROM MSSMBios_RawSMBiosTables");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["SMBiosData"] is byte[] raw) return raw;
                }
            }
            catch { }

            // Fallback: native GetSystemFirmwareTable (works on Win11 with VBS)
            return NativeSmbiosReader.GetRawSmbiosData();
        }

        /// <summary>
        /// Parse a string from the SMBIOS string table at the given index.
        /// </summary>
        public static string? GetSmbiosString(byte[] data, int stringsStart, int strIndex)
        {
            if (strIndex <= 0) return null;
            int pos = stringsStart;
            for (int i = 1; i < strIndex; i++)
            {
                while (pos < data.Length && data[pos] != 0) pos++;
                pos++;
                if (pos >= data.Length) return null;
            }
            int end = pos;
            while (end < data.Length && data[end] != 0) end++;
            if (end == pos) return null;
            return Encoding.ASCII.GetString(data, pos, end - pos);
        }

        /// <summary>
        /// Get RAM serials from raw SMBIOS Type 17 (Memory Device) structures.
        /// </summary>
        public static IEnumerable<string> GetRamSerialsSmbios(byte[] data)
        {
            var list = new List<string>();
            int pos = 8;
            while (pos + 4 <= data.Length)
            {
                byte type = data[pos];
                int length = data[pos + 1];
                if (length < 4 || pos + length > data.Length) break;

                if (type == 17 && length >= 0x1B)
                {
                    ushort handle = (ushort)(data[pos + 2] | (data[pos + 3] << 8));
                    int serialIndex = (length > 0x1A) ? data[pos + 0x1A] : (byte)0;
                    if (serialIndex > 0)
                    {
                        int stringsStart = pos + length;
                        string? serial = GetSmbiosString(data, stringsStart, serialIndex);
                        if (!string.IsNullOrEmpty(serial) && serial != "None" && serial != "Not Specified" && serial != "<OUT OF SPEC>")
                            list.Add(serial);
                    }
                }

                int end = pos + length;
                while (end + 1 < data.Length)
                {
                    if (data[end] == 0 && data[end + 1] == 0) { end += 2; break; }
                    end++;
                }
                pos = end;
                if (type == 127) break;
            }
            return list;
        }

        /// <summary>
        /// Get monitor serials from raw SMBIOS Type 7 (Display Device) structures.
        /// </summary>
        public static IEnumerable<string> GetMonitorSerialsSmbios()
        {
            var raw = GetRawSmbiosData();
            return raw != null ? ParseMonitorSerialsFromRawData(raw) : Array.Empty<string>();
        }

        /// <summary>
        /// Parse monitor serials from pre-loaded raw SMBIOS data.
        /// Used when the caller already has the SMBIOS buffer (e.g. from NativeSmbiosReader).
        /// </summary>
        public static IEnumerable<string> ParseMonitorSerialsFromRawData(byte[] raw)
        {
            var list = new List<string>();
            int pos = 8;
            while (pos + 4 <= raw.Length)
            {
                byte type = raw[pos];
                int length = raw[pos + 1];
                if (length < 4 || pos + length > raw.Length) break;

                if (type == 7 && length >= 25)
                {
                    int stringsStart = pos + length;
                    for (int i = 1; i <= 4; i++)
                    {
                        string? s = GetSmbiosString(raw, stringsStart, i);
                        if (!string.IsNullOrEmpty(s) && s != "None" && s != "Not Specified" &&
                            s.Length > 3 && !s.Equals("LCD Monitor", StringComparison.OrdinalIgnoreCase))
                            list.Add(s);
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
            return list;
        }

        /// <summary>
        /// Get BIOS serial for detection. Tries SMBIOS Type 0, then WMI fallback.
        /// </summary>
        public static string? GetBiosSerialDetection()
        {
            var raw = GetRawSmbiosData();
            if (raw != null)
            {
                int pos = 8;
                while (pos + 4 <= raw.Length)
                {
                    byte type = raw[pos];
                    int length = raw[pos + 1];
                    if (length < 4 || pos + length > raw.Length) break;

                    if (type == 0 && length >= 6)
                    {
                        int vendorIndex = raw[pos + 4];
                        int versionIndex = raw[pos + 5];
                        int stringsStart = pos + length;

                        if (vendorIndex > 0)
                        {
                            string? s = GetSmbiosString(raw, stringsStart, vendorIndex);
                            if (!string.IsNullOrEmpty(s) && s != "None" && !s.Equals("Not Specified", StringComparison.OrdinalIgnoreCase))
                                return s;
                        }
                        if (versionIndex > 0)
                        {
                            string? s = GetSmbiosString(raw, stringsStart, versionIndex);
                            if (!string.IsNullOrEmpty(s) && s != "None" && !s.Equals("Not Specified", StringComparison.OrdinalIgnoreCase))
                                return s;
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

            var serial = QueryWmi("Win32_BIOS", "SerialNumber")?.Trim();
            if (!string.IsNullOrEmpty(serial) && serial != "None" && serial != "Default String")
                return serial;

            return null;
        }

        /// <summary>
        /// Get a spoofable BIOS serial from SMBIOS Type 0, then WMI fallback.
        /// </summary>
        public static string? GetSpoofableBiosSerial()
        {
            var raw = GetRawSmbiosData();
            if (raw != null)
            {
                int pos = 8;
                while (pos + 4 <= raw.Length)
                {
                    byte type = raw[pos];
                    int length = raw[pos + 1];
                    if (length < 4 || pos + length > raw.Length) break;

                    if (type == 0 && length >= 6)
                    {
                        int vendorIndex = raw[pos + 4];
                        if (vendorIndex > 0)
                        {
                            int stringsStart = pos + length;
                            string? s = GetSmbiosString(raw, stringsStart, vendorIndex);
                            if (!string.IsNullOrEmpty(s) && s != "None" && !s.Equals("Not Specified", StringComparison.OrdinalIgnoreCase))
                                return s;
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

            var serial = QueryWmi("Win32_BIOS", "SerialNumber")?.Trim();
            if (!string.IsNullOrEmpty(serial) && serial != "None" && serial != "Default String")
                return serial;

            return null;
        }

        private static string? QueryWmi(string queryClass, string property, string scope = "root\\CIMV2")
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(scope, $"SELECT {property} FROM {queryClass}");
                foreach (ManagementObject obj in searcher.Get()) return obj[property]?.ToString()?.Trim() ?? "";
            }
            catch { }
            return "";
        }
    }
}
