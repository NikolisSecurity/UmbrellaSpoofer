namespace UmbrellaCore.Models
{
    public class NetworkAdapter
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string InterfaceIndex { get; set; } = string.Empty;

        public override string ToString() => $"{Name} ({MacAddress})";
    }
}
