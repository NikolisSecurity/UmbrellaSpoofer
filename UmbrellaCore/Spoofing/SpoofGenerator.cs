using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UmbrellaCore.Hardware;

namespace UmbrellaCore.Spoofing
{
    /// <summary>
    /// Generates randomized spoof values for hardware identifiers.
    /// </summary>
    public static class SpoofGenerator
    {
        public static string GenerateRandomHex(int length)
        {
            const string chars = "ABCDEF0123456789";
            return new string(Enumerable.Repeat(chars, length).Select(s => s[HardwareReader.Rng.Next(s.Length)]).ToArray());
        }

        public static string GenerateComponentSerial(string prefix, int randomLength)
        {
            var cleanPrefix = new string((prefix ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            var tail = GenerateRandomHex(Math.Max(randomLength, 4));
            return $"{cleanPrefix}-{tail}";
        }

        public static string GenerateRandomMac()
        {
            var bytes = new byte[6];
            HardwareReader.Rng.NextBytes(bytes);
            bytes[0] = (byte)((bytes[0] & 0xFE) | 0x02);
            return string.Join(":", bytes.Select(b => b.ToString("X2")));
        }

        /// <summary>
        /// Generates a random value that preserves the character style of the original
        /// (digits map to digits, uppercase to uppercase, lowercase to lowercase, others unchanged).
        /// </summary>
        public static string GetStylePreservedRandom(string original)
        {
            if (string.IsNullOrEmpty(original)) return GenerateRandomHex(10);

            var sb = new StringBuilder();
            foreach (char c in original)
            {
                if (char.IsDigit(c))
                    sb.Append(HardwareReader.Rng.Next(0, 10).ToString());
                else if (char.IsUpper(c))
                    sb.Append((char)HardwareReader.Rng.Next('A', 'Z' + 1));
                else if (char.IsLower(c))
                    sb.Append((char)HardwareReader.Rng.Next('a', 'z' + 1));
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
