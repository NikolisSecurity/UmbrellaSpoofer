using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UmbrellaCore.Driver
{
    /// <summary>
    /// Communication with the Umbrella kernel driver via IOCTL calls.
    /// References DriverConstants for all P/Invoke, structs, and IOCTL codes.
    /// </summary>
    public class KernelDriverService : IDisposable
    {
        private SafeFileHandle? _driverHandle;
        private bool _disposed = false;

        public KernelDriverService()
        {
            ConnectToDriver();
        }

        private void ConnectToDriver()
        {
            try
            {
                _driverHandle = DriverConstants.CreateFile(
                    "\\\\.\\UmbrellaKernelDriver",
                    DriverConstants.GENERIC_READ | DriverConstants.GENERIC_WRITE,
                    DriverConstants.FILE_SHARE_READ | DriverConstants.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    DriverConstants.OPEN_EXISTING,
                    DriverConstants.FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);

                if (_driverHandle == null || _driverHandle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "Failed to connect to kernel driver. Make sure the driver is loaded and running.");
                }
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Failed to initialize kernel driver service", ex);
            }
        }

        private bool SendIoctl(uint controlCode, IntPtr inBuffer, uint inBufferSize, IntPtr outBuffer, uint outBufferSize)
        {
            if (_driverHandle == null || _driverHandle.IsInvalid)
                return false;

            uint bytesReturned;
            return DriverConstants.DeviceIoControl(
                _driverHandle,
                controlCode,
                inBuffer,
                inBufferSize,
                outBuffer,
                outBufferSize,
                out bytesReturned,
                IntPtr.Zero);
        }

        public string SpoofHardware(DriverConstants.SpoofType type, string identifier, string newValue)
        {
            if (_driverHandle == null || _driverHandle.IsInvalid)
                throw new InvalidOperationException("Driver is not connected");

            var spoofData = new DriverConstants.HardwareSpoofData
            {
                Type = type,
                Identifier = identifier,
                NewValue = newValue,
                DataSize = 0
            };

            IntPtr spoofDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(spoofData));
            try
            {
                Marshal.StructureToPtr(spoofData, spoofDataPtr, false);

                uint bytesReturned;
                bool success = DriverConstants.DeviceIoControl(
                    _driverHandle,
                    DriverConstants.IOCTL_UMBC_SPOOF_HARDWARE,
                    spoofDataPtr,
                    (uint)Marshal.SizeOf(spoofData),
                    spoofDataPtr,
                    (uint)Marshal.SizeOf(spoofData),
                    out bytesReturned,
                    IntPtr.Zero);

                if (!success)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "Hardware spoof operation failed");
                }

                var returnedData = Marshal.PtrToStructure<DriverConstants.HardwareSpoofData>(spoofDataPtr);
                return returnedData.NewValue;
            }
            finally
            {
                Marshal.FreeHGlobal(spoofDataPtr);
            }
        }

        public bool RestoreHardware(DriverConstants.SpoofType type, string identifier)
        {
            if (_driverHandle == null || _driverHandle.IsInvalid)
                return false;

            var restoreData = new DriverConstants.HardwareSpoofData
            {
                Type = type,
                Identifier = identifier,
                NewValue = string.Empty,
                DataSize = 0
            };

            IntPtr restoreDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(restoreData));
            try
            {
                Marshal.StructureToPtr(restoreData, restoreDataPtr, false);

                uint bytesReturned;
                bool success = DriverConstants.DeviceIoControl(
                    _driverHandle,
                    DriverConstants.IOCTL_UMBC_RESTORE_HARDWARE,
                    restoreDataPtr,
                    (uint)Marshal.SizeOf(restoreData),
                    IntPtr.Zero,
                    0,
                    out bytesReturned,
                    IntPtr.Zero);

                if (!success)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "Hardware restore operation failed");
                }

                return success;
            }
            finally
            {
                Marshal.FreeHGlobal(restoreDataPtr);
            }
        }

        public string? QuerySystemInfo(DriverConstants.InfoType type)
        {
            if (_driverHandle == null || _driverHandle.IsInvalid)
                return null;

            var inputData = new DriverConstants.SystemInfoData { InfoType = type };
            var outputData = new DriverConstants.SystemInfoData();

            IntPtr inputDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(inputData));
            IntPtr outputDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(outputData));
            try
            {
                Marshal.StructureToPtr(inputData, inputDataPtr, false);

                uint bytesReturned;
                bool success = DriverConstants.DeviceIoControl(
                    _driverHandle,
                    DriverConstants.IOCTL_UMBC_QUERY_SYSTEM_INFO,
                    inputDataPtr,
                    (uint)Marshal.SizeOf(inputData),
                    outputDataPtr,
                    (uint)Marshal.SizeOf(outputData),
                    out bytesReturned,
                    IntPtr.Zero);

                if (!success)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "System info query failed");
                }

                outputData = Marshal.PtrToStructure<DriverConstants.SystemInfoData>(outputDataPtr);
                return outputData.InfoValue;
            }
            finally
            {
                Marshal.FreeHGlobal(inputDataPtr);
                Marshal.FreeHGlobal(outputDataPtr);
            }
        }

        public byte[]? ReadMemory(IntPtr address, uint size)
        {
            if (_driverHandle == null || _driverHandle.IsInvalid)
                return null;

            if (size > 1024)
                throw new ArgumentException("Size cannot exceed 1024 bytes");

            var memData = new DriverConstants.MemoryManipulationData
            {
                OperationType = DriverConstants.MemoryOperation.Read,
                TargetAddress = address,
                DataSize = size,
                DataBuffer = new byte[1024]
            };

            IntPtr memDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(memData));
            try
            {
                Marshal.StructureToPtr(memData, memDataPtr, false);

                uint bytesReturned;
                bool success = DriverConstants.DeviceIoControl(
                    _driverHandle,
                    DriverConstants.IOCTL_UMBC_MEMORY_MANIPULATION,
                    memDataPtr,
                    (uint)Marshal.SizeOf(memData),
                    IntPtr.Zero,
                    0,
                    out bytesReturned,
                    IntPtr.Zero);

                if (!success)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "Memory read operation failed");
                }

                memData = Marshal.PtrToStructure<DriverConstants.MemoryManipulationData>(memDataPtr);
                byte[] result = new byte[bytesReturned];
                Array.Copy(memData.DataBuffer, result, bytesReturned);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(memDataPtr);
            }
        }

        public bool WriteMemory(IntPtr address, byte[] data)
        {
            if (_driverHandle == null || _driverHandle.IsInvalid)
                return false;

            if (data.Length > 1024)
                throw new ArgumentException("Data size cannot exceed 1024 bytes");

            var memData = new DriverConstants.MemoryManipulationData
            {
                OperationType = DriverConstants.MemoryOperation.Write,
                TargetAddress = address,
                DataSize = (uint)data.Length,
                DataBuffer = data
            };

            IntPtr memDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(memData));
            try
            {
                Marshal.StructureToPtr(memData, memDataPtr, false);

                uint bytesReturned;
                bool success = DriverConstants.DeviceIoControl(
                    _driverHandle,
                    DriverConstants.IOCTL_UMBC_MEMORY_MANIPULATION,
                    memDataPtr,
                    (uint)Marshal.SizeOf(memData),
                    IntPtr.Zero,
                    0,
                    out bytesReturned,
                    IntPtr.Zero);

                if (!success)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "Memory write operation failed");
                }

                return success;
            }
            finally
            {
                Marshal.FreeHGlobal(memDataPtr);
            }
        }

        public DriverConstants.SpoofAllResult SpoofAll()
        {
            if (_driverHandle == null || _driverHandle.IsInvalid)
                throw new InvalidOperationException("Driver is not connected");

            var result = new DriverConstants.SpoofAllResult { ComponentCount = 0, Components = new DriverConstants.SpoofResultEntry[16] };
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(result));
            try
            {
                Marshal.StructureToPtr(result, ptr, false);
                bool success = DriverConstants.DeviceIoControl(_driverHandle, DriverConstants.IOCTL_UMBC_SPOOF_ALL, ptr, (uint)Marshal.SizeOf(result), ptr, (uint)Marshal.SizeOf(result), out uint bytesReturned, IntPtr.Zero);
                if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "SpoofAll failed");
                return Marshal.PtrToStructure<DriverConstants.SpoofAllResult>(ptr);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public DriverConstants.SpoofAllResult RestoreAll()
        {
            if (_driverHandle == null || _driverHandle.IsInvalid)
                throw new InvalidOperationException("Driver is not connected");

            var result = new DriverConstants.SpoofAllResult { ComponentCount = 0, Components = new DriverConstants.SpoofResultEntry[16] };
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(result));
            try
            {
                Marshal.StructureToPtr(result, ptr, false);
                bool success = DriverConstants.DeviceIoControl(_driverHandle, DriverConstants.IOCTL_UMBC_RESTORE_ALL, ptr, (uint)Marshal.SizeOf(result), ptr, (uint)Marshal.SizeOf(result), out uint bytesReturned, IntPtr.Zero);
                if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "RestoreAll failed");
                return Marshal.PtrToStructure<DriverConstants.SpoofAllResult>(ptr);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public Dictionary<string, string> QueryAllGeneratedValues()
        {
            if (_driverHandle == null || _driverHandle.IsInvalid)
                return new Dictionary<string, string>();

            var output = new DriverConstants.GeneratedValuesAll { EntryCount = 0, Entries = new DriverConstants.GeneratedValueEntry[16] };
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(output));
            try
            {
                Marshal.StructureToPtr(output, ptr, false);
                bool success = DriverConstants.DeviceIoControl(_driverHandle, DriverConstants.IOCTL_UMBC_QUERY_GENERATED_VALUES, ptr, (uint)Marshal.SizeOf(output), ptr, (uint)Marshal.SizeOf(output), out uint bytesReturned, IntPtr.Zero);
                if (!success) return new Dictionary<string, string>();

                var result = Marshal.PtrToStructure<DriverConstants.GeneratedValuesAll>(ptr);
                var dict = new Dictionary<string, string>();
                for (uint i = 0; i < result.EntryCount; i++)
                {
                    var entry = result.Entries[i];
                    if (!string.IsNullOrEmpty(entry.NewValue))
                        dict[entry.ComponentName] = entry.NewValue;
                }
                return dict;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public string? QueryWmiResponse(uint queryType)
        {
            if (_driverHandle == null || _driverHandle.IsInvalid)
                return null;

            var data = new DriverConstants.WmiQueryData { QueryType = queryType, OutputBuffer = "", BufferSize = 0 };
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            try
            {
                Marshal.StructureToPtr(data, ptr, false);
                bool success = DriverConstants.DeviceIoControl(_driverHandle, DriverConstants.IOCTL_UMBC_READ_WMI_RESPONSE, ptr, (uint)Marshal.SizeOf(data), ptr, (uint)Marshal.SizeOf(data), out uint bytesReturned, IntPtr.Zero);
                if (!success) return null;
                var result = Marshal.PtrToStructure<DriverConstants.WmiQueryData>(ptr);
                return result.OutputBuffer;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public bool IsDriverConnected
        {
            get { return _driverHandle != null && !_driverHandle.IsInvalid; }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_driverHandle != null && !_driverHandle.IsInvalid)
                    {
                        _driverHandle.Dispose();
                    }
                }
                _disposed = true;
            }
        }

        ~KernelDriverService()
        {
            Dispose(false);
        }
    }
}
