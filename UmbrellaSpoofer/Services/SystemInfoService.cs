using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace UmbrellaSpoofer.Services
{
    public class SystemInfoService
    {
        public Dictionary<string, string> GetAll()
        {
            var d = new Dictionary<string, string>();
            d["MachineGuid"] = ReadRegistryValue();
            d["BIOS_Serial"] = QueryWmi("Win32_BIOS", "SerialNumber");
            d["BaseBoard_Serial"] = QueryWmi("Win32_BaseBoard", "SerialNumber");
            d["EFI_Version"] = QueryWmi("Win32_ComputerSystem", "SystemSKUNumber");
            d["Monitor_Serials"] = string.Join(",", GetMonitorSerials());
            d["RAM_Serials"] = string.Join(",", GetRamSerials());
            d["MAC_Addresses"] = string.Join(",", GetMacs());
            d["Disk_Serials"] = string.Join(",", GetDiskSerials());
            d["GPU_Identifiers"] = string.Join(",", GetGpuIdentifiers());
            d["Volume_Serials"] = string.Join(",", GetVolumeSerials());
            d["TPM_Identity"] = QueryWmi("Win32_Tpm", "IsActivated_InitialValue") ?? "Not Available";
            d["EFI_Boot"] = QueryWmi("Win32_BootConfiguration", "Description") ?? "Not Available";
            d["ARP_Cache"] = GetArpCacheCount().ToString();
            return d;
        }

        public class BackupOptions
        {
            public bool MachineGuid { get; set; } = true;
            public bool BiosSerial { get; set; } = true;
            public bool BaseBoardSerial { get; set; } = true;
            public bool EfiVersion { get; set; } = true;
            public bool MonitorSerials { get; set; } = true;
            public bool RamSerials { get; set; } = true;
            public bool MacAddresses { get; set; } = true;
            public bool RegistryEdid { get; set; } = true;
        }

        public Dictionary<string, object> GetBackupSnapshot()
        {
            return GetBackupSnapshot(new BackupOptions());
        }

        public Dictionary<string, object> GetBackupSnapshot(BackupOptions options)
        {
            var snapshot = new Dictionary<string, object>();
            snapshot["CapturedAt"] = DateTime.UtcNow.ToString("O");
            if (options.MachineGuid) snapshot["MachineGuid"] = ReadRegistryValue();
            if (options.BiosSerial) snapshot["BIOS_Serial"] = QueryWmi("Win32_BIOS", "SerialNumber");
            if (options.BaseBoardSerial) snapshot["BaseBoard_Serial"] = QueryWmi("Win32_BaseBoard", "SerialNumber");
            if (options.EfiVersion) snapshot["EFI_Version"] = QueryWmi("Win32_ComputerSystem", "SystemSKUNumber");
            if (options.MonitorSerials) snapshot["Monitor_Serials"] = GetMonitorSerials().ToArray();
            if (options.RamSerials) snapshot["RAM_Serials"] = GetRamSerials().ToArray();
            if (options.MacAddresses) snapshot["MAC_Addresses"] = GetMacs().ToArray();
            if (options.RegistryEdid) snapshot["Monitor_Registry_EDID"] = GetMonitorEdidRegistry().ToArray();
            return snapshot;
        }

        string ReadRegistryValue()
        {
            using var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Cryptography");
            var v = key?.GetValue("MachineGuid")?.ToString();
            return v ?? "";
        }

        string QueryWmi(string scope, string prop, string? fallbackScope = null, string? fallbackProp = null)
        {
            string result = "";
            
            try
            {
                using var s = new ManagementObjectSearcher($"select {prop} from {scope}");
                foreach (ManagementObject o in s.Get())
                {
                    var v = o[prop]?.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) 
                    {
                        var cleanValue = v.Trim().Replace("\0", "").Replace(" ", "");
                        if (!string.IsNullOrWhiteSpace(cleanValue)) 
                            return cleanValue;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WMI Query failed for {scope}.{prop}: {ex.Message}");
            }
            
            if (!string.IsNullOrEmpty(fallbackScope) && !string.IsNullOrEmpty(fallbackProp))
            {
                try
                {
                    using var s = new ManagementObjectSearcher($"select {fallbackProp} from {fallbackScope}");
                    foreach (ManagementObject o in s.Get())
                    {
                        var v = o[fallbackProp]?.ToString();
                        if (!string.IsNullOrWhiteSpace(v)) 
                        {
                            var cleanValue = v.Trim().Replace("\0", "").Replace(" ", "");
                            if (!string.IsNullOrWhiteSpace(cleanValue)) 
                                return $"[Fallback] {cleanValue}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Fallback WMI Query failed for {fallbackScope}.{fallbackProp}: {ex.Message}");
                }
            }
            
            result = TryRegistryFallback(scope, prop);
            if (!string.IsNullOrEmpty(result))
                return $"[Registry] {result}";

            return $"[Not Available] Check permissions/UAC for {scope}.{prop}";
        }
        
        string TryRegistryFallback(string scope, string prop)
        {
            try
            {
                var fallbackMappings = new Dictionary<string, (string keyPath, string valueName)>
                {
                    ["Win32_BIOS.SerialNumber"] = ("HARDWARE\\DESCRIPTION\\System\\BIOS", "SystemSerialNumber"),
                    ["Win32_BaseBoard.SerialNumber"] = ("HARDWARE\\DESCRIPTION\\System\\BIOS", "BaseBoardSerialNumber"),
                    ["Win32_ComputerSystem.SystemSKUNumber"] = ("SYSTEM\\CurrentControlSet\\Control\\SystemInformation", "SystemProductName")
                };
                
                var key = $"{scope}.{prop}";
                if (fallbackMappings.TryGetValue(key, out var mapping))
                {
                    using var regKey = Registry.LocalMachine.OpenSubKey(mapping.keyPath);
                    var value = regKey?.GetValue(mapping.valueName)?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }
            catch { }
            
            return null!;
        }

        IEnumerable<string> GetMacs()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => n.GetPhysicalAddress().ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }

        IEnumerable<string> GetMonitorSerials()
        {
            var list = new List<string>();
            try
            {
                using var s = new ManagementObjectSearcher(@"root\\wmi", "select ManufacturerName,UserFriendlyName,SerialNumberID,InstanceName from WmiMonitorID");
                foreach (ManagementObject o in s.Get())
                {
                    var mfg = ReadWmiString(o["ManufacturerName"]);
                    var model = ReadWmiString(o["UserFriendlyName"]);
                    var serial = ReadWmiString(o["SerialNumberID"]);
                    var inst = o["InstanceName"]?.ToString()?.Trim() ?? "";
                    var entry = FormatMonitorDisplay(mfg, model, serial, inst);
                    if (!string.IsNullOrWhiteSpace(entry)) list.Add(entry);
                }
            }
            catch { }
            if (list.Count == 0)
            {
                foreach (var v in GetMonitorDisplayNamesFromRegistry())
                {
                    if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
                }
            }
            if (list.Count == 0)
            {
                foreach (var v in GetMonitorDescriptorStrings(0xFC))
                {
                    if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
                }
            }
            if (list.Count == 0)
            {
                foreach (var v in GetMonitorDescriptorStrings(0xFF))
                {
                    if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
                }
            }
            if (list.Count == 0)
            {
                foreach (var v in GetMonitorFallbackIds())
                {
                    if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
                }
            }
            if (list.Count == 0) list.Add("Unavailable");
            return list.Distinct().ToList();
        }

        IEnumerable<string> GetMonitorDescriptorStrings(byte descriptorType)
        {
            var list = new List<string>();
            try
            {
                using var s = new ManagementObjectSearcher(@"root\\wmi", "select DescriptorType,DescriptorData from WmiMonitorDescriptor");
                foreach (ManagementObject o in s.Get())
                {
                    if (o["DescriptorType"] is byte t && t == descriptorType)
                    {
                        var data = o["DescriptorData"] as byte[];
                        var str = ReadDescriptorString(data);
                        if (!string.IsNullOrWhiteSpace(str)) list.Add(str);
                    }
                }
            }
            catch { }
            return list;
        }

        IEnumerable<string> GetMonitorFallbackIds()
        {
            var list = new List<string>();
            try
            {
                using var s = new ManagementObjectSearcher("select Name,PNPDeviceID from Win32_DesktopMonitor");
                foreach (ManagementObject o in s.Get())
                {
                    var name = o["Name"]?.ToString()?.Trim() ?? "";
                    var id = o["PNPDeviceID"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
                    else if (!string.IsNullOrWhiteSpace(id)) list.Add(id);
                }
            }
            catch { }
            return list;
        }

        static string FormatMonitorDisplay(string mfg, string model, string serial, string fallback)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(mfg)) parts.Add(mfg);
            if (!string.IsNullOrWhiteSpace(model)) parts.Add(model);
            var label = string.Join(" ", parts);
            if (string.IsNullOrWhiteSpace(label)) label = fallback;
            if (!string.IsNullOrWhiteSpace(serial) && !string.IsNullOrWhiteSpace(label))
                return $"{label} - {serial}";
            if (!string.IsNullOrWhiteSpace(serial)) return serial;
            return label ?? "";
        }

        IEnumerable<string> GetMonitorDisplayNamesFromRegistry()
        {
            var list = new List<string>();
            try
            {
                using var display = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY");
                if (display == null) return list;
                foreach (var mfgId in display.GetSubKeyNames())
                {
                    using var mfgKey = display.OpenSubKey(mfgId);
                    if (mfgKey == null) continue;
                    foreach (var instId in mfgKey.GetSubKeyNames())
                    {
                        using var instKey = mfgKey.OpenSubKey(instId);
                        if (instKey == null) continue;
                        using var devParams = instKey.OpenSubKey("Device Parameters");
                        var edid = devParams?.GetValue("EDID") as byte[];
                        var name = ReadEdidName(edid);
                        var serial = ReadEdidSerial(edid);
                        var entry = FormatMonitorDisplay("", name, serial, "");
                        if (!string.IsNullOrWhiteSpace(entry)) list.Add(entry);
                    }
                }
            }
            catch { }
            return list;
        }

        IEnumerable<Dictionary<string, string>> GetMonitorEdidRegistry()
        {
            var list = new List<Dictionary<string, string>>();
            try
            {
                using var display = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY");
                if (display == null) return list;
                foreach (var mfgId in display.GetSubKeyNames())
                {
                    using var mfgKey = display.OpenSubKey(mfgId);
                    if (mfgKey == null) continue;
                    foreach (var instId in mfgKey.GetSubKeyNames())
                    {
                        using var instKey = mfgKey.OpenSubKey(instId);
                        if (instKey == null) continue;
                        using var devParams = instKey.OpenSubKey("Device Parameters");
                        var edid = devParams?.GetValue("EDID") as byte[];
                        if (edid == null || edid.Length == 0) continue;
                        var entry = new Dictionary<string, string>();
                        entry["Path"] = $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{mfgId}\{instId}\Device Parameters";
                        entry["EDID_Base64"] = Convert.ToBase64String(edid);
                        list.Add(entry);
                    }
                }
            }
            catch { }
            return list;
        }

        static string ReadEdidSerial(byte[]? edid)
        {
            if (edid == null || edid.Length < 128) return "";
            for (var i = 54; i + 18 <= edid.Length; i += 18)
            {
                if (edid[i] == 0x00 && edid[i + 1] == 0x00 && edid[i + 2] == 0x00 && edid[i + 3] == 0xFF)
                {
                    var sb = new StringBuilder();
                    for (var j = i + 5; j < i + 18; j++)
                    {
                        var b = edid[j];
                        if (b == 0x0A) break;
                        if (b < 32) continue;
                        sb.Append((char)b);
                    }
                    var s = sb.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            return "";
        }

        static string ReadEdidName(byte[]? edid)
        {
            if (edid == null || edid.Length < 128) return "";
            for (var i = 54; i + 18 <= edid.Length; i += 18)
            {
                if (edid[i] == 0x00 && edid[i + 1] == 0x00 && edid[i + 2] == 0x00 && edid[i + 3] == 0xFC)
                {
                    var sb = new StringBuilder();
                    for (var j = i + 5; j < i + 18; j++)
                    {
                        var b = edid[j];
                        if (b == 0x0A) break;
                        if (b < 32) continue;
                        sb.Append((char)b);
                    }
                    var s = sb.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            return "";
        }

        static string ReadDescriptorString(byte[]? data)
        {
            if (data == null || data.Length == 0) return "";
            var sb = new StringBuilder();
            foreach (var b in data)
            {
                if (b == 0x0A || b == 0x00) break;
                if (b < 32) continue;
                sb.Append((char)b);
            }
            return sb.ToString().Trim();
        }

        static string ReadWmiString(object? value)
        {
            if (value is ushort[] us)
            {
                var sb = new StringBuilder();
                foreach (var c in us)
                {
                    if (c == 0 || c == 0xFFFF) break;
                    if (c < 32) continue;
                    sb.Append((char)c);
                }
                return sb.ToString().Trim();
            }
            if (value is byte[] bs)
            {
                var sb = new StringBuilder();
                foreach (var b in bs)
                {
                    if (b == 0) break;
                    if (b < 32) continue;
                    sb.Append((char)b);
                }
                return sb.ToString().Trim();
            }
            return value?.ToString()?.Trim() ?? "";
        }

        IEnumerable<string> GetRamSerials()
        {
            var list = new List<string>();
            try
            {
                using var s = new ManagementObjectSearcher("select SerialNumber from Win32_PhysicalMemory");
                foreach (ManagementObject o in s.Get())
                {
                    var v = o["SerialNumber"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) list.Add(v!);
                }
            }
            catch { }
            return list;
        }

        public List<NetworkAdapter> GetNetworkAdapters()
        {
            var adapters = new List<NetworkAdapter>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionStatus = 2");
                foreach (ManagementObject o in s.Get())
                {
                    var name = o["Name"]?.ToString() ?? "";
                    var description = o["Description"]?.ToString() ?? "";
                    var macAddress = o["MACAddress"]?.ToString() ?? "";
                    var interfaceIndex = o["InterfaceIndex"]?.ToString() ?? "";
                    
                    if (!string.IsNullOrEmpty(macAddress) && !string.IsNullOrEmpty(name))
                    {
                        adapters.Add(new NetworkAdapter
                        {
                            Name = name,
                            Description = description,
                            MacAddress = macAddress,
                            InterfaceIndex = interfaceIndex
                        });
                    }
                }
            }
            catch
            {
            }
            return adapters;
        }

        public bool SpoofMachineGuid(string newGuid)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey("SOFTWARE\\Microsoft\\Cryptography");
                key.SetValue("MachineGuid", newGuid, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof MachineGuid: {ex.Message}");
                return false;
            }
        }

        public bool SpoofBiosSerial(string newSerial)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS");
                key.SetValue("SystemSerialNumber", newSerial, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof BIOS Serial: {ex.Message}");
                return false;
            }
        }

        public bool SpoofBaseBoardSerial(string newSerial)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS");
                key.SetValue("BaseBoardSerialNumber", newSerial, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof BaseBoard Serial: {ex.Message}");
                return false;
            }
        }

        public bool SpoofEfiVersion(string newVersion)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey("SYSTEM\\CurrentControlSet\\Control\\SystemInformation");
                key.SetValue("SystemProductName", newVersion, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof EFI Version: {ex.Message}");
                return false;
            }
        }

        public bool SpoofMacAddress(string interfaceIndex, string newMac)
        {
            try
            {
                var adapterPath = $"SYSTEM\\CurrentControlSet\\Control\\Class\\{{4D36E972-E325-11CE-BFC1-08002BE10318}}\\{interfaceIndex.PadLeft(4, '0')}";
                
                using var key = Registry.LocalMachine.OpenSubKey(adapterPath, true);
                if (key != null)
                {
                    var originalMac = key.GetValue("NetworkAddress")?.ToString();
                    if (!string.IsNullOrEmpty(originalMac))
                    {
                        key.SetValue("OriginalNetworkAddress", originalMac, RegistryValueKind.String);
                    }
                    
                    key.SetValue("NetworkAddress", newMac, RegistryValueKind.String);
                    return true;
                }
                
                System.Diagnostics.Debug.WriteLine($"Could not find network adapter registry key for interface {interfaceIndex}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof MAC Address: {ex.Message}");
                return false;
            }
        }

        public bool SpoofMonitorSerials(string newSerial)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey("SYSTEM\\CurrentControlSet\\Enum\\DISPLAY\\DefaultMonitor");
                key.SetValue("SerialNumber", newSerial, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof Monitor Serial: {ex.Message}");
                return false;
            }
        }

        public bool SpoofRamSerials(string newSerial)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey("HARDWARE\\DESCRIPTION\\System\\Memory");
                key.SetValue("MemorySerial", newSerial, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof RAM Serial: {ex.Message}");
                return false;
            }
        }

        public bool SpoofDiskSerials(string newSerial)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey("SYSTEM\\CurrentControlSet\\Services\\Disk\\Enum");
                key.SetValue("DiskSerial", newSerial, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof Disk Serial: {ex.Message}");
                return false;
            }
        }

        public bool SpoofGpuIdentifiers(string newIdentifier)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey("SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000");
                key.SetValue("AdapterString", newIdentifier, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof GPU Identifier: {ex.Message}");
                return false;
            }
        }

        public bool SpoofVolumeSerials(string newSerial)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey("SYSTEM\\MountedDevices");
                key.SetValue("VolumeSerialOverride", newSerial, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof Volume Serial: {ex.Message}");
                return false;
            }
        }

        public bool SpoofTpmIdentity(string newIdentity)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey("SYSTEM\\CurrentControlSet\\Services\\TPM\\WMI");
                key.SetValue("TpmIdentity", newIdentity, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof TPM Identity: {ex.Message}");
                return false;
            }
        }

        public bool SpoofEfiBoot(string newBootConfig)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey("SYSTEM\\CurrentControlSet\\Control\\FirmwareResources");
                key.SetValue("BootConfiguration", newBootConfig, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof EFI Boot: {ex.Message}");
                return false;
            }
        }

        public bool SpoofArpCache(string newCacheCount)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey("SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters");
                key.SetValue("ArpCacheSize", newCacheCount, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to spoof ARP Cache: {ex.Message}");
                return false;
            }
        }

        public bool SpoofAll(Dictionary<string, string> spoofValues, bool spoofMac = false, string? macInterfaceIndex = null)
        {
            var results = new Dictionary<string, bool>();
            
            foreach (var kv in spoofValues)
            {
                bool success = false;
                switch (kv.Key)
                {
                    case "MachineGuid":
                        success = SpoofMachineGuid(kv.Value);
                        break;
                    case "BIOS_Serial":
                        success = SpoofBiosSerial(kv.Value);
                        break;
                    case "BaseBoard_Serial":
                        success = SpoofBaseBoardSerial(kv.Value);
                        break;
                    case "EFI_Version":
                        success = SpoofEfiVersion(kv.Value);
                        break;
                    case "Monitor_Serials":
                        success = SpoofMonitorSerials(kv.Value);
                        break;
                    case "RAM_Serials":
                        success = SpoofRamSerials(kv.Value);
                        break;
                    case "Disk_Serials":
                        success = SpoofDiskSerials(kv.Value);
                        break;
                    case "GPU_Identifiers":
                        success = SpoofGpuIdentifiers(kv.Value);
                        break;
                    case "Volume_Serials":
                        success = SpoofVolumeSerials(kv.Value);
                        break;
                    case "TPM_Identity":
                        success = SpoofTpmIdentity(kv.Value);
                        break;
                    case "EFI_Boot":
                        success = SpoofEfiBoot(kv.Value);
                        break;
                    case "ARP_Cache":
                        success = SpoofArpCache(kv.Value);
                        break;
                    default:
                        continue;
                }
                results[kv.Key] = success;
            }

            if (spoofMac && !string.IsNullOrEmpty(macInterfaceIndex) && spoofValues.ContainsKey("MAC_Addresses"))
            {
                results["MAC_Addresses"] = SpoofMacAddress(macInterfaceIndex, spoofValues["MAC_Addresses"]);
            }

            return results.All(r => r.Value);
        }

        public bool RestoreFromPayloadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            try
            {
                var json = File.ReadAllText(path);
                var payload = JsonSerializer.Deserialize<TempRestorePayload>(json);
                if (payload == null) return false;
                var ok = RestoreFromPayload(payload);
                try { File.Delete(path); } catch { }
                return ok;
            }
            catch
            {
                return false;
            }
        }

        public bool RestoreFromPayload(TempRestorePayload payload)
        {
            var results = new Dictionary<string, bool>();
            foreach (var kv in payload.Values)
            {
                bool success = false;
                switch (kv.Key)
                {
                    case "MachineGuid":
                        success = SpoofMachineGuid(kv.Value);
                        break;
                    case "BIOS_Serial":
                        success = SpoofBiosSerial(kv.Value);
                        break;
                    case "BaseBoard_Serial":
                        success = SpoofBaseBoardSerial(kv.Value);
                        break;
                    case "EFI_Version":
                        success = SpoofEfiVersion(kv.Value);
                        break;
                    case "Monitor_Serials":
                        success = SpoofMonitorSerials(kv.Value);
                        break;
                    case "RAM_Serials":
                        success = SpoofRamSerials(kv.Value);
                        break;
                    case "Disk_Serials":
                        success = SpoofDiskSerials(kv.Value);
                        break;
                    case "GPU_Identifiers":
                        success = SpoofGpuIdentifiers(kv.Value);
                        break;
                    case "Volume_Serials":
                        success = SpoofVolumeSerials(kv.Value);
                        break;
                    case "TPM_Identity":
                        success = SpoofTpmIdentity(kv.Value);
                        break;
                    case "EFI_Boot":
                        success = SpoofEfiBoot(kv.Value);
                        break;
                    case "ARP_Cache":
                        success = SpoofArpCache(kv.Value);
                        break;
                    default:
                        continue;
                }
                results[kv.Key] = success;
            }
            if (!string.IsNullOrWhiteSpace(payload.MacInterfaceIndex) && !string.IsNullOrWhiteSpace(payload.MacAddress))
            {
                results["MAC_Addresses"] = SpoofMacAddress(payload.MacInterfaceIndex, payload.MacAddress);
            }
            if (results.Count == 0) return false;
            return results.All(r => r.Value);
        }

        IEnumerable<string> GetDiskSerials()
        {
            var list = new List<string>();
            try
            {
                using var s = new ManagementObjectSearcher("select SerialNumber,Model from Win32_DiskDrive");
                foreach (ManagementObject o in s.Get())
                {
                    var serial = o["SerialNumber"]?.ToString();
                    var model = o["Model"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(serial)) 
                        list.Add($"{model}:{serial}".Trim());
                }
            }
            catch { }
            return list;
        }

        IEnumerable<string> GetGpuIdentifiers()
        {
            var list = new List<string>();
            try
            {
                using var s = new ManagementObjectSearcher("select Name,AdapterRAM,DriverVersion from Win32_VideoController");
                foreach (ManagementObject o in s.Get())
                {
                    var name = o["Name"]?.ToString();
                    var ram = o["AdapterRAM"]?.ToString();
                    var driver = o["DriverVersion"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name) && name != "Microsoft Basic Display Driver")
                        list.Add($"{name} ({ram} bytes)".Trim());
                }
            }
            catch { }
            return list;
        }

        IEnumerable<string> GetVolumeSerials()
        {
            var list = new List<string>();
            try
            {
                using var s = new ManagementObjectSearcher("select VolumeSerialNumber,Name from Win32_LogicalDisk");
                foreach (ManagementObject o in s.Get())
                {
                    var serial = o["VolumeSerialNumber"]?.ToString();
                    var name = o["Name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(serial))
                        list.Add($"{name}:{serial}".Trim());
                }
            }
            catch { }
            return list;
        }

        int GetArpCacheCount()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Count();
            }
            catch { }
            return 0;
        }
    }

    public class NetworkAdapter
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public string InterfaceIndex { get; set; } = "";
        
        public override string ToString()
        {
            return $"{Name} ({MacAddress})";
        }
    }

    public class TempRestorePayload
    {
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
        public string? MacInterfaceIndex { get; set; }
        public string? MacAddress { get; set; }
    }
}
