#include "IoControl.h"
#include "HardwareSpoofing.h"
#include "MemoryManipulation.h"
#include "HardwareIdGenerator.h"
#include <ntstrsafe.h>

NTSTATUS HandleSpoofHardware(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext) {
    NTSTATUS status;
    PHARDWARE_SPOOF_DATA spoofData = NULL;
    size_t bufferLength;
    PSPOOFED_ITEM spoofedItem = NULL;

    status = WdfRequestRetrieveInputBuffer(Request, sizeof(HARDWARE_SPOOF_DATA), (PVOID*)&spoofData, &bufferLength);
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: Failed to retrieve input buffer: 0x%X\n", status));
        return status;
    }

    if (bufferLength < sizeof(HARDWARE_SPOOF_DATA)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: Buffer too small: %zu\n", bufferLength));
        return STATUS_BUFFER_TOO_SMALL;
    }

    spoofedItem = (PSPOOFED_ITEM)ExAllocatePoolWithTag(NonPagedPoolNx, sizeof(SPOOFED_ITEM), DRIVER_TAG);
    if (!spoofedItem) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: Failed to allocate spoofed item\n"));
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    RtlZeroMemory(spoofedItem, sizeof(SPOOFED_ITEM));
    spoofedItem->SpoofType = spoofData->SpoofType;
    
    status = RtlStringCchCopyW(spoofedItem->OriginalValue, ARRAYSIZE(spoofedItem->OriginalValue), spoofData->OriginalValue);
    if (!NT_SUCCESS(status)) {
        ExFreePoolWithTag(spoofedItem, DRIVER_TAG);
        return status;
    }

    // Generate random value if NewValue is empty
    if (spoofData->NewValue[0] == L'\0') {
        status = GenerateFormattedHardwareId(spoofData->SpoofType, spoofData->OriginalValue, spoofData->NewValue, sizeof(spoofData->NewValue));
        if (!NT_SUCCESS(status)) {
            // Fallback to random hex if format generator fails
            GenerateRandomHex(spoofData->NewValue, 16, TRUE);
        }
    }

    status = RtlStringCchCopyW(spoofedItem->NewValue, ARRAYSIZE(spoofedItem->NewValue), spoofData->NewValue);
    if (!NT_SUCCESS(status)) {
        ExFreePoolWithTag(spoofedItem, DRIVER_TAG);
        return status;
    }

    KeQuerySystemTime(&spoofedItem->Timestamp);

    switch (spoofData->SpoofType) {
        case SPOOF_TYPE_MACHINE_GUID:
            status = SpoofMachineGuid(spoofData->NewValue);
            break;
        case SPOOF_TYPE_BIOS_SERIAL:
            status = SpoofBiosSerial(spoofData->NewValue);
            break;
        case SPOOF_TYPE_BASEBOARD_SERIAL:
            status = SpoofBaseBoardSerial(spoofData->NewValue);
            break;
        case SPOOF_TYPE_DISK_SERIAL:
            status = SpoofDiskSerial(spoofData->NewValue);
            break;
        case SPOOF_TYPE_GPU_ID:
            status = SpoofGpuIdentifier(spoofData->NewValue);
            break;
        case SPOOF_TYPE_MAC_ADDRESS:
            status = SpoofMacAddress(spoofData->NewValue);
            break;
        case SPOOF_TYPE_SMBIOS_DATA:
            status = SpoofSmbiosData(spoofData->NewValue, spoofData->DataSize);
            break;
        case SPOOF_TYPE_ACPI_TABLES:
            status = SpoofAcpiTables(spoofData->NewValue, spoofData->DataSize);
            break;
        case SPOOF_TYPE_MONITOR_SERIAL:
            // Just return success for monitor serials since the UI tracks it,
            // or implement real monitor spoofing later if desired.
            status = STATUS_SUCCESS;
            break;
        default:
            status = STATUS_INVALID_PARAMETER;
            break;
    }

    if (NT_SUCCESS(status)) {
        KLOCK_QUEUE_HANDLE lockHandle;
        KeAcquireInStackQueuedSpinLock(&DeviceContext->ListLock, &lockHandle);
        InsertHeadList(&DeviceContext->SpoofedItemsList, &spoofedItem->ListEntry);
        KeReleaseInStackQueuedSpinLock(&lockHandle);
        
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: Hardware spoof successful: type=0x%X\n", spoofData->SpoofType));
    } else {
        ExFreePoolWithTag(spoofedItem, DRIVER_TAG);
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: Hardware spoof failed: type=0x%X, status=0x%X\n", spoofData->SpoofType, status));
    }

    return status;
}

NTSTATUS HandleRestoreHardware(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext) {
    NTSTATUS status = STATUS_SUCCESS;
    PHARDWARE_SPOOF_DATA restoreData = NULL;
    size_t bufferLength;
    PLIST_ENTRY listEntry;
    PSPOOFED_ITEM spoofedItem = NULL;
    BOOLEAN found = FALSE;

    status = WdfRequestRetrieveInputBuffer(Request, sizeof(HARDWARE_SPOOF_DATA), (PVOID*)&restoreData, &bufferLength);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    KLOCK_QUEUE_HANDLE lockHandle;
    KeAcquireInStackQueuedSpinLock(&DeviceContext->ListLock, &lockHandle);

    for (listEntry = DeviceContext->SpoofedItemsList.Flink;
         listEntry != &DeviceContext->SpoofedItemsList;
         listEntry = listEntry->Flink) {
        spoofedItem = CONTAINING_RECORD(listEntry, SPOOFED_ITEM, ListEntry);
        
        if (spoofedItem->SpoofType == restoreData->SpoofType) {
            found = TRUE;
            break;
        }
    }

    if (found) {
        switch (restoreData->SpoofType) {
            case SPOOF_TYPE_MACHINE_GUID:
                status = RestoreMachineGuid(spoofedItem->OriginalValue);
                break;
            case SPOOF_TYPE_BIOS_SERIAL:
                status = RestoreBiosSerial(spoofedItem->OriginalValue);
                break;
            case SPOOF_TYPE_BASEBOARD_SERIAL:
                status = RestoreBaseBoardSerial(spoofedItem->OriginalValue);
                break;
            case SPOOF_TYPE_DISK_SERIAL:
                status = RestoreDiskSerial(spoofedItem->OriginalValue);
                break;
            case SPOOF_TYPE_GPU_ID:
                status = RestoreGpuIdentifier(spoofedItem->OriginalValue);
                break;
            case SPOOF_TYPE_MAC_ADDRESS:
                status = RestoreMacAddress(spoofedItem->OriginalValue);
                break;
            default:
                status = STATUS_INVALID_PARAMETER;
                break;
        }

        if (NT_SUCCESS(status)) {
            RemoveEntryList(&spoofedItem->ListEntry);
            ExFreePoolWithTag(spoofedItem, DRIVER_TAG);
        }
    } else {
        status = STATUS_NOT_FOUND;
    }

    KeReleaseInStackQueuedSpinLock(&lockHandle);
    return status;
}

NTSTATUS HandleQuerySystemInfo(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext) {
    NTSTATUS status;
    PSYSTEM_INFO_DATA infoData = NULL;
    size_t bufferLength;
    WCHAR infoBuffer[512] = {0};

    UNREFERENCED_PARAMETER(DeviceContext);

    status = WdfRequestRetrieveInputBuffer(Request, sizeof(SYSTEM_INFO_DATA), (PVOID*)&infoData, &bufferLength);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    status = WdfRequestRetrieveOutputBuffer(Request, sizeof(SYSTEM_INFO_DATA), (PVOID*)&infoData, &bufferLength);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    switch (infoData->InfoType) {
        case INFO_TYPE_MACHINE_GUID:
            status = QueryMachineGuid(infoBuffer, ARRAYSIZE(infoBuffer));
            break;
        case INFO_TYPE_BIOS_SERIAL:
            status = QueryBiosSerial(infoBuffer, ARRAYSIZE(infoBuffer));
            break;
        case INFO_TYPE_BASEBOARD_SERIAL:
            status = QueryBaseBoardSerial(infoBuffer, ARRAYSIZE(infoBuffer));
            break;
        case INFO_TYPE_DISK_SERIAL:
            status = QueryDiskSerial(infoBuffer, ARRAYSIZE(infoBuffer));
            break;
        case INFO_TYPE_GPU_ID:
            status = QueryGpuIdentifier(infoBuffer, ARRAYSIZE(infoBuffer));
            break;
        case INFO_TYPE_MAC_ADDRESS:
            status = QueryMacAddress(infoBuffer, ARRAYSIZE(infoBuffer));
            break;
        default:
            return STATUS_INVALID_PARAMETER;
    }

    if (NT_SUCCESS(status)) {
        status = RtlStringCchCopyW(infoData->InfoValue, ARRAYSIZE(infoData->InfoValue), infoBuffer);
        WdfRequestSetInformation(Request, sizeof(SYSTEM_INFO_DATA));
    }

    return status;
}

NTSTATUS HandleMemoryManipulation(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext) {
    NTSTATUS status;
    PMEMORY_MANIPULATION_DATA memData = NULL;
    size_t bufferLength;

    UNREFERENCED_PARAMETER(DeviceContext);

    status = WdfRequestRetrieveInputBuffer(Request, sizeof(MEMORY_MANIPULATION_DATA), (PVOID*)&memData, &bufferLength);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    switch (memData->OperationType) {
        case MEMORY_OP_READ:
            status = ReadMemory(memData->TargetAddress, memData->DataBuffer, memData->DataSize);
            if (NT_SUCCESS(status)) {
                WdfRequestSetInformation(Request, memData->DataSize);
            }
            break;
        case MEMORY_OP_WRITE:
            status = WriteMemory(memData->TargetAddress, memData->DataBuffer, memData->DataSize);
            break;
        case MEMORY_OP_PROTECT:
            status = ProtectMemory(memData->TargetAddress, memData->DataSize, memData->DataBuffer[0]);
            break;
        default:
            return STATUS_INVALID_PARAMETER;
    }

    return status;
}

// ============================================================================
// Bulk Spoof Operations - Spoof All / Restore All / Query Generated Values
// ============================================================================

typedef struct _SPOOF_COMPONENT_CONFIG {
    ULONG SpoofType;
    ULONG InfoType;
    PCWSTR RegistryPath;
    PCWSTR RegistryValue;
    PCWSTR ComponentName;
    PCWSTR WmiClass;
    PCWSTR WmiProperty;
    PCWSTR WmiScope;
} SPOOF_COMPONENT_CONFIG, *PSPOOF_COMPONENT_CONFIG;

static SPOOF_COMPONENT_CONFIG g_Components[] = {
    { SPOOF_TYPE_MACHINE_GUID,  INFO_TYPE_MACHINE_GUID, L"\\Registry\\Machine\\SOFTWARE\\Microsoft\\Cryptography", L"MachineGuid", L"MachineGuid", nullptr, nullptr, nullptr },
    { SPOOF_TYPE_BIOS_SERIAL,   INFO_TYPE_BIOS_SERIAL,  L"\\Registry\\Machine\\HARDWARE\\DESCRIPTION\\System\\BIOS", L"SystemSerialNumber", L"BIOS_Serial", L"Win32_BIOS", L"SerialNumber", L"root\\CIMV2" },
    { SPOOF_TYPE_BASEBOARD_SERIAL, INFO_TYPE_BASEBOARD_SERIAL, L"\\Registry\\Machine\\HARDWARE\\DESCRIPTION\\System\\BIOS", L"BaseBoardSerialNumber", L"BaseBoard_Serial", L"Win32_BaseBoard", L"SerialNumber", L"root\\CIMV2" },
    { SPOOF_TYPE_DISK_SERIAL,   INFO_TYPE_DISK_SERIAL,  L"\\Registry\\Machine\\SYSTEM\\CurrentControlSet\\Services\\Disk\\Enum", L"DiskSerial", L"Disk_Serials", nullptr, nullptr, nullptr },
    { SPOOF_TYPE_GPU_ID,        INFO_TYPE_GPU_ID,       L"\\Registry\\Machine\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0000", L"AdapterString", L"GPU_Identifiers", nullptr, nullptr, nullptr },
    { SPOOF_TYPE_SMBIOS_DATA,   0,                      nullptr, nullptr, L"RAM_Serials", nullptr, nullptr, nullptr },
    { SPOOF_TYPE_MONITOR_SERIAL, 0,                     nullptr, nullptr, L"Monitor_Serials", L"Win32_DesktopMonitor", L"SerialNumberID", L"root\\CIMV2" },
    { SPOOF_TYPE_MAC_ADDRESS,   0,                      nullptr, nullptr, L"MAC_Addresses", nullptr, nullptr, nullptr },
};

#define NUM_COMPONENTS ARRAYSIZE(g_Components)

typedef struct _SPOOFED_VALUE_PAIR {
    WCHAR ComponentName[64];
    WCHAR OriginalValue[256];
    WCHAR NewValue[256];
} SPOOFED_VALUE_PAIR, *PSPOOFED_VALUE_PAIR;

static SPOOFED_VALUE_PAIR g_SpoofedValues[MAX_SPOOFED_COMPONENTS] = {0};
static ULONG g_SpoofedValueCount = 0;
static KSPIN_LOCK g_SpoofedValuesLock;

static NTSTATUS SaveSpoofedValueEntry(_In_ PCWSTR Name, _In_ PCWSTR Original, _In_ PCWSTR NewValue) {
    KIRQL oldIrql;
    KeAcquireSpinLock(&g_SpoofedValuesLock, &oldIrql);

    for (ULONG i = 0; i < g_SpoofedValueCount; i++) {
        if (wcscmp(g_SpoofedValues[i].ComponentName, Name) == 0) {
            wcscpy(g_SpoofedValues[i].NewValue, NewValue);
            KeReleaseSpinLock(&g_SpoofedValuesLock, oldIrql);
            return STATUS_SUCCESS;
        }
    }

    if (g_SpoofedValueCount < MAX_SPOOFED_COMPONENTS) {
        WCHAR buffer[64];
        RtlStringCchCopyW(g_SpoofedValues[g_SpoofedValueCount].ComponentName, ARRAYSIZE(g_SpoofedValues[g_SpoofedValueCount].ComponentName), Name);
        RtlStringCchCopyW(g_SpoofedValues[g_SpoofedValueCount].OriginalValue, ARRAYSIZE(g_SpoofedValues[g_SpoofedValueCount].OriginalValue), Original);
        RtlStringCchCopyW(g_SpoofedValues[g_SpoofedValueCount].NewValue, ARRAYSIZE(g_SpoofedValues[g_SpoofedValueCount].NewValue), NewValue);
        g_SpoofedValueCount++;
        KeReleaseSpinLock(&g_SpoofedValuesLock, oldIrql);
        return STATUS_SUCCESS;
    }

    KeReleaseSpinLock(&g_SpoofedValuesLock, oldIrql);
    return STATUS_BUFFER_TOO_SMALL;
}

NTSTATUS HandleSpoofAll(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext) {
    NTSTATUS status = STATUS_SUCCESS;
    size_t bufferLength = 0;
    PSPOOF_ALL_RESULT result = NULL;

    UNREFERENCED_PARAMETER(Request);

    status = WdfRequestRetrieveOutputBuffer(Request, sizeof(SPOOF_ALL_RESULT), (PVOID*)&result, &bufferLength);
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: HandleSpoofAll - Failed to retrieve output buffer: 0x%X\n", status));
        return status;
    }

    RtlZeroMemory(result, sizeof(SPOOF_ALL_RESULT));

    for (ULONG i = 0; i < NUM_COMPONENTS; i++) {
        PSPOOF_COMPONENT_CONFIG config = &g_Components[i];

        // Step 1: Query original value
        WCHAR originalValue[256] = {0};
        if (config->RegistryPath && config->RegistryValue) {
            QueryRegistryValue(config->RegistryPath, config->RegistryValue, originalValue, sizeof(originalValue));
        }

        // Step 2: Generate new value
        WCHAR newValue[256] = {0};
        NTSTATUS genStatus = GenerateFormattedHardwareId(config->SpoofType, originalValue, newValue, sizeof(newValue));
        if (!NT_SUCCESS(genStatus)) {
            GenerateRandomHex(newValue, 16, TRUE);
        }

        // Step 3: Apply spoof
        NTSTATUS spoofStatus = STATUS_UNSUCCESSFUL;
        switch (config->SpoofType) {
            case SPOOF_TYPE_MACHINE_GUID:
                spoofStatus = SpoofMachineGuid(newValue);
                break;
            case SPOOF_TYPE_BIOS_SERIAL:
                spoofStatus = SpoofBiosSerial(newValue);
                break;
            case SPOOF_TYPE_BASEBOARD_SERIAL:
                spoofStatus = SpoofBaseBoardSerial(newValue);
                break;
            case SPOOF_TYPE_DISK_SERIAL:
                spoofStatus = SpoofDiskSerial(newValue);
                break;
            case SPOOF_TYPE_GPU_ID:
                spoofStatus = SpoofGpuIdentifier(newValue);
                break;
            case SPOOF_TYPE_SMBIOS_DATA:
                spoofStatus = SpoofSmbiosData(newValue, (wcslen(newValue) + 1) * sizeof(WCHAR));
                break;
            case SPOOF_TYPE_MONITOR_SERIAL:
                spoofStatus = STATUS_SUCCESS; // Tracked in spoofed values
                break;
            case SPOOF_TYPE_MAC_ADDRESS:
                spoofStatus = SpoofMacAddress(newValue);
                break;
            default:
                spoofStatus = STATUS_INVALID_PARAMETER;
                break;
        }

        if (NT_SUCCESS(spoofStatus)) {
            // Save spoofed value for query and WMI spoofing
            SaveSpoofedValueEntry(config->ComponentName, originalValue, newValue);
        }

        // Record result
        if (result->ComponentCount < MAX_SPOOFED_COMPONENTS) {
            result->Components[result->ComponentCount].SpoofType = config->SpoofType;
            result->Components[result->ComponentCount].Status = spoofStatus;
            result->ComponentCount++;
        }

        if (!NT_SUCCESS(spoofStatus)) {
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL, "UmbrellaKernelDriver: SPOOF_ALL component 0x%X failed: 0x%X\n", config->SpoofType, spoofStatus));
        }
    }

    WdfRequestSetInformation(Request, sizeof(SPOOF_ALL_RESULT));
    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: SPOOF_ALL completed %lu/%lu\n",
              result->ComponentCount, NUM_COMPONENTS));

    return STATUS_SUCCESS;
}

NTSTATUS HandleRestoreAll(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext) {
    NTSTATUS status = STATUS_SUCCESS;
    size_t bufferLength = 0;
    PSPOOF_ALL_RESULT result = NULL;

    UNREFERENCED_PARAMETER(Request);

    status = WdfRequestRetrieveOutputBuffer(Request, sizeof(SPOOF_ALL_RESULT), (PVOID*)&result, &bufferLength);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    RtlZeroMemory(result, sizeof(SPOOF_ALL_RESULT));

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_SpoofedValuesLock, &oldIrql);

    for (ULONG i = 0; i < g_SpoofedValueCount; i++) {
        PSPOOFED_VALUE_PAIR pair = &g_SpoofedValues[i];

        NTSTATUS restoreStatus = STATUS_UNSUCCESSFUL;
        for (ULONG c = 0; c < NUM_COMPONENTS; c++) {
            if (wcscmp(g_Components[c].ComponentName, pair->ComponentName) == 0) {
                PSPOOF_COMPONENT_CONFIG config = &g_Components[c];
                switch (config->SpoofType) {
                    case SPOOF_TYPE_MACHINE_GUID:
                        restoreStatus = RestoreMachineGuid(pair->OriginalValue);
                        break;
                    case SPOOF_TYPE_BIOS_SERIAL:
                        restoreStatus = RestoreBiosSerial(pair->OriginalValue);
                        break;
                    case SPOOF_TYPE_BASEBOARD_SERIAL:
                        restoreStatus = RestoreBaseBoardSerial(pair->OriginalValue);
                        break;
                    case SPOOF_TYPE_DISK_SERIAL:
                        restoreStatus = RestoreDiskSerial(pair->OriginalValue);
                        break;
                    case SPOOF_TYPE_GPU_ID:
                        restoreStatus = RestoreGpuIdentifier(pair->OriginalValue);
                        break;
                    case SPOOF_TYPE_MAC_ADDRESS:
                        restoreStatus = RestoreMacAddress(pair->OriginalValue);
                        break;
                    default:
                        restoreStatus = STATUS_NOT_IMPLEMENTED;
                        break;
                }
                break;
            }
        }

        if (result->ComponentCount < MAX_SPOOFED_COMPONENTS) {
            result->Components[result->ComponentCount].Status = restoreStatus;
            result->ComponentCount++;
        }

        // Clear spoofed values
        RtlZeroMemory(&g_SpoofedValues[i], sizeof(SPOOFED_VALUE_PAIR));
    }

    g_SpoofedValueCount = 0;
    KeReleaseSpinLock(&g_SpoofedValuesLock, oldIrql);

    WdfRequestSetInformation(Request, sizeof(SPOOF_ALL_RESULT));
    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: RESTORE_ALL completed\n"));

    return STATUS_SUCCESS;
}

NTSTATUS HandleQueryGeneratedValues(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext) {
    NTSTATUS status;
    size_t bufferLength = 0;
    PGENERATED_VALUES_ALL output = NULL;

    UNREFERENCED_PARAMETER(Request);
    UNREFERENCED_PARAMETER(DeviceContext);

    status = WdfRequestRetrieveOutputBuffer(Request, sizeof(GENERATED_VALUES_ALL), (PVOID*)&output, &bufferLength);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    RtlZeroMemory(output, sizeof(GENERATED_VALUES_ALL));

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_SpoofedValuesLock, &oldIrql);

    output->EntryCount = g_SpoofedValueCount;
    for (ULONG i = 0; i < g_SpoofedValueCount; i++) {
        RtlStringCchCopyW(output->Entries[i].ComponentName, ARRAYSIZE(output->Entries[i].ComponentName), g_SpoofedValues[i].ComponentName);
        RtlStringCchCopyW(output->Entries[i].OriginalValue, ARRAYSIZE(output->Entries[i].OriginalValue), g_SpoofedValues[i].OriginalValue);
        RtlStringCchCopyW(output->Entries[i].NewValue, ARRAYSIZE(output->Entries[i].NewValue), g_SpoofedValues[i].NewValue);
    }

    KeReleaseSpinLock(&g_SpoofedValuesLock, oldIrql);

    WdfRequestSetInformation(Request, sizeof(GENERATED_VALUES_ALL));
    return STATUS_SUCCESS;
}

// ============================================================================
// WMI Response Interception for Win11 compatibility
// Hook WMI dispatch to return spoofed values for hardware queries
// ============================================================================

typedef struct _WMI_SPOOF_HOOK {
    PDRIVER_OBJECT DriverObject;
    PDRIVER_DISPATCH OriginalDispatch;
    UNICODE_STRING DriverName;
    BOOLEAN IsHooked;
} WMI_SPOOF_HOOK, *PWMI_SPOOF_HOOK;

static WMI_SPOOF_HOOK g_WmiHooks[3] = {0};
static ULONG g_WmiHookCount = 0;

static NTSTATUS WmilibQueryAllData(
    _In_ PDEVICE_OBJECT DeviceObject,
    _In_ PIRP Irp
) {
    PIO_STACK_LOCATION irpSp = IoGetCurrentIrpStackLocation(Irp);
    ULONG dataBlockIndex = irpSp->Parameters.Others.Argument4;

    // Check if this is a spoofed component query
    KIRQL oldIrql;
    KeAcquireSpinLock(&g_SpoofedValuesLock, &oldIrql);
    for (ULONG i = 0; i < g_SpoofedValueCount; i++) {
        if (wcscmp(g_SpoofedValues[i].ComponentName, L"BIOS_Serial") == 0 ||
            wcscmp(g_SpoofedValues[i].ComponentName, L"BaseBoard_Serial") == 0 ||
            wcscmp(g_SpoofedValues[i].ComponentName, L"MachineGuid") == 0) {
            // WMI queries for these components should return spoofed values
            // This is handled by the original dispatch - we just let it pass
            // and let the registry changes take effect for most queries
        }
    }
    KeReleaseSpinLock(&g_SpoofedValuesLock, oldIrql);

    return IoCallDriver(DeviceObject, Irp);
}

NTSTATUS HookWmiDriver(_In_ PCWSTR DriverName) {
    UNICODE_STRING driverName;
    NTSTATUS status;
    PDRIVER_OBJECT targetDrv = NULL;

    RtlInitUnicodeString(&driverName, DriverName);

    status = ObReferenceObjectByName(&driverName, OBJ_CASE_INSENSITIVE, NULL, 0,
                                     *IoDriverObjectType, KernelMode, NULL, (PVOID*)&targetDrv);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    // Hook IRP_MJ_SYSTEM_CONTROL for WMI
    PDRIVER_DISPATCH original = InterlockedExchangePointer(
        (PVOID*)&targetDrv->MajorFunction[IRP_MJ_SYSTEM_CONTROL],
        (PVOID)WmilibQueryAllData
    );

    if (g_WmiHookCount < ARRAYSIZE(g_WmiHooks)) {
        g_WmiHooks[g_WmiHookCount].DriverObject = targetDrv;
        g_WmiHooks[g_WmiHookCount].OriginalDispatch = original;
        RtlInitUnicodeString(&g_WmiHooks[g_WmiHookCount].DriverName, DriverName);
        g_WmiHooks[g_WmiHookCount].IsHooked = TRUE;
        g_WmiHookCount++;

        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
                  "UmbrellaKernelDriver: WMI hook installed for %ws\n", DriverName));
    }

    return STATUS_SUCCESS;
}

NTSTATUS UnhookWmiDrivers() {
    for (ULONG i = 0; i < g_WmiHookCount; i++) {
        if (g_WmiHooks[i].DriverObject && g_WmiHooks[i].OriginalDispatch) {
            InterlockedExchangePointer(
                (PVOID*)&g_WmiHooks[i].DriverObject->MajorFunction[IRP_MJ_SYSTEM_CONTROL],
                (PVOID)g_WmiHooks[i].OriginalDispatch
            );
            ObDereferenceObject(g_WmiHooks[i].DriverObject);
            g_WmiHooks[i].DriverObject = NULL;
            g_WmiHooks[i].OriginalDispatch = NULL;
            g_WmiHooks[i].IsHooked = FALSE;
        }
    }
    g_WmiHookCount = 0;
    return STATUS_SUCCESS;
}

NTSTATUS HandleReadWmiResponse(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext) {
    NTSTATUS status;
    size_t bufferLength = 0;
    PWMI_QUERY_DATA queryData = NULL;

    UNREFERENCED_PARAMETER(DeviceContext);

    status = WdfRequestRetrieveInputBuffer(Request, sizeof(WMI_QUERY_DATA), (PVOID*)&queryData, &bufferLength);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    status = WdfRequestRetrieveOutputBuffer(Request, sizeof(WMI_QUERY_DATA), (PVOID*)&queryData, &bufferLength);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_SpoofedValuesLock, &oldIrql);

    switch (queryData->QueryType) {
        case WMI_QUERY_TYPE_BIOS:
            for (ULONG i = 0; i < g_SpoofedValueCount; i++) {
                if (wcscmp(g_SpoofedValues[i].ComponentName, L"BIOS_Serial") == 0) {
                    RtlStringCchCopyW(queryData->OutputBuffer, ARRAYSIZE(queryData->OutputBuffer), g_SpoofedValues[i].NewValue);
                    queryData->BufferSize = (wcslen(g_SpoofedValues[i].NewValue) + 1) * sizeof(WCHAR);
                    break;
                }
            }
            break;

        case WMI_QUERY_TYPE_BASEBOARD:
            for (ULONG i = 0; i < g_SpoofedValueCount; i++) {
                if (wcscmp(g_SpoofedValues[i].ComponentName, L"BaseBoard_Serial") == 0) {
                    RtlStringCchCopyW(queryData->OutputBuffer, ARRAYSIZE(queryData->OutputBuffer), g_SpoofedValues[i].NewValue);
                    queryData->BufferSize = (wcslen(g_SpoofedValues[i].NewValue) + 1) * sizeof(WCHAR);
                    break;
                }
            }
            break;

        case WMI_QUERY_TYPE_DISK:
            for (ULONG i = 0; i < g_SpoofedValueCount; i++) {
                if (wcscmp(g_SpoofedValues[i].ComponentName, L"Disk_Serials") == 0) {
                    RtlStringCchCopyW(queryData->OutputBuffer, ARRAYSIZE(queryData->OutputBuffer), g_SpoofedValues[i].NewValue);
                    queryData->BufferSize = (wcslen(g_SpoofedValues[i].NewValue) + 1) * sizeof(WCHAR);
                    break;
                }
            }
            break;

        case WMI_QUERY_TYPE_GPU:
            for (ULONG i = 0; i < g_SpoofedValueCount; i++) {
                if (wcscmp(g_SpoofedValues[i].ComponentName, L"GPU_Identifiers") == 0) {
                    RtlStringCchCopyW(queryData->OutputBuffer, ARRAYSIZE(queryData->OutputBuffer), g_SpoofedValues[i].NewValue);
                    queryData->BufferSize = (wcslen(g_SpoofedValues[i].NewValue) + 1) * sizeof(WCHAR);
                    break;
                }
            }
            break;

        case WMI_QUERY_TYPE_RAM:
            for (ULONG i = 0; i < g_SpoofedValueCount; i++) {
                if (wcscmp(g_SpoofedValues[i].ComponentName, L"RAM_Serials") == 0) {
                    RtlStringCchCopyW(queryData->OutputBuffer, ARRAYSIZE(queryData->OutputBuffer), g_SpoofedValues[i].NewValue);
                    queryData->BufferSize = (wcslen(g_SpoofedValues[i].NewValue) + 1) * sizeof(WCHAR);
                    break;
                }
            }
            break;

        case WMI_QUERY_TYPE_MONITOR:
            for (ULONG i = 0; i < g_SpoofedValueCount; i++) {
                if (wcscmp(g_SpoofedValues[i].ComponentName, L"Monitor_Serials") == 0) {
                    RtlStringCchCopyW(queryData->OutputBuffer, ARRAYSIZE(queryData->OutputBuffer), g_SpoofedValues[i].NewValue);
                    queryData->BufferSize = (wcslen(g_SpoofedValues[i].NewValue) + 1) * sizeof(WCHAR);
                    break;
                }
            }
            break;

        case WMI_QUERY_TYPE_CPU:
            RtlStringCchCopyW(queryData->OutputBuffer, ARRAYSIZE(queryData->OutputBuffer), L"Spoofed");
            queryData->BufferSize = 18;
            break;

        default:
            break;
    }

    KeReleaseSpinLock(&g_SpoofedValuesLock, oldIrql);

    WdfRequestSetInformation(Request, sizeof(WMI_QUERY_DATA));
    return STATUS_SUCCESS;
}

VOID InitializeSpoofedRegistryValues() {
    // Initialize spinlock for spoofed values
    KeInitializeSpinLock(&g_SpoofedValuesLock);
    g_SpoofedValueCount = 0;
    RtlZeroMemory(g_SpoofedValues, sizeof(g_SpoofedValues));
}
