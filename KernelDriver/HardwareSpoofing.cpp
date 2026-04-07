#include "HardwareSpoofing.h"
#include "HardwareIdGenerator.h"
#include "Driver.h"
#include <wdm.h>
#include <ntstrsafe.h>
#include <ntddstor.h>
#include <mountdev.h>
#include <ntddvol.h>

// Forward declarations for undocumented functions
extern "C" NTKERNELAPI NTSTATUS PsLookupProcessByProcessId(_In_ HANDLE ProcessId, _Out_ PEPROCESS* Process);
extern "C" NTKERNELAPI PVOID NTAPI PsGetProcessPeb(_In_ PEPROCESS Process);

extern "C" POBJECT_TYPE* IoDriverObjectType;

extern "C" NTSTATUS NTAPI ObReferenceObjectByName(
    PUNICODE_STRING ObjectName,
    ULONG Attributes,
    PACCESS_STATE AccessState,
    ACCESS_MASK DesiredAccess,
    POBJECT_TYPE ObjectType,
    KPROCESSOR_MODE AccessMode,
    PVOID ParseContext,
    PVOID* Object
);

// Global variables for spoofing
WCHAR g_SpoofedDiskSerial[256] = {0};
BOOLEAN g_DiskSpoofingActive = FALSE;
HOOK_CONTEXT g_DiskHookContext = {0};

// Global variables for kernel callbacks (extern declarations from header)

// Forward declaration
NTSTATUS HookedDiskDeviceControl(PDEVICE_OBJECT DeviceObject, PIRP Irp);

// Helper to get driver object by name
NTSTATUS GetDriverObjectByName(PUNICODE_STRING DriverName, PDRIVER_OBJECT* DriverObject) {
    NTSTATUS status;
    PVOID object = NULL;

    status = ObReferenceObjectByName(
        DriverName,
        OBJ_CASE_INSENSITIVE,
        NULL,
        0,
        *IoDriverObjectType,
        KernelMode,
        NULL,
        &object
    );

    if (NT_SUCCESS(status)) {
        *DriverObject = (PDRIVER_OBJECT)object;
    }

    return status;
}

// ============================================================================
// Kernel Callback Functions for Anti-Detection
// ============================================================================

VOID ProcessNotifyRoutine(_In_ HANDLE ParentId, _In_ HANDLE ProcessId, _In_ BOOLEAN Create) {
    UNREFERENCED_PARAMETER(ParentId);
    
    if (Create) {
        // Monitor process creation for anti-cheat detection
        PEPROCESS process = NULL;
        NTSTATUS status = PsLookupProcessByProcessId(ProcessId, &process);
        
        if (NT_SUCCESS(status)) {
            // Check if this is an anti-cheat process
            // In a real implementation, you'd check process names, signatures, etc.
            ObDereferenceObject(process);
        }
    }
}

VOID ThreadNotifyRoutine(_In_ HANDLE ProcessId, _In_ HANDLE ThreadId, _In_ BOOLEAN Create) {
    UNREFERENCED_PARAMETER(ProcessId);
    UNREFERENCED_PARAMETER(ThreadId);
    
    if (Create) {
        // Monitor thread creation for suspicious activity
        // Anti-cheat systems often create monitoring threads
    }
}

VOID LoadImageNotifyRoutine(_In_ PUNICODE_STRING FullImageName, _In_ HANDLE ProcessId, _In_ PIMAGE_INFO ImageInfo) {
    UNREFERENCED_PARAMETER(ProcessId);
    UNREFERENCED_PARAMETER(ImageInfo);
    
    if (FullImageName) {
        // Check if this is an anti-cheat driver or module being loaded
        // In a real implementation, you'd check module names and signatures
        if (wcsstr(FullImageName->Buffer, L"EasyAntiCheat") ||
            wcsstr(FullImageName->Buffer, L"BattlEye") ||
            wcsstr(FullImageName->Buffer, L"Vanguard")) {
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, 
                      "UmbrellaKernelDriver: Anti-cheat module loaded: %wZ\n", FullImageName));
        }
    }
}

NTSTATUS RegistryNotifyRoutine(_In_ PVOID CallbackContext, _In_ PVOID Argument1, _In_ PVOID Argument2) {
    UNREFERENCED_PARAMETER(CallbackContext);
    
    // Monitor registry changes for anti-cheat detection
    REG_NOTIFY_CLASS notifyClass = (REG_NOTIFY_CLASS)(ULONG_PTR)Argument1;
    
    if (notifyClass == RegNtPreSetValueKey || notifyClass == RegNtPreQueryValueKey) {
        PREG_SET_VALUE_KEY_INFORMATION setValueInfo = (PREG_SET_VALUE_KEY_INFORMATION)Argument2;
        
        // Check if anti-cheat is trying to detect our driver
        if (setValueInfo && setValueInfo->ValueName) {
            if (wcsstr(setValueInfo->ValueName->Buffer, L"UmbrellaKernelDriver") ||
                wcsstr(setValueInfo->ValueName->Buffer, L"Spoof")) {
                // Block or modify the detection attempt
                return STATUS_ACCESS_DENIED;
            }
        }
    }
    
    return STATUS_SUCCESS;
}

NTSTATUS InitializeKernelCallbacks() {
    NTSTATUS status;
    
    // Register process creation callback
    status = PsSetCreateProcessNotifyRoutine(ProcessNotifyRoutine, FALSE);
    if (NT_SUCCESS(status)) {
        g_ProcessNotifyRoutine = ProcessNotifyRoutine;
    }
    
    // Register thread creation callback
    status = PsSetCreateThreadNotifyRoutine(ThreadNotifyRoutine);
    if (NT_SUCCESS(status)) {
        g_ThreadNotifyRoutine = ThreadNotifyRoutine;
    }
    
    // Register image load callback
    status = PsSetLoadImageNotifyRoutine(LoadImageNotifyRoutine);
    if (NT_SUCCESS(status)) {
        g_LoadImageNotifyRoutine = LoadImageNotifyRoutine;
    }
    
    // Register registry callback
    status = CmRegisterCallbackEx(RegistryNotifyRoutine, NULL, &g_RegistryNotifyRoutine, NULL);
    
    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, 
              "UmbrellaKernelDriver: Kernel callbacks initialized\n"));
    
    return STATUS_SUCCESS;
}

NTSTATUS RemoveKernelCallbacks() {
    if (g_ProcessNotifyRoutine) {
        PsSetCreateProcessNotifyRoutineEx((PCREATE_PROCESS_NOTIFY_ROUTINE_EX)g_ProcessNotifyRoutine, TRUE);
        g_ProcessNotifyRoutine = NULL;
    }
    
    if (g_ThreadNotifyRoutine) {
        PsRemoveCreateThreadNotifyRoutine((PCREATE_THREAD_NOTIFY_ROUTINE)g_ThreadNotifyRoutine);
        g_ThreadNotifyRoutine = NULL;
    }
    
    if (g_LoadImageNotifyRoutine) {
        PsRemoveLoadImageNotifyRoutine((PLOAD_IMAGE_NOTIFY_ROUTINE)g_LoadImageNotifyRoutine);
        g_LoadImageNotifyRoutine = NULL;
    }
    
    if (g_RegistryNotifyRoutine) {
        CmUnRegisterCallback(g_RegistryNotifyRoutine);
        g_RegistryNotifyRoutine = NULL;
    }
    
    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, 
              "UmbrellaKernelDriver: Kernel callbacks removed\n"));
    
    return STATUS_SUCCESS;
}

NTSTATUS InstallAntiDetectionHooks() {
    NTSTATUS status;
    
    // Hook common anti-cheat detection functions
    // This would require signature scanning and inline hooking of specific functions
    // For now, we'll implement basic protection
    
    status = ProtectDriverMemory();
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, 
                  "UmbrellaKernelDriver: Failed to protect driver memory: 0x%X\n", status));
    }
    
    return status;
}

NTSTATUS RemoveAntiDetectionHooks() {
    return UnprotectDriverMemory();
}

NTSTATUS ProtectDriverMemory() {
    // Implement memory protection to prevent anti-cheat from scanning our driver
    // This would involve modifying page protections and hiding driver sections
    
    // Get the current driver object
    PDRIVER_OBJECT driverObject = WdfDriverGlobals->DriverObject;
    if (!driverObject) {
        return STATUS_UNSUCCESSFUL;
    }
    
    PMDL mdl = IoAllocateMdl(driverObject->DriverStart, (ULONG)driverObject->DriverSize, FALSE, FALSE, NULL);
    if (!mdl) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    
    MmProbeAndLockPages(mdl, KernelMode, IoReadAccess);
    
    // Mark pages as non-readable to prevent scanning
    PVOID mapped = MmMapLockedPagesSpecifyCache(mdl, KernelMode, MmNonCached, NULL, FALSE, NormalPagePriority);
    if (mapped) {
        // Change protection to prevent reading
        // Note: This is a simplified example - real implementation would be more complex
        MmUnmapLockedPages(mapped, mdl);
    }
    
    MmUnlockPages(mdl);
    IoFreeMdl(mdl);
    
    return STATUS_SUCCESS;
}

NTSTATUS UnprotectDriverMemory() {
    // Restore original memory protections
    return STATUS_SUCCESS;
}

// Completion routine for disk IOCTL
NTSTATUS DiskDeviceControlCompletion(
    PDEVICE_OBJECT DeviceObject,
    PIRP Irp,
    PVOID Context
) {
    PIO_STACK_LOCATION irpSp = IoGetCurrentIrpStackLocation(Irp);
    PSTORAGE_DEVICE_DESCRIPTOR descriptor = NULL;
    ULONG serialOffset = 0;
    ULONG bufferLength = 0;
    
    UNREFERENCED_PARAMETER(DeviceObject);
    UNREFERENCED_PARAMETER(Context);

    if (NT_SUCCESS(Irp->IoStatus.Status) && g_DiskSpoofingActive) {
        if (irpSp->Parameters.DeviceIoControl.IoControlCode == IOCTL_STORAGE_QUERY_PROPERTY) {
            PSTORAGE_PROPERTY_QUERY query = (PSTORAGE_PROPERTY_QUERY)Irp->AssociatedIrp.SystemBuffer;
            
            if (query && query->PropertyId == StorageDeviceProperty && query->QueryType == PropertyStandardQuery) {
                descriptor = (PSTORAGE_DEVICE_DESCRIPTOR)Irp->AssociatedIrp.SystemBuffer;
                bufferLength = (ULONG)Irp->IoStatus.Information;

                if (descriptor && bufferLength >= sizeof(STORAGE_DEVICE_DESCRIPTOR)) {
                    if (descriptor->SerialNumberOffset != 0 && descriptor->SerialNumberOffset < bufferLength) {
                        char* serialPtr = (char*)descriptor + descriptor->SerialNumberOffset;
                        size_t maxLen = bufferLength - descriptor->SerialNumberOffset;
                        
                        // Check if we have enough space or if we need to be careful
                        // For simplicity, we assume standard ASCII serials and just overwrite
                        // In a production driver, you'd handle buffer resizing or relocation if needed
                        
                        // Convert global WCHAR serial to ASCII
                        UNICODE_STRING uniSerial;
                        ANSI_STRING ansiSerial;
                        RtlInitUnicodeString(&uniSerial, g_SpoofedDiskSerial);
                        
                        if (NT_SUCCESS(RtlUnicodeStringToAnsiString(&ansiSerial, &uniSerial, TRUE))) {
                             // Only copy if it fits, otherwise truncate
                             ULONG copyLen = min(ansiSerial.Length, (ULONG)strlen(serialPtr));
                             if (copyLen > 0) {
                                 RtlCopyMemory(serialPtr, ansiSerial.Buffer, copyLen);
                                 // Pad with spaces if original was longer? Or null terminate?
                                 // Usually safe to just null terminate if shorter
                                 if (copyLen < (ULONG)strlen(serialPtr)) {
                                     serialPtr[copyLen] = 0;
                                 }
                             }
                             RtlFreeAnsiString(&ansiSerial);
                        }
                    }
                }
            }
        }
    }

    if (Irp->PendingReturned) {
        IoMarkIrpPending(Irp);
    }

    return Irp->IoStatus.Status;
}

// Hooked dispatch routine
NTSTATUS HookedDiskDeviceControl(PDEVICE_OBJECT DeviceObject, PIRP Irp) {
    PIO_STACK_LOCATION irpSp = IoGetCurrentIrpStackLocation(Irp);
    
    if (g_DiskSpoofingActive && irpSp->MajorFunction == IRP_MJ_DEVICE_CONTROL) {
        if (irpSp->Parameters.DeviceIoControl.IoControlCode == IOCTL_STORAGE_QUERY_PROPERTY) {
            // Set completion routine to modify the result
            IoCopyCurrentIrpStackLocationToNext(Irp);
            IoSetCompletionRoutine(
                Irp,
                DiskDeviceControlCompletion,
                NULL,
                TRUE,
                TRUE,
                TRUE
            );
            
            // Call original dispatch
            return g_DiskHookContext.OriginalMajorFunction(DeviceObject, Irp);
        }
    }

    return g_DiskHookContext.OriginalMajorFunction(DeviceObject, Irp);
}

NTSTATUS InitializeDiskHook() {
    UNICODE_STRING driverName;
    NTSTATUS status;

    if (g_DiskHookContext.DriverObject != NULL) {
        return STATUS_SUCCESS; // Already hooked
    }

    // Hook stornvme.sys first (Win11 NVMe), then disk.sys (SATA), then PartMgr
    RtlInitUnicodeString(&driverName, L"\\Driver\\stornvme");
    status = GetDriverObjectByName(&driverName, &g_DiskHookContext.DriverObject);

    if (!NT_SUCCESS(status)) {
        RtlInitUnicodeString(&driverName, L"\\Driver\\Disk");
        status = GetDriverObjectByName(&driverName, &g_DiskHookContext.DriverObject);
    }

    if (!NT_SUCCESS(status)) {
        RtlInitUnicodeString(&driverName, L"\\Driver\\PartMgr");
        status = GetDriverObjectByName(&driverName, &g_DiskHookContext.DriverObject);
    }

    if (NT_SUCCESS(status)) {
        // Save original dispatch
        g_DiskHookContext.OriginalMajorFunction = g_DiskHookContext.DriverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL];
        
        // Install hook using InterlockedExchangePointer for atomicity
        InterlockedExchangePointer(
            (PVOID*)&g_DiskHookContext.DriverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL],
            (PVOID)HookedDiskDeviceControl
        );
        
        g_DiskHookContext.DriverName = driverName; // Just pointer reference, be careful if string is stack allocated (it's literal here so fine)
        
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: Disk driver hooked successfully\n"));
    }

    return status;
}

NTSTATUS RemoveDiskHook() {
    if (g_DiskHookContext.DriverObject && g_DiskHookContext.OriginalMajorFunction) {
        InterlockedExchangePointer(
            (PVOID*)&g_DiskHookContext.DriverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL],
            (PVOID)g_DiskHookContext.OriginalMajorFunction
        );
        
        ObDereferenceObject(g_DiskHookContext.DriverObject);
        g_DiskHookContext.DriverObject = NULL;
        g_DiskHookContext.OriginalMajorFunction = NULL;
        
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: Disk hook removed\n"));
    }
    return STATUS_SUCCESS;
}

// --------------------------------------------------------------------------------
// Existing Registry Helpers (Keep these as they are useful for basic spoofing)
// --------------------------------------------------------------------------------

NTSTATUS ModifyRegistryValue(_In_ PCWSTR KeyPath, _In_ PCWSTR ValueName, _In_ PCWSTR NewValue) {
    UNICODE_STRING keyPathStr, valueNameStr;
    OBJECT_ATTRIBUTES objectAttributes;
    HANDLE keyHandle = NULL;
    NTSTATUS status;
    ULONG disposition;
    
    RtlInitUnicodeString(&keyPathStr, KeyPath);
    RtlInitUnicodeString(&valueNameStr, ValueName);
    
    InitializeObjectAttributes(&objectAttributes, &keyPathStr, OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE, NULL, NULL);
    
    status = ZwCreateKey(&keyHandle, KEY_SET_VALUE, &objectAttributes, 0, NULL, REG_OPTION_NON_VOLATILE, &disposition);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    
    status = ZwSetValueKey(keyHandle, &valueNameStr, 0, REG_SZ, (PVOID)NewValue, (wcslen(NewValue) + 1) * sizeof(WCHAR));
    
    ZwClose(keyHandle);
    return status;
}

NTSTATUS QueryRegistryValue(_In_ PCWSTR KeyPath, _In_ PCWSTR ValueName, _Out_ PWSTR Buffer, _In_ ULONG BufferSize) {
    UNICODE_STRING keyPathStr, valueNameStr;
    OBJECT_ATTRIBUTES objectAttributes;
    HANDLE keyHandle = NULL;
    NTSTATUS status;
    KEY_VALUE_PARTIAL_INFORMATION* valueInfo;
    ULONG resultLength;
    
    RtlInitUnicodeString(&keyPathStr, KeyPath);
    RtlInitUnicodeString(&valueNameStr, ValueName);
    
    InitializeObjectAttributes(&objectAttributes, &keyPathStr, OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE, NULL, NULL);
    
    status = ZwOpenKey(&keyHandle, KEY_QUERY_VALUE, &objectAttributes);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    
    valueInfo = (KEY_VALUE_PARTIAL_INFORMATION*)ExAllocatePoolWithTag(NonPagedPool, sizeof(KEY_VALUE_PARTIAL_INFORMATION) + BufferSize, DRIVER_TAG);
    if (!valueInfo) {
        ZwClose(keyHandle);
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    
    status = ZwQueryValueKey(keyHandle, &valueNameStr, KeyValuePartialInformation, valueInfo, 
                           sizeof(KEY_VALUE_PARTIAL_INFORMATION) + BufferSize, &resultLength);
    
    if (NT_SUCCESS(status) && valueInfo->Type == REG_SZ) {
        status = RtlStringCchCopyW(Buffer, BufferSize / sizeof(WCHAR), (PWSTR)valueInfo->Data);
    }
    
    ExFreePoolWithTag(valueInfo, DRIVER_TAG);
    ZwClose(keyHandle);
    return status;
}

// --------------------------------------------------------------------------------
// Kernel-Level Hardware Manipulation Functions
// --------------------------------------------------------------------------------

// SMBIOS Table Manipulation
NTSTATUS SpoofMachineGuid(_In_ PCWSTR NewGuid) {
    NTSTATUS status;
    
    // 1. Registry Spoofing (Basic)
    status = ModifyRegistryValue(
        L"\\Registry\\Machine\\SOFTWARE\\Microsoft\\Cryptography",
        L"MachineGuid",
        NewGuid
    );
    
    // 2. Kernel Memory Spoofing (Advanced)
    if (NT_SUCCESS(status)) {
        status = PatchSmbiosStructure(1, (PVOID)NewGuid, (wcslen(NewGuid) + 1) * sizeof(WCHAR));
    }
    
    return status;
}

NTSTATUS SpoofBiosSerial(_In_ PCWSTR NewSerial) {
    NTSTATUS status;
    
    // 1. Registry Spoofing
    status = ModifyRegistryValue(
        L"\\Registry\\Machine\\HARDWARE\\DESCRIPTION\\System\\BIOS",
        L"SystemSerialNumber",
        NewSerial
    );
    
    // 2. SMBIOS Table Spoofing
    if (NT_SUCCESS(status)) {
        status = PatchSmbiosStructure(0, (PVOID)NewSerial, (wcslen(NewSerial) + 1) * sizeof(WCHAR));
    }
    
    return status;
}

NTSTATUS SpoofBaseBoardSerial(_In_ PCWSTR NewSerial) {
    NTSTATUS status;
    
    // 1. Registry Spoofing
    status = ModifyRegistryValue(
        L"\\Registry\\Machine\\HARDWARE\\DESCRIPTION\\System\\BIOS",
        L"BaseBoardSerialNumber",
        NewSerial
    );
    
    // 2. SMBIOS Table Spoofing
    if (NT_SUCCESS(status)) {
        status = PatchSmbiosStructure(2, (PVOID)NewSerial, (wcslen(NewSerial) + 1) * sizeof(WCHAR));
    }
    
    return status;
}

NTSTATUS SpoofDiskSerial(_In_ PCWSTR NewSerial) {
    NTSTATUS status;
    
    // 1. Registry Spoofing (Basic)
    status = ModifyRegistryValue(
        L"\\Registry\\Machine\\SYSTEM\\CurrentControlSet\\Services\\Disk\\Enum",
        L"DiskSerial",
        NewSerial
    );

    // 2. Kernel Hook Spoofing (Advanced)
    RtlStringCchCopyW(g_SpoofedDiskSerial, ARRAYSIZE(g_SpoofedDiskSerial), NewSerial);
    g_DiskSpoofingActive = TRUE;
    
    // Ensure hook is installed
    if (g_DiskHookContext.DriverObject == NULL) {
        InitializeDiskHook();
    }
    
    return status;
}

NTSTATUS SpoofGpuIdentifier(_In_ PCWSTR NewIdentifier) {
    return ModifyRegistryValue(
        L"\\Registry\\Machine\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000",
        L"AdapterString",
        NewIdentifier
    );
}

NTSTATUS SpoofMacAddress(_In_ PCWSTR NewMac) {
    UNICODE_STRING macStr;
    WCHAR interfaceKey[256];
    NTSTATUS status;
    
    // Basic Registry Spoofing
    // Note: To be fully effective, the network adapter needs to be restarted.
    // The driver should ideally issue a PnP request to restart it, but that's complex.
    // For now, we rely on the registry change which is the standard way.
    
    // Iterate through first 10 interfaces to find the active one (simplified)
    for (int i = 0; i < 10; i++) {
        status = RtlStringCchPrintfW(
            interfaceKey,
            ARRAYSIZE(interfaceKey),
            L"\\Registry\\Machine\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4D36E972-E325-11CE-BFC1-08002BE10318}\\%04d",
            i
        );
        
        if (NT_SUCCESS(status)) {
            // Check if this interface exists and has a DriverDesc
            WCHAR buffer[256];
            if (NT_SUCCESS(QueryRegistryValue(interfaceKey, L"DriverDesc", buffer, sizeof(buffer)))) {
                // Apply MAC spoof to this interface
                ModifyRegistryValue(interfaceKey, L"NetworkAddress", NewMac);
            }
        }
    }
    
    return STATUS_SUCCESS;
}

NTSTATUS SpoofSmbiosData(_In_ PVOID NewData, _In_ ULONG DataSize) {
    PHYSICAL_ADDRESS smbiosAddress = {0};
    PVOID mappedSmbios = NULL;
    NTSTATUS status = STATUS_SUCCESS;
    
    // Warning: Direct physical memory writing is dangerous and can be blocked by VBS/HVCI.
    // Use with caution.
    
    smbiosAddress = MmGetPhysicalAddress(NewData);
    if (smbiosAddress.QuadPart == 0) {
        return STATUS_INVALID_PARAMETER;
    }
    
    mappedSmbios = MmMapIoSpace(smbiosAddress, DataSize, MmNonCached);
    if (!mappedSmbios) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    
    __try {
        RtlCopyMemory(mappedSmbios, NewData, DataSize);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        status = GetExceptionCode();
    }
    
    MmUnmapIoSpace(mappedSmbios, DataSize);
    return status;
}

NTSTATUS SpoofAcpiTables(_In_ PVOID NewData, _In_ ULONG DataSize) {
    return PatchAcpiTable("DSDT", NewData, DataSize);
}

NTSTATUS RestoreMachineGuid(_In_ PCWSTR OriginalGuid) {
    return SpoofMachineGuid(OriginalGuid);
}

NTSTATUS RestoreBiosSerial(_In_ PCWSTR OriginalSerial) {
    return SpoofBiosSerial(OriginalSerial);
}

NTSTATUS RestoreBaseBoardSerial(_In_ PCWSTR OriginalSerial) {
    return SpoofBaseBoardSerial(OriginalSerial);
}

NTSTATUS RestoreDiskSerial(_In_ PCWSTR OriginalSerial) {
    g_DiskSpoofingActive = FALSE;
    // RemoveDiskHook(); // Optional: keep hook but inactive
    return SpoofDiskSerial(OriginalSerial);
}

NTSTATUS RestoreGpuIdentifier(_In_ PCWSTR OriginalIdentifier) {
    return SpoofGpuIdentifier(OriginalIdentifier);
}

NTSTATUS RestoreMacAddress(_In_ PCWSTR OriginalMac) {
    return SpoofMacAddress(OriginalMac);
}

NTSTATUS QueryMachineGuid(_Out_ PWSTR Buffer, _In_ ULONG BufferSize) {
    return QueryRegistryValue(
        L"\\Registry\\Machine\\SOFTWARE\\Microsoft\\Cryptography",
        L"MachineGuid",
        Buffer,
        BufferSize
    );
}

NTSTATUS QueryBiosSerial(_Out_ PWSTR Buffer, _In_ ULONG BufferSize) {
    return QueryRegistryValue(
        L"\\Registry\\Machine\\HARDWARE\\DESCRIPTION\\System\\BIOS",
        L"SystemSerialNumber",
        Buffer,
        BufferSize
    );
}

NTSTATUS QueryBaseBoardSerial(_Out_ PWSTR Buffer, _In_ ULONG BufferSize) {
    return QueryRegistryValue(
        L"\\Registry\\Machine\\HARDWARE\\DESCRIPTION\\System\\BIOS",
        L"BaseBoardSerialNumber",
        Buffer,
        BufferSize
    );
}

NTSTATUS QueryDiskSerial(_Out_ PWSTR Buffer, _In_ ULONG BufferSize) {
    return QueryRegistryValue(
        L"\\Registry\\Machine\\SYSTEM\\CurrentControlSet\\Services\\Disk\\Enum",
        L"DiskSerial",
        Buffer,
        BufferSize
    );
}

NTSTATUS QueryGpuIdentifier(_Out_ PWSTR Buffer, _In_ ULONG BufferSize) {
    return QueryRegistryValue(
        L"\\Registry\\Machine\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000",
        L"AdapterString",
        Buffer,
        BufferSize
    );
}

NTSTATUS QueryMacAddress(_Out_ PWSTR Buffer, _In_ ULONG BufferSize) {
    WCHAR interfaceKey[256];
    NTSTATUS status;
    
    status = RtlStringCchPrintfW(
        interfaceKey,
        ARRAYSIZE(interfaceKey),
        L"\\Registry\\Machine\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4D36E972-E325-11CE-BFC1-08002BE10318}\\%04d",
        0
    );
    
    if (!NT_SUCCESS(status)) {
        return status;
    }
    
    return QueryRegistryValue(interfaceKey, L"NetworkAddress", Buffer, BufferSize);
}

// Global variables for SMBIOS manipulation
PVOID g_SmbiosPhysicalAddress = NULL;
PVOID g_SmbiosMappedAddress = NULL;
ULONG g_SmbiosTableSize = 0;

// Global variables for ACPI manipulation
PVOID g_AcpiRsdp = NULL;

// Kernel callback routines
VOID ProcessNotifyRoutine(_In_ HANDLE ParentId, _In_ HANDLE ProcessId, _In_ BOOLEAN Create);
VOID ThreadNotifyRoutine(_In_ HANDLE ProcessId, _In_ HANDLE ThreadId, _In_ BOOLEAN Create);
VOID LoadImageNotifyRoutine(_In_ PUNICODE_STRING FullImageName, _In_ HANDLE ProcessId, _In_ PIMAGE_INFO ImageInfo);
NTSTATUS RegistryNotifyRoutine(_In_ PVOID CallbackContext, _In_ PVOID Argument1, _In_ PVOID Argument2);

NTSTATUS LocateSmbiosTables() {
    NTSTATUS status = STATUS_SUCCESS;
    PHYSICAL_ADDRESS highestAddress;
    highestAddress.QuadPart = MAXULONG64;
    
    // Try to find SMBIOS tables using system firmware table APIs
    ULONG bufferSize = 0;
    status = ZwQuerySystemInformation(SystemFirmwareTableInformation, NULL, 0, &bufferSize);
    
    if (status == STATUS_INFO_LENGTH_MISMATCH && bufferSize > 0) {
        PVOID buffer = ExAllocatePoolWithTag(NonPagedPoolNx, bufferSize, DRIVER_TAG);
        if (buffer) {
            status = ZwQuerySystemInformation(SystemFirmwareTableInformation, buffer, bufferSize, &bufferSize);
            if (NT_SUCCESS(status)) {
                PSYSTEM_FIRMWARE_TABLE_INFORMATION firmwareInfo = (PSYSTEM_FIRMWARE_TABLE_INFORMATION)buffer;
                g_SmbiosPhysicalAddress = firmwareInfo->TableBuffer;
                g_SmbiosTableSize = firmwareInfo->TableBufferLength;
                
                // Map the physical memory
                g_SmbiosMappedAddress = MmMapIoSpace(*(PHYSICAL_ADDRESS*)&g_SmbiosPhysicalAddress, 
                                                   g_SmbiosTableSize, MmNonCached);
                if (!g_SmbiosMappedAddress) {
                    status = STATUS_INSUFFICIENT_RESOURCES;
                }
            }
            ExFreePoolWithTag(buffer, DRIVER_TAG);
        }
    }
    
    return status;
}

NTSTATUS PatchSmbiosStructure(_In_ UCHAR Type, _In_ PVOID NewData, _In_ ULONG DataSize) {
    NTSTATUS status = STATUS_SUCCESS;
    
    if (!g_SmbiosMappedAddress) {
        status = LocateSmbiosTables();
        if (!NT_SUCCESS(status)) {
            return status;
        }
    }
    
    __try {
        PUCHAR current = (PUCHAR)g_SmbiosMappedAddress;
        PUCHAR end = current + g_SmbiosTableSize;
        
        while (current < end - 4) {
            UCHAR structureType = current[0];
            UCHAR structureLength = current[1];
            
            if (structureType == Type && structureLength >= 4) {
                // Found the target structure type
                if (DataSize <= structureLength - 4) {
                    // Copy the new data into the structure (skip type and length fields)
                    RtlCopyMemory(current + 4, NewData, DataSize);
                    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, 
                              "UmbrellaKernelDriver: Patched SMBIOS type %u\n", Type));
                    break;
                } else {
                    status = STATUS_BUFFER_TOO_SMALL;
                    break;
                }
            }
            
            // Move to next structure
            current += structureLength;
            
            // Skip string area (double null terminated)
            while (current < end - 1 && !(current[0] == 0 && current[1] == 0)) {
                current++;
            }
            current += 2; // Skip the double null terminator
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        status = GetExceptionCode();
    }
    
    return status;
}

NTSTATUS PatchAcpiTable(_In_ PCHAR Signature, _In_ PVOID NewData, _In_ ULONG DataSize) {
    NTSTATUS status = STATUS_SUCCESS;
    
    // Locate ACPI RSDP if not already found
    if (!g_AcpiRsdp) {
        PHYSICAL_ADDRESS rsdpAddress = {0};
        
        // Try to find RSDP in BIOS memory areas
        for (ULONG64 address = 0x000E0000; address < 0x000FFFFF; address += 16) {
            PHYSICAL_ADDRESS physAddr;
            physAddr.QuadPart = address;
            
            PVOID mapped = MmMapIoSpace(physAddr, 16, MmNonCached);
            if (mapped) {
                if (RtlCompareMemory(mapped, "RSD PTR ", 8) == 8) {
                    g_AcpiRsdp = mapped;
                    break;
                }
                MmUnmapIoSpace(mapped, 16);
            }
        }
        
        if (!g_AcpiRsdp) {
            return STATUS_NOT_FOUND;
        }
    }
    
    __try {
        // Parse ACPI tables and locate the target table
        PACPI_DESCRIPTION_HEADER rsdp = (PACPI_DESCRIPTION_HEADER)g_AcpiRsdp;
        
        // This is a simplified implementation - real ACPI patching would require
        // parsing the RSDT/XSDT and locating specific tables
        if (RtlCompareMemory(rsdp->Signature, Signature, 4) == 4) {
            if (DataSize <= sizeof(ACPI_DESCRIPTION_HEADER)) {
                RtlCopyMemory(rsdp, NewData, DataSize);
            } else {
                status = STATUS_BUFFER_TOO_SMALL;
            }
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        status = GetExceptionCode();
    }
    
    return status;
}
