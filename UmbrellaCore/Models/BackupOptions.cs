namespace UmbrellaCore.Models
{
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
}
