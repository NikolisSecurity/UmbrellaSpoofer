#pragma once

#include "Driver.h"
#include <ntddstor.h>
#include <mountdev.h>
#include <ntddvol.h>

// Structure for hooking context
typedef struct _HOOK_CONTEXT {
    PDRIVER_OBJECT DriverObject;
    PDRIVER_DISPATCH OriginalMajorFunction;
    UNICODE_STRING DriverName;
} HOOK_CONTEXT, *PHOOK_CONTEXT;

// Function prototypes
NTSTATUS SpoofMachineGuid(_In_ PCWSTR NewGuid);
NTSTATUS SpoofBiosSerial(_In_ PCWSTR NewSerial);
NTSTATUS SpoofBaseBoardSerial(_In_ PCWSTR NewSerial);
NTSTATUS SpoofDiskSerial(_In_ PCWSTR NewSerial);
NTSTATUS SpoofGpuIdentifier(_In_ PCWSTR NewIdentifier);
NTSTATUS SpoofMacAddress(_In_ PCWSTR NewMac);
NTSTATUS SpoofSmbiosData(_In_ PVOID NewData, _In_ ULONG DataSize);
NTSTATUS SpoofAcpiTables(_In_ PVOID NewData, _In_ ULONG DataSize);

NTSTATUS RestoreMachineGuid(_In_ PCWSTR OriginalGuid);
NTSTATUS RestoreBiosSerial(_In_ PCWSTR OriginalSerial);
NTSTATUS RestoreBaseBoardSerial(_In_ PCWSTR OriginalSerial);
NTSTATUS RestoreDiskSerial(_In_ PCWSTR OriginalSerial);
NTSTATUS RestoreGpuIdentifier(_In_ PCWSTR OriginalIdentifier);
NTSTATUS RestoreMacAddress(_In_ PCWSTR OriginalMac);

NTSTATUS QueryMachineGuid(_Out_ PWSTR Buffer, _In_ ULONG BufferSize);
NTSTATUS QueryBiosSerial(_Out_ PWSTR Buffer, _In_ ULONG BufferSize);
NTSTATUS QueryBaseBoardSerial(_Out_ PWSTR Buffer, _In_ ULONG BufferSize);
NTSTATUS QueryDiskSerial(_Out_ PWSTR Buffer, _In_ ULONG BufferSize);
NTSTATUS QueryGpuIdentifier(_Out_ PWSTR Buffer, _In_ ULONG BufferSize);
NTSTATUS QueryMacAddress(_Out_ PWSTR Buffer, _In_ ULONG BufferSize);

NTSTATUS PatchSmbiosStructure(_In_ UCHAR Type, _In_ PVOID NewData, _In_ ULONG DataSize);
NTSTATUS PatchAcpiTable(_In_ PCHAR Signature, _In_ PVOID NewData, _In_ ULONG DataSize);
NTSTATUS ModifyRegistryValue(_In_ PCWSTR KeyPath, _In_ PCWSTR ValueName, _In_ PCWSTR NewValue);
NTSTATUS QueryRegistryValue(_In_ PCWSTR KeyPath, _In_ PCWSTR ValueName, _Out_ PWSTR Buffer, _In_ ULONG BufferSize);

// Kernel Callback Hooks
NTSTATUS InitializeKernelCallbacks();
NTSTATUS RemoveKernelCallbacks();

// Anti-Detection Functions
NTSTATUS InstallAntiDetectionHooks();
NTSTATUS RemoveAntiDetectionHooks();

// Memory Protection Functions
NTSTATUS ProtectDriverMemory();
NTSTATUS UnprotectDriverMemory();

// Hooking functions
NTSTATUS InitializeDiskHook();
NTSTATUS RemoveDiskHook();
NTSTATUS HookDriverDispatch(_In_ PUNICODE_STRING DriverName, _In_ PDRIVER_DISPATCH NewDispatch, _Out_ PHOOK_CONTEXT HookContext);
NTSTATUS UnhookDriverDispatch(_In_ PHOOK_CONTEXT HookContext);

// Global variables for kernel callbacks
extern PVOID g_ProcessNotifyRoutine;
extern PVOID g_ThreadNotifyRoutine;
extern PVOID g_LoadImageNotifyRoutine;
extern PVOID g_RegistryNotifyRoutine;

// Global variable for spoofed disk serial
extern WCHAR g_SpoofedDiskSerial[256];
extern BOOLEAN g_DiskSpoofingActive;