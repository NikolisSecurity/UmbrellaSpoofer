using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UmbrellaCore.Driver
{
    /// <summary>
    /// IOCTL constants, P/Invoke signatures, structures, and enums for the Umbrella kernel driver.
    /// </summary>
    public static class DriverConstants
    {
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint FILE_SHARE_READ = 0x1;
        public const uint FILE_SHARE_WRITE = 0x2;
        public const uint OPEN_EXISTING = 3;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;

        public const uint IOCTL_UMBC_SPOOF_HARDWARE = 0x80002004;
        public const uint IOCTL_UMBC_RESTORE_HARDWARE = 0x80002008;
        public const uint IOCTL_UMBC_QUERY_SYSTEM_INFO = 0x8000200C;
        public const uint IOCTL_UMBC_MEMORY_MANIPULATION = 0x80002010;
        public const uint IOCTL_UMBC_SPOOF_ALL = 0x80002014;
        public const uint IOCTL_UMBC_RESTORE_ALL = 0x80002018;
        public const uint IOCTL_UMBC_QUERY_GENERATED_VALUES = 0x8000201C;
        public const uint IOCTL_UMBC_READ_WMI_RESPONSE = 0x80002020;

        public enum SpoofType : uint
        {
            MachineGuid = 0x01,
            BiosSerial = 0x02,
            BaseBoardSerial = 0x03,
            DiskSerial = 0x04,
            GpuId = 0x05,
            MacAddress = 0x06,
            SmbiosData = 0x07,
            AcpiTables = 0x08,
            MonitorSerial = 0x09
        }

        public enum InfoType : uint
        {
            MachineGuid = 0x01,
            BiosSerial = 0x02,
            BaseBoardSerial = 0x03,
            DiskSerial = 0x04,
            GpuId = 0x05,
            MacAddress = 0x06
        }

        public enum MemoryOperation : uint
        {
            Read = 0x01,
            Write = 0x02,
            Protect = 0x03
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct HardwareSpoofData
        {
            public SpoofType Type;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Identifier;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string NewValue;
            public uint DataSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SystemInfoData
        {
            public InfoType InfoType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
            public string InfoValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MemoryManipulationData
        {
            public MemoryOperation OperationType;
            public IntPtr TargetAddress;
            public uint DataSize;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)]
            public byte[] DataBuffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SpoofResultEntry
        {
            public uint SpoofType;
            public int Status;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SpoofAllResult
        {
            public uint ComponentCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public SpoofResultEntry[] Components;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct GeneratedValueEntry
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string ComponentName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string OriginalValue;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string NewValue;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct GeneratedValuesAll
        {
            public uint EntryCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public GeneratedValueEntry[] Entries;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WmiQueryData
        {
            public uint QueryType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
            public string OutputBuffer;
            public uint BufferSize;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);
    }
}
