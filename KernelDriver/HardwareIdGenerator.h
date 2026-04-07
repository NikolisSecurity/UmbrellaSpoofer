#pragma once

#include <ntddk.h>
#include <wdf.h>
#include <ntstrsafe.h>

#define MAX_HARDWARE_IDS 10
#define MAC_ADDRESS_LENGTH 18
#define SERIAL_NUMBER_LENGTH 32
#define TPM_ID_LENGTH 24

// Random number generation
VOID InitializeRandom();
int GetRandomInt();

typedef struct _HARDWARE_ID_FORMAT {
    WCHAR OriginalValue[256];
    WCHAR GeneratedValue[256];
    BOOLEAN IsMultiValue;
    ULONG ValueCount;
} HARDWARE_ID_FORMAT, *PHARDWARE_ID_FORMAT;

// Function prototypes
NTSTATUS GenerateFormattedHardwareId(
    _In_ ULONG SpoofType,
    _In_ PCWSTR OriginalValue,
    _Out_ PWCHAR GeneratedValue,
    _In_ SIZE_T BufferSize
);

NTSTATUS GenerateMultipleFormattedIds(
    _In_ PCWSTR OriginalValue,
    _Out_ PWCHAR GeneratedValues,
    _In_ SIZE_T BufferSize,
    _Out_ PULONG ValueCount
);

NTSTATUS GenerateMacAddress(
    _In_ PCWSTR OriginalMac,
    _Out_ PWCHAR GeneratedMac,
    _In_ SIZE_T BufferSize
);

NTSTATUS GenerateBiosSerial(
    _In_ PCWSTR OriginalSerial,
    _Out_ PWCHAR GeneratedSerial,
    _In_ SIZE_T BufferSize
);

NTSTATUS GenerateTpmId(
    _In_ PCWSTR OriginalTpm,
    _Out_ PWCHAR GeneratedTpm,
    _In_ SIZE_T BufferSize
);

NTSTATUS GenerateDiskSerial(
    _In_ PCWSTR OriginalSerial,
    _Out_ PWCHAR GeneratedSerial,
    _In_ SIZE_T BufferSize
);

NTSTATUS GenerateBaseboardSerial(
    _In_ PCWSTR OriginalSerial,
    _Out_ PWCHAR GeneratedSerial,
    _In_ SIZE_T BufferSize
);

NTSTATUS GenerateGpuId(
    _In_ PCWSTR OriginalGpuId,
    _Out_ PWCHAR GeneratedGpuId,
    _In_ SIZE_T BufferSize
);

BOOLEAN IsMacAddressFormat(_In_ PCWSTR Value);
BOOLEAN IsBiosSerialFormat(_In_ PCWSTR Value);
BOOLEAN IsTpmIdFormat(_In_ PCWSTR Value);
BOOLEAN IsDiskSerialFormat(_In_ PCWSTR Value);
BOOLEAN IsBaseboardSerialFormat(_In_ PCWSTR Value);
BOOLEAN IsGpuIdFormat(_In_ PCWSTR Value);
BOOLEAN IsDevicePathFormat(_In_ PCWSTR Value);
BOOLEAN IsVolumeSerialFormat(_In_ PCWSTR Value);
BOOLEAN IsDescriptiveStringFormat(_In_ PCWSTR Value);
BOOLEAN IsNumericOnlyFormat(_In_ PCWSTR Value);

// Utility functions
VOID GenerateRandomHex(_Out_ PWCHAR Buffer, _In_ SIZE_T Length, _In_ BOOLEAN Uppercase);
VOID GenerateRandomDecimal(_Out_ PWCHAR Buffer, _In_ SIZE_T Length);
ULONG CountMacAddresses(_In_ PCWSTR MacAddresses);
ULONG CountSerialNumbers(_In_ PCWSTR Serials);