#pragma once

#include <ntddk.h>
#include <wdf.h>

#define DRIVER_TAG 'umbr'

// Match C# definitions:
// IOCTL_UMBC_SPOOF_HARDWARE = 0x80002004
// DeviceType = 0x8000 (32768)
// Function = 0x801 (2049)
// Method = 0 (BUFFERED)
// Access = 0 (ANY)

#define FILE_DEVICE_UMBRELLA 0x8000
#define UMBC_IOCTL_BASE 0x800

#define IOCTL_UMBC_SPOOF_HARDWARE CTL_CODE(FILE_DEVICE_UMBRELLA, UMBC_IOCTL_BASE + 0x01, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_UMBC_RESTORE_HARDWARE CTL_CODE(FILE_DEVICE_UMBRELLA, UMBC_IOCTL_BASE + 0x02, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_UMBC_QUERY_SYSTEM_INFO CTL_CODE(FILE_DEVICE_UMBRELLA, UMBC_IOCTL_BASE + 0x03, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_UMBC_MEMORY_MANIPULATION CTL_CODE(FILE_DEVICE_UMBRELLA, UMBC_IOCTL_BASE + 0x04, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_UMBC_SPOOF_ALL CTL_CODE(FILE_DEVICE_UMBRELLA, UMBC_IOCTL_BASE + 0x05, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_UMBC_RESTORE_ALL CTL_CODE(FILE_DEVICE_UMBRELLA, UMBC_IOCTL_BASE + 0x06, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_UMBC_QUERY_GENERATED_VALUES CTL_CODE(FILE_DEVICE_UMBRELLA, UMBC_IOCTL_BASE + 0x07, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_UMBC_READ_WMI_RESPONSE CTL_CODE(FILE_DEVICE_UMBRELLA, UMBC_IOCTL_BASE + 0x08, METHOD_BUFFERED, FILE_ANY_ACCESS)

typedef struct _DEVICE_CONTEXT {
    WDFDEVICE Device;
    UNICODE_STRING DeviceName;
    UNICODE_STRING SymbolicLinkName;
    LIST_ENTRY SpoofedItemsList;
    KSPIN_LOCK ListLock;
    BOOLEAN IsInitialized;
} DEVICE_CONTEXT, *PDEVICE_CONTEXT;

typedef struct _SPOOFED_ITEM {
    LIST_ENTRY ListEntry;
    ULONG SpoofType;
    WCHAR OriginalValue[256];
    WCHAR NewValue[256];
    LARGE_INTEGER Timestamp;
} SPOOFED_ITEM, *PSPOOFED_ITEM;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, GetDeviceContext)

// Undocumented structure for loaded module entry
typedef struct _LDR_DATA_TABLE_ENTRY {
    LIST_ENTRY InLoadOrderLinks;
    LIST_ENTRY InMemoryOrderLinks;
    LIST_ENTRY InInitializationOrderLinks;
    PVOID DllBase;
    PVOID EntryPoint;
    ULONG SizeOfImage;
    UNICODE_STRING FullDllName;
    UNICODE_STRING BaseDllName;
    ULONG Flags;
    USHORT LoadCount;
    USHORT TlsIndex;
    LIST_ENTRY HashLinks;
    ULONG TimeDateStamp;
    // ... more fields depending on Windows version
} LDR_DATA_TABLE_ENTRY, *PLDR_DATA_TABLE_ENTRY;

// Function declarations for driver hiding
VOID HideDriver(_In_ PDRIVER_OBJECT DriverObject);
VOID UnhideDriver(_In_ PDRIVER_OBJECT DriverObject);

typedef struct _HARDWARE_SPOOF_DATA {
    ULONG SpoofType;
    WCHAR OriginalValue[256];
    WCHAR NewValue[256];
    ULONG DataSize;
} HARDWARE_SPOOF_DATA, *PHARDWARE_SPOOF_DATA;

typedef struct _SYSTEM_INFO_DATA {
    ULONG InfoType;
    WCHAR InfoValue[512];
} SYSTEM_INFO_DATA, *PSYSTEM_INFO_DATA;

typedef struct _MEMORY_MANIPULATION_DATA {
    ULONG OperationType;
    PVOID TargetAddress;
    ULONG DataSize;
    UCHAR DataBuffer[1024];
} MEMORY_MANIPULATION_DATA, *PMEMORY_MANIPULATION_DATA;

#define SPOOF_TYPE_MACHINE_GUID 0x01
#define SPOOF_TYPE_BIOS_SERIAL 0x02
#define SPOOF_TYPE_BASEBOARD_SERIAL 0x03
#define SPOOF_TYPE_DISK_SERIAL 0x04
#define SPOOF_TYPE_GPU_ID 0x05
#define SPOOF_TYPE_MAC_ADDRESS 0x06
#define SPOOF_TYPE_SMBIOS_DATA 0x07
#define SPOOF_TYPE_ACPI_TABLES 0x08
#define SPOOF_TYPE_MONITOR_SERIAL 0x09

#define INFO_TYPE_MACHINE_GUID 0x01
#define INFO_TYPE_BIOS_SERIAL 0x02
#define INFO_TYPE_BASEBOARD_SERIAL 0x03
#define INFO_TYPE_DISK_SERIAL 0x04
#define INFO_TYPE_GPU_ID 0x05
#define INFO_TYPE_MAC_ADDRESS 0x06

// Function pointer for the original NtDeviceIoControlFile
typedef NTSTATUS (*PNtDeviceIoControlFile)(
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
); 

extern PNtDeviceIoControlFile OriginalNtDeviceIoControlFile;
extern PVOID HookedNtDeviceIoControlFile; // To store the trampoline for unhooking

NTSTATUS InitializeNtDeviceIoControlFileHook();
NTSTATUS HookNtDeviceIoControlFile();
VOID UnhookNtDeviceIoControlFile();


#define MEMORY_OP_READ 0x01
#define MEMORY_OP_WRITE 0x02
#define MEMORY_OP_PROTECT 0x03

#define MAX_SPOOFED_COMPONENTS 16

typedef struct _SPOOF_RESULT_ENTRY {
    ULONG SpoofType;
    NTSTATUS Status;
} SPOOF_RESULT_ENTRY, *PSPOOF_RESULT_ENTRY;

typedef struct _SPOOF_ALL_RESULT {
    ULONG ComponentCount;
    SPOOF_RESULT_ENTRY Components[MAX_SPOOFED_COMPONENTS];
} SPOOF_ALL_RESULT, *PSPOOF_ALL_RESULT;

typedef struct _GENERATED_VALUE_ENTRY {
    WCHAR ComponentName[64];
    WCHAR OriginalValue[256];
    WCHAR NewValue[256];
} GENERATED_VALUE_ENTRY, *PGENERATED_VALUE_ENTRY;

typedef struct _GENERATED_VALUES_ALL {
    ULONG EntryCount;
    GENERATED_VALUE_ENTRY Entries[MAX_SPOOFED_COMPONENTS];
} GENERATED_VALUES_ALL, *PGENERATED_VALUES_ALL;

typedef struct _WMI_QUERY_DATA {
    ULONG QueryType;
    WCHAR OutputBuffer[512];
    ULONG BufferSize;
} WMI_QUERY_DATA, *PWMI_QUERY_DATA;

#define WMI_QUERY_TYPE_BIOS 0x01
#define WMI_QUERY_TYPE_BASEBOARD 0x02
#define WMI_QUERY_TYPE_DISK 0x03
#define WMI_QUERY_TYPE_CPU 0x04
#define WMI_QUERY_TYPE_GPU 0x05
#define WMI_QUERY_TYPE_RAM 0x06
#define WMI_QUERY_TYPE_MONITOR 0x07

// Registry key where driver stores original and spoofed values
#define DRIVER_REG_KEY L"\\Registry\\Machine\\SYSTEM\\CurrentControlSet\\Services\\UmbrellaKernelDriver\\SpoofedValues"
