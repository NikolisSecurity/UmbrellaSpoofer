using System;
using System.Runtime.InteropServices;

namespace UmbrellaCore.Hardware
{
    /// <summary>
    /// Reads raw SMBIOS tables via native Windows API (GetSystemFirmwareTable).
    /// More reliable on Windows 11 where WMI MSSMBios_RawSMBiosTables may return empty data
    /// due to Virtualization-Based Security (VBS) or HVCI restrictions.
    /// </summary>
    public static class NativeSmbiosReader
    {
        private const uint RSMB = 0x52534D42; // 'RSMB' signature

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(
            uint firmwareTableProviderSignature,
            uint firmwareTableID,
            IntPtr pFirmwareTableBuffer,
            uint bufferSize);

        /// <summary>
        /// Reads the raw SMBIOS table via native API.
        /// Returns null if the call fails or returns an empty buffer.
        /// </summary>
        public static byte[]? GetRawSmbiosData()
        {
            try
            {
                // First call: get required buffer size
                uint size = GetSystemFirmwareTable(RSMB, 0, IntPtr.Zero, 0);
                if (size == 0)
                    return null;

                IntPtr buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    uint bytesRead = GetSystemFirmwareTable(RSMB, 0, buffer, size);
                    if (bytesRead == 0 || bytesRead != size)
                        return null;

                    byte[] data = new byte[size];
                    Marshal.Copy(buffer, data, 0, (int)size);
                    return data;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
