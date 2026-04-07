using System.Collections.Generic;

namespace UmbrellaCore.Models
{
    public class TempRestorePayload
    {
        public Dictionary<string, string> Values { get; set; } = new();
        public string? MacAddress { get; set; }
        public string? MacInterfaceIndex { get; set; }

        public Dictionary<string, object> SystemInfo { get; set; } = new();
        public List<NetworkAdapter> NetworkAdapters { get; set; } = new();
    }
}
