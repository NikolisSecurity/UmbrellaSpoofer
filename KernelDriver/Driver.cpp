#include "Driver.h"
#include "HardwareSpoofing.h"
#include "MemoryManipulation.h"
#include "IoControl.h"
#include "StringObfuscation.h"
#include "TpmSpoofing.h"
#include <ntstrsafe.h>

// Forward declarations
EVT_WDF_DRIVER_DEVICE_ADD UmbrellaEvtDeviceAdd;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL UmbrellaEvtIoDeviceControl;
EVT_WDF_IO_QUEUE_IO_STOP UmbrellaEvtIoStop;
EVT_WDF_OBJECT_CONTEXT_CLEANUP UmbrellaEvtDriverContextCleanup;
EVT_WDF_OBJECT_CONTEXT_CLEANUP UmbrellaEvtDeviceContextCleanup;
VOID InitializeRandom();

// Global variable to store the original NtDeviceIoControlFile address
PNtDeviceIoControlFile OriginalNtDeviceIoControlFile = NULL;
PVOID HookedNtDeviceIoControlFile = NULL; // To store the trampoline for unhooking

// Our hook function for NtDeviceIoControlFile
NTSTATUS MyNtDeviceIoControlFile(
    _In_ HANDLE FileHandle,
    _In_opt_ HANDLE Event,
    _In_opt_ PIO_APC_ROUTINE ApcRoutine,
    _In_opt_ PVOID ApcContext,
    _Out_ PIO_STATUS_BLOCK IoStatusBlock,
    _In_ ULONG IoControlCode,
    _In_opt_ PVOID InputBuffer,
    _In_ ULONG InputBufferLength,
    _Out_opt_ PVOID OutputBuffer,
    _In_ ULONG OutputBufferLength
) {
    PFILE_OBJECT fileObject = NULL;
    PDEVICE_OBJECT deviceObject = NULL;
    NTSTATUS status = STATUS_SUCCESS;
    BOOLEAN isTpmDevice = FALSE;

    // Get the FILE_OBJECT from the FileHandle
    status = ObReferenceObjectByHandle(
        FileHandle,
        0, // DesiredAccess, 0 for any access
        *IoFileObjectType,
        KernelMode,
        (PVOID*)&fileObject,
        NULL
    );

    if (NT_SUCCESS(status) && fileObject != NULL) {
        // Get the DEVICE_OBJECT from the FILE_OBJECT
        deviceObject = IoGetRelatedDeviceObject(fileObject);

        if (deviceObject != NULL && deviceObject->DriverObject != NULL) {
            // Check if the device belongs to TPM.sys
            UNICODE_STRING tpmDriverName;
            RtlInitUnicodeString(&tpmDriverName, L"\\Driver\\TPM");

            if (RtlCompareUnicodeString(&deviceObject->DriverObject->DriverName, &tpmDriverName, TRUE) == 0) {
                isTpmDevice = TRUE;
            }
        }
        ObDereferenceObject(fileObject); // Dereference the file object
    }

// ... (rest of the code)

// Inside MyNtDeviceIoControlFile in Driver.cpp
    if (isTpmDevice) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: IOCTL 0x%X for TPM.sys detected!\n", IoControlCode));

        // Log InputBuffer if available
        if (InputBuffer != NULL && InputBufferLength > 0) {
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: TPM InputBuffer (length %lu):\n", InputBufferLength));
            // Print first 64 bytes or less if buffer is smaller
            for (ULONG i = 0; i < min(InputBufferLength, 64); ++i) {
                KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "%02X ", ((PUCHAR)InputBuffer)[i]));
            }
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "\n"));
        }

        // --- SPOOFING LOGIC STARTS HERE ---
        // Placeholder IOCTLs - these need to be identified through dynamic analysis
        // Example: IOCTL for getting EK public key
        // Example: IOCTL for getting AIK public key
        // Example: IOCTL for reading PCRs

        // For demonstration, let's assume a hypothetical IOCTL for EK public key
        // This IOCTL value is a placeholder and needs to be replaced with a real one.
        #define IOCTL_TPM_GET_EK_PUBLIC_KEY CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800, METHOD_BUFFERED, FILE_READ_ACCESS)
        #define IOCTL_TPM_GET_PCR_VALUES CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_READ_ACCESS)

        switch (IoControlCode) {
            case IOCTL_TPM_GET_EK_PUBLIC_KEY: {
                if (OutputBuffer != NULL && OutputBufferLength >= sizeof(TPM2B_PUBLIC)) {
                    TPM2B_PUBLIC spoofedEkPublic;
                    GenerateSpoofedTpm2bPublic(&spoofedEkPublic);
                    RtlCopyMemory(OutputBuffer, &spoofedEkPublic, min(OutputBufferLength, sizeof(TPM2B_PUBLIC)));
                    IoStatusBlock->Information = min(OutputBufferLength, sizeof(TPM2B_PUBLIC));
                    status = STATUS_SUCCESS;
                    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: Spoofed EK Public Key!\n"));
                    return status; // Complete the request here
                }
                break;
            }
            case IOCTL_TPM_GET_PCR_VALUES: {
                // Assuming a request for 24 PCRs
                ULONG numPcr = 24;
                if (OutputBuffer != NULL && OutputBufferLength >= (numPcr * sizeof(TPM2B_DIGEST))) {
                    PTPM2B_DIGEST spoofedPcrArray = (PTPM2B_DIGEST)ExAllocatePoolWithTag(NonPagedPool, numPcr * sizeof(TPM2B_DIGEST), DRIVER_TAG);
                    if (spoofedPcrArray) {
                        GenerateSpoofedPcrValues(spoofedPcrArray, numPcr);
                        RtlCopyMemory(OutputBuffer, spoofedPcrArray, min(OutputBufferLength, numPcr * sizeof(TPM2B_DIGEST)));
                        IoStatusBlock->Information = min(OutputBufferLength, numPcr * sizeof(TPM2B_DIGEST));
                        ExFreePoolWithTag(spoofedPcrArray, DRIVER_TAG);
                        status = STATUS_SUCCESS;
                        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: Spoofed PCR Values!\n"));
                        return status; // Complete the request here
                    }
                }
                break;
            }
            // Add more cases for other TPM identity-related IOCTLs
            default:
                // For unknown TPM IOCTLs, let the original function handle it
                break;
        }
        // --- SPOOFING LOGIC ENDS HERE ---

        // Call the original function if not spoofed
        status = OriginalNtDeviceIoControlFile(
            FileHandle,
            Event,
            ApcRoutine,
            ApcContext,
            IoStatusBlock,
            IoControlCode,
            InputBuffer,
            InputBufferLength,
            OutputBuffer,
            OutputBufferLength
        );

        if (NT_SUCCESS(status) && OutputBuffer != NULL && OutputBufferLength > 0) {
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: TPM OutputBuffer (length %lu):\n", OutputBufferLength));
            // Print first 64 bytes or less if buffer is smaller
            for (ULONG i = 0; i < min(OutputBufferLength, 64); ++i) {
                KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "%02X ", ((PUCHAR)OutputBuffer)[i]));
            }
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "\n"));
        }
        return status; // Return here if it's a TPM device, after logging
    }

    // Call the original function for non-TPM devices
    return OriginalNtDeviceIoControlFile(
        FileHandle,
        Event,
        ApcRoutine,
        ApcContext,
        IoStatusBlock,
        IoControlCode,
        InputBuffer,
        InputBufferLength,
        OutputBuffer,
        OutputBufferLength
    );
}

NTSTATUS InitializeNtDeviceIoControlFileHook() {
    UNICODE_STRING functionName;
    RtlInitUnicodeString(&functionName, L"NtDeviceIoControlFile");

    OriginalNtDeviceIoControlFile = (PNtDeviceIoControlFile)MmGetSystemRoutineAddress(&functionName);

    if (OriginalNtDeviceIoControlFile == NULL) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: Failed to find NtDeviceIoControlFile\n"));
        return STATUS_NOT_FOUND;
    }

    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: NtDeviceIoControlFile found at %p\n", OriginalNtDeviceIoControlFile));
    return STATUS_SUCCESS;
}

NTSTATUS HookNtDeviceIoControlFile() {
    if (OriginalNtDeviceIoControlFile == NULL) {
        return STATUS_UNSUCCESSFUL;
    }

    return HookFunction(OriginalNtDeviceIoControlFile, MyNtDeviceIoControlFile, &HookedNtDeviceIoControlFile);
}

VOID UnhookNtDeviceIoControlFile() {
    if (OriginalNtDeviceIoControlFile != NULL && HookedNtDeviceIoControlFile != NULL) {
        UnhookFunction(OriginalNtDeviceIoControlFile, HookedNtDeviceIoControlFile);
        OriginalNtDeviceIoControlFile = NULL;
        HookedNtDeviceIoControlFile = NULL;
    }
}

// Function to hide the driver from PsLoadedModuleList
VOID HideDriver(_In_ PDRIVER_OBJECT DriverObject) {
    PLDR_DATA_TABLE_ENTRY ldrEntry;
    PLIST_ENTRY prev, next;

    // This is a simplified approach. In reality, you'd iterate PsLoadedModuleList
    // to find your driver's entry based on its DllBase or DriverObject.
    // For demonstration, we assume DriverObject->DriverSection points to the LDR_DATA_TABLE_ENTRY
    // This is NOT always true and depends on Windows version and how the driver is loaded.
    ldrEntry = (PLDR_DATA_TABLE_ENTRY)DriverObject->DriverSection;

    if (ldrEntry && ldrEntry->InLoadOrderLinks.Flink != NULL) {
        // Unlink from InLoadOrderLinks
        prev = ldrEntry->InLoadOrderLinks.Blink;
        next = ldrEntry->InLoadOrderLinks.Flink;
        prev->Flink = next;
        next->Blink = prev;

        // Unlink from InMemoryOrderLinks
        prev = ldrEntry->InMemoryOrderLinks.Blink;
        next = ldrEntry->InMemoryOrderLinks.Flink;
        prev->Flink = next;
        next->Blink = prev;

        // Unlink from InInitializationOrderLinks
        prev = ldrEntry->InInitializationOrderLinks.Blink;
        next = ldrEntry->InInitializationOrderLinks.Flink;
        prev->Flink = next;
        next->Blink = prev;

        // Zero out our entry's links to prevent double unlinking or corruption
        ldrEntry->InLoadOrderLinks.Flink = NULL;
        ldrEntry->InLoadOrderLinks.Blink = NULL;
        ldrEntry->InMemoryOrderLinks.Flink = NULL;
        ldrEntry->InMemoryOrderLinks.Blink = NULL;
        ldrEntry->InInitializationOrderLinks.Flink = NULL;
        ldrEntry->InInitializationOrderLinks.Blink = NULL;

        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: Driver hidden from module lists.\n"));
    } else {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: Failed to hide driver (ldrEntry invalid or already unlinked).\n"));
    }
}

// Function to unhide the driver (re-link it)
// This is even more complex as you need to find the correct insertion points.
// For simplicity, we will just log that it's being unhidden.
VOID UnhideDriver(_In_ PDRIVER_OBJECT DriverObject) {
    // In a real scenario, you would need to re-link the driver entry
    // into the PsLoadedModuleList. This requires finding the correct
    // insertion points, which is non-trivial and highly version-dependent.
    // For now, we just acknowledge the intent.
    UNREFERENCED_PARAMETER(DriverObject);
    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: Driver unhide requested (conceptual).\n"));
}

extern "C" NTSTATUS DriverEntry(_In_ PDRIVER_OBJECT DriverObject, _In_ PUNICODE_STRING RegistryPath) {
    NTSTATUS status;
    WDF_DRIVER_CONFIG config;
    WDF_OBJECT_ATTRIBUTES attributes;

    // Initialize random seed once for the driver
    InitializeRandom();

    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: DriverEntry called\n"));

    // Initialize kernel callbacks for anti-detection
    status = InitializeKernelCallbacks();
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL, "UmbrellaKernelDriver: Kernel callbacks initialization failed: 0x%X\n", status));
        // Continue anyway - callbacks are optional for basic functionality
    }

    // Install anti-detection hooks
    status = InstallAntiDetectionHooks();
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL, "UmbrellaKernelDriver: Anti-detection hooks installation failed: 0x%X\n", status));
        // Continue anyway - hooks are optional for basic functionality
    }

    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.EvtCleanupCallback = UmbrellaEvtDriverContextCleanup;

    WDF_DRIVER_CONFIG_INIT(&config, UmbrellaEvtDeviceAdd);
    config.DriverPoolTag = DRIVER_TAG;

    status = WdfDriverCreate(DriverObject, RegistryPath, &attributes, &config, WDF_NO_HANDLE);
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: WdfDriverCreate failed: 0x%X\n", status));
        
        // Cleanup callbacks if driver creation failed
        RemoveKernelCallbacks();
        RemoveAntiDetectionHooks();
        
        return status;
    }

    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: Driver loaded successfully with kernel callbacks\n"));
    return STATUS_SUCCESS;
}

NTSTATUS UmbrellaEvtDeviceAdd(_In_ WDFDRIVER Driver, _Inout_ PWDFDEVICE_INIT DeviceInit) {
    NTSTATUS status;
    WDFDEVICE device;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDF_OBJECT_ATTRIBUTES deviceAttributes;
    PDEVICE_CONTEXT deviceContext;
    UNICODE_STRING localDeviceName;
    UNICODE_STRING localSymlinkName;

    UNREFERENCED_PARAMETER(Driver);

    // Use fixed device name for compatibility with all Windows versions and C# app
    RtlInitUnicodeString(&localDeviceName, L"\\Device\\UmbrellaKernelDriver");
    RtlInitUnicodeString(&localSymlinkName, L"\\DosDevices\\UmbrellaKernelDriver");

    status = WdfDeviceInitAssignName(DeviceInit, &localDeviceName);
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: WdfDeviceInitAssignName failed: 0x%X\n", status));
        return status;
    }

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&deviceAttributes, DEVICE_CONTEXT);
    deviceAttributes.EvtCleanupCallback = UmbrellaEvtDeviceContextCleanup;

    status = WdfDeviceCreate(&DeviceInit, &deviceAttributes, &device);
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: WdfDeviceCreate failed: 0x%X\n", status));
        return status;
    }

    deviceContext = GetDeviceContext(device);
    RtlZeroMemory(deviceContext, sizeof(DEVICE_CONTEXT));

    // Store the device name in the device context
    deviceContext->DeviceName = localDeviceName;
    deviceContext->SymbolicLinkName = localSymlinkName;

    // Create symbolic link for user-mode access
    status = WdfDeviceCreateSymbolicLink(device, &localSymlinkName);
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: WdfDeviceCreateSymbolicLink failed: 0x%X\n", status));
        return status;
    }

    // Initialize the NtDeviceIoControlFile hook
    status = InitializeNtDeviceIoControlFileHook();
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: Failed to initialize NtDeviceIoControlFile hook: 0x%X\n", status));
        return status;
    }

    // Hook NtDeviceIoControlFile
    status = HookNtDeviceIoControlFile();
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: Failed to hook NtDeviceIoControlFile: 0x%X\n", status));
        return status;
    }

    // Initialize spoofed values registry
    InitializeSpoofedRegistryValues();

    // Initialize disk hook so IOCTL intercepts work
    InitializeDiskHook();

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchParallel);
    queueConfig.EvtIoDeviceControl = UmbrellaEvtIoDeviceControl;
    queueConfig.EvtIoStop = UmbrellaEvtIoStop;

    status = WdfIoQueueCreate(device, &queueConfig, WDF_NO_OBJECT_ATTRIBUTES, WDF_NO_HANDLE);
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: WdfIoQueueCreate failed: 0x%X\n", status));
        return status;
    }

    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: Device created successfully\n"));
    return STATUS_SUCCESS;
}

VOID UmbrellaEvtIoDeviceControl(_In_ WDFQUEUE Queue, _In_ WDFREQUEST Request, _In_ size_t OutputBufferLength, _In_ size_t InputBufferLength, _In_ ULONG IoControlCode) {
    NTSTATUS status = STATUS_SUCCESS;
    WDFDEVICE device = WdfIoQueueGetDevice(Queue);
    PDEVICE_CONTEXT deviceContext = GetDeviceContext(device);
    PVOID inputBuffer = NULL;
    PVOID outputBuffer = NULL;
    size_t bufferSize = 0;

    UNREFERENCED_PARAMETER(OutputBufferLength);
    UNREFERENCED_PARAMETER(InputBufferLength);

    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: IOCTL received: 0x%X\n", IoControlCode));

    switch (IoControlCode) {
        case IOCTL_UMBC_SPOOF_HARDWARE:
            status = HandleSpoofHardware(Request, deviceContext);
            if (NT_SUCCESS(status)) bufferSize = sizeof(HARDWARE_SPOOF_DATA);
            break;
        case IOCTL_UMBC_RESTORE_HARDWARE:
            status = HandleRestoreHardware(Request, deviceContext);
            break;
        case IOCTL_UMBC_QUERY_SYSTEM_INFO:
            status = HandleQuerySystemInfo(Request, deviceContext);
            if (NT_SUCCESS(status)) bufferSize = sizeof(SYSTEM_INFO_DATA);
            break;
        case IOCTL_UMBC_MEMORY_MANIPULATION:
            status = HandleMemoryManipulation(Request, deviceContext);
            break;
        case IOCTL_UMBC_SPOOF_ALL:
            status = HandleSpoofAll(Request, deviceContext);
            if (NT_SUCCESS(status)) bufferSize = sizeof(SPOOF_ALL_RESULT);
            break;
        case IOCTL_UMBC_RESTORE_ALL:
            status = HandleRestoreAll(Request, deviceContext);
            if (NT_SUCCESS(status)) bufferSize = sizeof(SPOOF_ALL_RESULT);
            break;
        case IOCTL_UMBC_QUERY_GENERATED_VALUES:
            status = HandleQueryGeneratedValues(Request, deviceContext);
            if (NT_SUCCESS(status)) bufferSize = sizeof(GENERATED_VALUES_ALL);
            break;
        case IOCTL_UMBC_READ_WMI_RESPONSE:
            status = HandleReadWmiResponse(Request, deviceContext);
            if (NT_SUCCESS(status)) bufferSize = sizeof(WMI_QUERY_DATA);
            break;
        default:
            status = STATUS_INVALID_DEVICE_REQUEST;
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL, "UmbrellaKernelDriver: Unknown IOCTL: 0x%X\n", IoControlCode));
            break;
    }

    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "UmbrellaKernelDriver: IOCTL 0x%X failed: 0x%X\n", IoControlCode, status));
    }

    WdfRequestCompleteWithInformation(Request, status, bufferSize);
}

VOID UmbrellaEvtIoStop(_In_ WDFQUEUE Queue, _In_ WDFREQUEST Request, _In_ ULONG ActionFlags) {
    UNREFERENCED_PARAMETER(Queue);
    UNREFERENCED_PARAMETER(Request);
    UNREFERENCED_PARAMETER(ActionFlags);
    
    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: IO stop requested\n"));
}

VOID UmbrellaEvtDriverContextCleanup(_In_ WDFOBJECT DriverObject) {
    UNREFERENCED_PARAMETER(DriverObject);
    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: Driver context cleanup\n"));

    // Remove kernel callbacks and anti-detection hooks
    RemoveKernelCallbacks();
    RemoveAntiDetectionHooks();

    // Remove all hooks to prevent BSOD on unload
    RemoveDiskHook();
    UnhookNtDeviceIoControlFile();
    UnhookWmiDrivers();
}

VOID UmbrellaEvtDeviceContextCleanup(_In_ WDFOBJECT DeviceObject) {
    PDEVICE_CONTEXT deviceContext = GetDeviceContext((WDFDEVICE)DeviceObject);
    UNREFERENCED_PARAMETER(deviceContext);
    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL, "UmbrellaKernelDriver: Device context cleanup\n"));
}