#include "MemoryManipulation.h"
#include <wdm.h>
#include <ntstrsafe.h>

extern "C" NTSTATUS NTAPI PsLookupProcessByProcessId(
    HANDLE ProcessId,
    PEPROCESS* Process
);

extern "C" NTSTATUS NTAPI MmCopyVirtualMemory(
    PEPROCESS SourceProcess,
    PVOID SourceAddress,
    PEPROCESS TargetProcess,
    PVOID TargetAddress,
    SIZE_T BufferSize,
    KPROCESSOR_MODE PreviousMode,
    PSIZE_T ReturnSize
);

NTSTATUS ReadMemory(_In_ PVOID TargetAddress, _Out_ PVOID Buffer, _In_ ULONG Size) {
    NTSTATUS status = STATUS_SUCCESS;
    PMDL mdl = NULL;
    PVOID mappedAddress = NULL;
    
    if (!TargetAddress || !Buffer || Size == 0) {
        return STATUS_INVALID_PARAMETER;
    }
    
    mdl = IoAllocateMdl(TargetAddress, Size, FALSE, FALSE, NULL);
    if (!mdl) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    
    __try {
        MmProbeAndLockPages(mdl, KernelMode, IoReadAccess);
        mappedAddress = MmGetSystemAddressForMdlSafe(mdl, NormalPagePriority);
        
        if (mappedAddress) {
            RtlCopyMemory(Buffer, mappedAddress, Size);
        } else {
            status = STATUS_INSUFFICIENT_RESOURCES;
        }
        
        MmUnlockPages(mdl);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        status = GetExceptionCode();
    }
    
    IoFreeMdl(mdl);
    return status;
}

NTSTATUS WriteMemory(_In_ PVOID TargetAddress, _In_ PVOID Buffer, _In_ ULONG Size) {
    NTSTATUS status = STATUS_SUCCESS;
    PMDL mdl = NULL;
    PVOID mappedAddress = NULL;
    
    if (!TargetAddress || !Buffer || Size == 0) {
        return STATUS_INVALID_PARAMETER;
    }
    
    mdl = IoAllocateMdl(TargetAddress, Size, FALSE, FALSE, NULL);
    if (!mdl) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    
    __try {
        MmProbeAndLockPages(mdl, KernelMode, IoWriteAccess);
        mappedAddress = MmGetSystemAddressForMdlSafe(mdl, NormalPagePriority);
        
        if (mappedAddress) {
            RtlCopyMemory(mappedAddress, Buffer, Size);
        } else {
            status = STATUS_INSUFFICIENT_RESOURCES;
        }
        
        MmUnlockPages(mdl);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        status = GetExceptionCode();
    }
    
    IoFreeMdl(mdl);
    return status;
}

NTSTATUS ProtectMemory(_In_ PVOID TargetAddress, _In_ ULONG Size, _In_ UCHAR NewProtection) {
    NTSTATUS status = STATUS_SUCCESS;
    PMDL mdl = NULL;
    PVOID mappedAddress = NULL;
    
    if (!TargetAddress || Size == 0) {
        return STATUS_INVALID_PARAMETER;
    }
    
    mdl = IoAllocateMdl(TargetAddress, Size, FALSE, FALSE, NULL);
    if (!mdl) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    
    __try {
        MmProbeAndLockPages(mdl, KernelMode, IoModifyAccess);
        mappedAddress = MmGetSystemAddressForMdlSafe(mdl, NormalPagePriority);
        
        if (mappedAddress) {
            ULONG oldProtection;
            status = MmProtectMdlSystemAddress(mdl, NewProtection);
        } else {
            status = STATUS_INSUFFICIENT_RESOURCES;
        }
        
        MmUnlockPages(mdl);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        status = GetExceptionCode();
    }
    
    IoFreeMdl(mdl);
    return status;
}

NTSTATUS ReadPhysicalMemory(_In_ PHYSICAL_ADDRESS PhysicalAddress, _Out_ PVOID Buffer, _In_ ULONG Size) {
    PVOID mappedAddress;
    
    if (PhysicalAddress.QuadPart == 0 || !Buffer || Size == 0) {
        return STATUS_INVALID_PARAMETER;
    }
    
    mappedAddress = MmMapIoSpace(PhysicalAddress, Size, MmNonCached);
    if (!mappedAddress) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    
    __try {
        RtlCopyMemory(Buffer, mappedAddress, Size);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        MmUnmapIoSpace(mappedAddress, Size);
        return GetExceptionCode();
    }
    
    MmUnmapIoSpace(mappedAddress, Size);
    return STATUS_SUCCESS;
}

NTSTATUS WritePhysicalMemory(_In_ PHYSICAL_ADDRESS PhysicalAddress, _In_ PVOID Buffer, _In_ ULONG Size) {
    PVOID mappedAddress;
    
    if (PhysicalAddress.QuadPart == 0 || !Buffer || Size == 0) {
        return STATUS_INVALID_PARAMETER;
    }
    
    mappedAddress = MmMapIoSpace(PhysicalAddress, Size, MmNonCached);
    if (!mappedAddress) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    
    __try {
        RtlCopyMemory(mappedAddress, Buffer, Size);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        MmUnmapIoSpace(mappedAddress, Size);
        return GetExceptionCode();
    }
    
    MmUnmapIoSpace(mappedAddress, Size);
    return STATUS_SUCCESS;
}

NTSTATUS ReadProcessMemory(_In_ HANDLE ProcessId, _In_ PVOID Address, _Out_ PVOID Buffer, _In_ ULONG Size) {
    PEPROCESS process;
    NTSTATUS status;
    
    status = PsLookupProcessByProcessId(ProcessId, &process);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    
    __try {
        SIZE_T bytesRead;
        status = MmCopyVirtualMemory(process, Address, PsGetCurrentProcess(), Buffer, Size, KernelMode, &bytesRead);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        status = GetExceptionCode();
    }
    
    ObDereferenceObject(process);
    return status;
}

// ============================================================================
// Advanced Kernel Object Manipulation for Hardware Spoofing
// ============================================================================

NTSTATUS FindAndPatchHardwareStructures(_In_ PCWSTR TargetDriver, _In_ PCWSTR Signature, _In_ PVOID NewData, _In_ ULONG DataSize) {
    NTSTATUS status;
    PDRIVER_OBJECT driverObject = NULL;
    UNICODE_STRING driverName;
    
    RtlInitUnicodeString(&driverName, TargetDriver);
    
    status = ObReferenceObjectByName(&driverName, OBJ_CASE_INSENSITIVE, NULL, 0, *IoDriverObjectType, KernelMode, NULL, (PVOID*)&driverObject);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    
    // Scan driver memory for hardware-related structures
    PVOID driverStart = driverObject->DriverStart;
    ULONG driverSize = (ULONG)driverObject->DriverSize;
    
    if (driverStart && driverSize > 0) {
        PVOID mappedDriver = MmMapIoSpace(*(PHYSICAL_ADDRESS*)&driverStart, driverSize, MmNonCached);
        if (mappedDriver) {
            __try {
                // Simple signature scanning (in real implementation, use proper pattern matching)
                PUCHAR current = (PUCHAR)mappedDriver;
                PUCHAR end = current + driverSize - DataSize;
                
                while (current < end) {
                    if (RtlCompareMemory(current, Signature, DataSize) == DataSize) {
                        // Found target structure - patch it
                        RtlCopyMemory(current, NewData, DataSize);
                        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, 
                                  "MemoryManipulation: Patched hardware structure in %wZ driver\n", &driverName));
                        break;
                    }
                    current++;
                }
            } __except (EXCEPTION_EXECUTE_HANDLER) {
                status = GetExceptionCode();
            }
            
            MmUnmapIoSpace(mappedDriver, driverSize);
        }
    }
    
    ObDereferenceObject(driverObject);
    return status;
}

NTSTATUS ManipulateDeviceExtension(_In_ PCWSTR DeviceName, _In_ ULONG Offset, _In_ PVOID NewData, _In_ ULONG DataSize) {
    NTSTATUS status;
    PDEVICE_OBJECT deviceObject = NULL;
    UNICODE_STRING deviceName;
    
    RtlInitUnicodeString(&deviceName, DeviceName);
    
    status = IoGetDeviceObjectPointer(&deviceName, FILE_ALL_ACCESS, &deviceObject, NULL);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    
    if (deviceObject->DeviceExtension && Offset + DataSize <= deviceObject->Size) {
        PVOID targetAddress = (PUCHAR)deviceObject->DeviceExtension + Offset;
        
        __try {
            RtlCopyMemory(targetAddress, NewData, DataSize);
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, 
                      "MemoryManipulation: Modified device extension at offset 0x%X\n", Offset));
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            status = GetExceptionCode();
        }
    }
    
    ObDereferenceObject(deviceObject);
    return status;
}

NTSTATUS HideDriverFromLists() {
    NTSTATUS status = STATUS_SUCCESS;
    
    // This is a simplified example - real implementation would require
    // walking the loaded module list and manipulating the structures
    
    PLIST_ENTRY currentEntry = PsLoadedModuleList->Flink;
    while (currentEntry != PsLoadedModuleList) {
        PLDR_DATA_TABLE_ENTRY entry = CONTAINING_RECORD(currentEntry, LDR_DATA_TABLE_ENTRY, InLoadOrderLinks);
        
        if (entry->BaseDllName.Buffer && 
            wcsstr(entry->BaseDllName.Buffer, L"UmbrellaKernelDriver")) {
            // Remove our driver from the loaded module list
            currentEntry->Blink->Flink = currentEntry->Flink;
            currentEntry->Flink->Blink = currentEntry->Blink;
            
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, 
                      "MemoryManipulation: Hid driver from loaded module list\n"));
            break;
        }
        
        currentEntry = currentEntry->Flink;
    }
    
    return status;
}

NTSTATUS SpoofHardwareInMemory(_In_ HARDWARE_TYPE HardwareType, _In_ PVOID NewData, _In_ ULONG DataSize) {
    NTSTATUS status = STATUS_SUCCESS;
    
    switch (HardwareType) {
        case HardwareTypeDisk:
            status = FindAndPatchHardwareStructures(
                L"\\Driver\\Disk", 
                L"SerialNumber", 
                NewData, 
                DataSize
            );
            break;
            
        case HardwareTypeNetwork:
            status = FindAndPatchHardwareStructures(
                L"\\Driver\\NDIS", 
                L"MACAddress", 
                NewData, 
                DataSize
            );
            break;
            
        case HardwareTypeGpu:
            status = FindAndPatchHardwareStructures(
                L"\\Driver\\nvlddmkm", // NVIDIA driver
                L"DeviceID", 
                NewData, 
                DataSize
            );
            break;
            
        default:
            status = STATUS_NOT_SUPPORTED;
            break;
    }
    
    return status;
}

NTSTATUS WriteProcessMemory(_In_ HANDLE ProcessId, _In_ PVOID Address, _In_ PVOID Buffer, _In_ ULONG Size) {
    PEPROCESS process;
    NTSTATUS status;
    
    status = PsLookupProcessByProcessId(ProcessId, &process);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    
    __try {
        SIZE_T bytesWritten;
        status = MmCopyVirtualMemory(PsGetCurrentProcess(), Buffer, process, Address, Size, KernelMode, &bytesWritten);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        status = GetExceptionCode();
    }
    
    ObDereferenceObject(process);
    return status;
}

NTSTATUS AllocateKernelMemory(_Out_ PVOID* Address, _In_ ULONG Size, _In_ ULONG PoolType) {
    if (!Address || Size == 0) {
        return STATUS_INVALID_PARAMETER;
    }
    
    *Address = ExAllocatePoolWithTag((POOL_TYPE)PoolType, Size, DRIVER_TAG);
    if (!*Address) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    
    RtlZeroMemory(*Address, Size);
    return STATUS_SUCCESS;
}

NTSTATUS FreeKernelMemory(_In_ PVOID Address) {
    if (!Address) {
        return STATUS_INVALID_PARAMETER;
    }
    
    ExFreePoolWithTag(Address, DRIVER_TAG);
    return STATUS_SUCCESS;
}

NTSTATUS HookFunction(_In_ PVOID TargetFunction, _In_ PVOID HookFunction, _Out_ PVOID* OriginalFunction) {
    UCHAR jmpCode[14] = {0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xE0};
    ULONG oldProtection;
    NTSTATUS status;
    
    if (!TargetFunction || !HookFunction || !OriginalFunction) {
        return STATUS_INVALID_PARAMETER;
    }
    
    *OriginalFunction = ExAllocatePoolWithTag(NonPagedPool, 14, DRIVER_TAG);
    if (!*OriginalFunction) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    
    status = ReadMemory(TargetFunction, *OriginalFunction, 14);
    if (!NT_SUCCESS(status)) {
        ExFreePoolWithTag(*OriginalFunction, DRIVER_TAG);
        return status;
    }
    
    *(ULONG_PTR*)(jmpCode + 2) = (ULONG_PTR)HookFunction;
    
    status = WriteMemory(TargetFunction, jmpCode, sizeof(jmpCode));
    if (!NT_SUCCESS(status)) {
        ExFreePoolWithTag(*OriginalFunction, DRIVER_TAG);
        return status;
    }
    
    return STATUS_SUCCESS;
}

NTSTATUS UnhookFunction(_In_ PVOID TargetFunction, _In_ PVOID OriginalFunction) {
    if (!TargetFunction || !OriginalFunction) {
        return STATUS_INVALID_PARAMETER;
    }
    
    NTSTATUS status = WriteMemory(TargetFunction, OriginalFunction, 14);
    ExFreePoolWithTag(OriginalFunction, DRIVER_TAG);
    return status;
}

NTSTATUS PatchMemory(_In_ PVOID Address, _In_ PUCHAR Pattern, _In_ ULONG PatternSize, _In_ PUCHAR Replacement, _In_ ULONG ReplacementSize) {
    UCHAR* buffer;
    NTSTATUS status;
    
    if (!Address || !Pattern || PatternSize == 0 || !Replacement || ReplacementSize == 0) {
        return STATUS_INVALID_PARAMETER;
    }
    
    buffer = (UCHAR*)ExAllocatePoolWithTag(NonPagedPool, PatternSize, DRIVER_TAG);
    if (!buffer) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    
    status = ReadMemory(Address, buffer, PatternSize);
    if (!NT_SUCCESS(status)) {
        ExFreePoolWithTag(buffer, DRIVER_TAG);
        return status;
    }
    
    if (RtlCompareMemory(buffer, Pattern, PatternSize) != PatternSize) {
        ExFreePoolWithTag(buffer, DRIVER_TAG);
        return STATUS_NOT_FOUND;
    }
    
    status = WriteMemory(Address, Replacement, ReplacementSize);
    ExFreePoolWithTag(buffer, DRIVER_TAG);
    return status;
}

NTSTATUS FindPattern(_In_ PVOID BaseAddress, _In_ ULONG Size, _In_ PUCHAR Pattern, _In_ PUCHAR Mask, _Out_ PVOID* FoundAddress) {
    UCHAR* current;
    ULONG i;
    
    if (!BaseAddress || Size == 0 || !Pattern || !Mask || !FoundAddress) {
        return STATUS_INVALID_PARAMETER;
    }
    
    current = (UCHAR*)BaseAddress;
    
    for (i = 0; i <= Size - strlen((char*)Mask); i++) {
        BOOLEAN found = TRUE;
        ULONG j;
        
        for (j = 0; Mask[j]; j++) {
            if (Mask[j] == 'x' && current[i + j] != Pattern[j]) {
                found = FALSE;
                break;
            }
        }
        
        if (found) {
            *FoundAddress = current + i;
            return STATUS_SUCCESS;
        }
    }
    
    return STATUS_NOT_FOUND;
}