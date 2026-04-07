#include "HardwareIdGenerator.h"
#include "HardwareSpoofing.h"

// Forward declarations for utility functions
BOOLEAN IsHexChar(WCHAR c);

// Initialize random seed once
BOOLEAN RandomInitialized = FALSE;
ULONG g_Seed = 0;

VOID InitializeRandom()
{
    if (!RandomInitialized)
    {
        LARGE_INTEGER seed;
        KeQuerySystemTime(&seed);
        g_Seed = seed.LowPart;
        RandomInitialized = TRUE;
    }
}

// Simple LCG PRNG since RtlRandomEx requires ntifs.h which conflicts with wdm.h
int GetRandomInt() {
    g_Seed = (1103515245 * g_Seed + 12345);
    return (int)(g_Seed & 0x7FFFFFFF);
}

NTSTATUS GenerateSingleFormattedHardwareId(
    _In_ ULONG SpoofType,
    _In_ PCWSTR OriginalValue,
    _Out_ PWCHAR GeneratedValue,
    _In_ SIZE_T BufferSize
);

NTSTATUS GenerateFormattedHardwareId(
    _In_ ULONG SpoofType,
    _In_ PCWSTR OriginalValue,
    _Out_ PWCHAR GeneratedValue,
    _In_ SIZE_T BufferSize
)
{
    InitializeRandom();

    if (OriginalValue == NULL || GeneratedValue == NULL || BufferSize < 2)
    {
        return STATUS_INVALID_PARAMETER;
    }

    RtlZeroMemory(GeneratedValue, BufferSize);

    // If it contains a comma, process each part separately
    if (wcschr(OriginalValue, L',') != NULL)
    {
        WCHAR tempBuffer[512];
        RtlStringCchCopyW(tempBuffer, sizeof(tempBuffer) / sizeof(WCHAR), OriginalValue);
        
        PWCHAR current = tempBuffer;
        BOOLEAN first = TRUE;
        
        while (current && *current != L'\0')
        {
            // Find the next comma
            PWCHAR comma = wcschr(current, L',');
            if (comma != NULL) {
                *comma = L'\0'; // Null-terminate the current part
            }
            
            // Trim leading spaces
            while (*current == L' ') current++;
            
            if (*current != L'\0') {
                WCHAR spoofedPart[256];
                RtlZeroMemory(spoofedPart, sizeof(spoofedPart));
                
                // Recursively process the single part
                NTSTATUS partStatus = GenerateSingleFormattedHardwareId(SpoofType, current, spoofedPart, sizeof(spoofedPart));
                if (!NT_SUCCESS(partStatus)) {
                    GenerateRandomHex(spoofedPart, 16, TRUE);
                }
                
                if (!first) {
                    RtlStringCchCatW(GeneratedValue, BufferSize / sizeof(WCHAR), L", ");
                }
                RtlStringCchCatW(GeneratedValue, BufferSize / sizeof(WCHAR), spoofedPart);
                first = FALSE;
            }
            
            // Move to the next part
            if (comma != NULL) {
                current = comma + 1;
            } else {
                break;
            }
        }
        return STATUS_SUCCESS;
    }

    return GenerateSingleFormattedHardwareId(SpoofType, OriginalValue, GeneratedValue, BufferSize);
}

NTSTATUS GenerateSingleFormattedHardwareId(
    _In_ ULONG SpoofType,
    _In_ PCWSTR OriginalValue,
    _Out_ PWCHAR GeneratedValue,
    _In_ SIZE_T BufferSize
)
{
    // First check specific spoof types to handle descriptive strings properly
    if (SpoofType == SPOOF_TYPE_GPU_ID) {
        return GenerateGpuId(OriginalValue, GeneratedValue, BufferSize);
    }
    else if (SpoofType == SPOOF_TYPE_SMBIOS_DATA) {
        // Generate random alphanumeric serial for RAM/SMBIOS
        WCHAR randomId[32];
        GenerateRandomHex(randomId, 16, TRUE);
        RtlStringCchPrintfW(GeneratedValue, BufferSize, L"%s", randomId);
        return STATUS_SUCCESS;
    }
    else if (SpoofType == SPOOF_TYPE_DISK_SERIAL) {
        return GenerateDiskSerial(OriginalValue, GeneratedValue, BufferSize);
    }
    else if (SpoofType == SPOOF_TYPE_MAC_ADDRESS) {
        return GenerateMacAddress(OriginalValue, GeneratedValue, BufferSize);
    }
    else if (SpoofType == SPOOF_TYPE_BIOS_SERIAL) {
        return GenerateBiosSerial(OriginalValue, GeneratedValue, BufferSize);
    }
    else if (SpoofType == SPOOF_TYPE_BASEBOARD_SERIAL) {
        return GenerateBaseboardSerial(OriginalValue, GeneratedValue, BufferSize);
    }
    else if (SpoofType == SPOOF_TYPE_MONITOR_SERIAL) {
        // Generate random alphanumeric serial for monitor
        WCHAR serial[24];
        GenerateRandomHex(serial, 12, TRUE);
        RtlStringCchCopyW(GeneratedValue, BufferSize, serial);
        return STATUS_SUCCESS;
    }
    else if (SpoofType == SPOOF_TYPE_MACHINE_GUID) {
        // Format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
        WCHAR guid[64];
        GenerateRandomHex(guid, 8, TRUE); guid[8] = L'-';
        GenerateRandomHex(guid + 9, 4, TRUE); guid[13] = L'-';
        GenerateRandomHex(guid + 14, 4, TRUE); guid[18] = L'-';
        GenerateRandomHex(guid + 19, 4, TRUE); guid[23] = L'-';
        GenerateRandomHex(guid + 24, 12, TRUE);
        RtlStringCchCopyW(GeneratedValue, BufferSize, guid);
        return STATUS_SUCCESS;
    }

    // First, check for and protect non-spoofable formats
    if (IsDevicePathFormat(OriginalValue) || IsDescriptiveStringFormat(OriginalValue) || IsNumericOnlyFormat(OriginalValue) || wcsstr(OriginalValue, L"[Not Available]") != NULL)
    {
        RtlStringCchCopyW(GeneratedValue, BufferSize, OriginalValue);
        return STATUS_SUCCESS;
    }

    // Check for specific, spoofable formats
    if (IsVolumeSerialFormat(OriginalValue))
    {
        return GenerateDiskSerial(OriginalValue, GeneratedValue, BufferSize);
    }
    else if (IsMacAddressFormat(OriginalValue))
    {
        return GenerateMacAddress(OriginalValue, GeneratedValue, BufferSize);
    }
    else if (IsBiosSerialFormat(OriginalValue))
    {
        return GenerateBiosSerial(OriginalValue, GeneratedValue, BufferSize);
    }
    else if (IsTpmIdFormat(OriginalValue))
    {
        return GenerateTpmId(OriginalValue, GeneratedValue, BufferSize);
    }
    else if (IsDiskSerialFormat(OriginalValue))
    {
        return GenerateDiskSerial(OriginalValue, GeneratedValue, BufferSize);
    }
    else if (IsBaseboardSerialFormat(OriginalValue))
    {
        return GenerateBaseboardSerial(OriginalValue, GeneratedValue, BufferSize);
    }
    else if (IsGpuIdFormat(OriginalValue))
    {
        return GenerateGpuId(OriginalValue, GeneratedValue, BufferSize);
    }

    // Default: generate random alphanumeric string
    WCHAR randomId[32];
    GenerateRandomHex(randomId, 16, TRUE);
    RtlStringCchPrintfW(GeneratedValue, BufferSize, L"%s", randomId);

    return STATUS_SUCCESS;
}

NTSTATUS GenerateMultipleFormattedIds(
    _In_ PCWSTR OriginalValue,
    _Out_ PWCHAR GeneratedValues,
    _In_ SIZE_T BufferSize,
    _Out_ PULONG ValueCount
)
{
    InitializeRandom();

    if (OriginalValue == NULL || GeneratedValues == NULL || ValueCount == NULL || BufferSize < 2)
    {
        return STATUS_INVALID_PARAMETER;
    }

    RtlZeroMemory(GeneratedValues, BufferSize);
    *ValueCount = 0;

    // Count how many values we need to generate
    ULONG count = 1;
    if (IsMacAddressFormat(OriginalValue))
    {
        count = CountMacAddresses(OriginalValue);
    }
    else
    {
        count = CountSerialNumbers(OriginalValue);
    }

    if (count == 0) count = 1;

    WCHAR generatedBuffer[256] = {0};
    WCHAR tempBuffer[64] = {0};

    for (ULONG i = 0; i < count; i++)
    {
        if (IsMacAddressFormat(OriginalValue))
        {
            GenerateMacAddress(OriginalValue, tempBuffer, sizeof(tempBuffer) / sizeof(WCHAR));
        }
        else if (IsBiosSerialFormat(OriginalValue))
        {
            GenerateBiosSerial(OriginalValue, tempBuffer, sizeof(tempBuffer) / sizeof(WCHAR));
        }
        else if (IsTpmIdFormat(OriginalValue))
        {
            GenerateTpmId(OriginalValue, tempBuffer, sizeof(tempBuffer) / sizeof(WCHAR));
        }
        else if (IsDiskSerialFormat(OriginalValue))
        {
            GenerateDiskSerial(OriginalValue, tempBuffer, sizeof(tempBuffer) / sizeof(WCHAR));
        }
        else if (IsBaseboardSerialFormat(OriginalValue))
        {
            GenerateBaseboardSerial(OriginalValue, tempBuffer, sizeof(tempBuffer) / sizeof(WCHAR));
        }
        else if (IsGpuIdFormat(OriginalValue))
        {
            GenerateGpuId(OriginalValue, tempBuffer, sizeof(tempBuffer) / sizeof(WCHAR));
        }
        else
        {
            GenerateRandomHex(tempBuffer, 16, TRUE);
        }

        // Append to result with comma separation
        if (i > 0)
        {
            RtlStringCchCatW(generatedBuffer, sizeof(generatedBuffer) / sizeof(WCHAR), L",");
        }
        RtlStringCchCatW(generatedBuffer, sizeof(generatedBuffer) / sizeof(WCHAR), tempBuffer);
    }

    RtlStringCchCopyW(GeneratedValues, BufferSize, generatedBuffer);
    *ValueCount = count;

    return STATUS_SUCCESS;
}

NTSTATUS GenerateMacAddress(
    _In_ PCWSTR OriginalMac,
    _Out_ PWCHAR GeneratedMac,
    _In_ SIZE_T BufferSize
)
{
    InitializeRandom();

    if (BufferSize < MAC_ADDRESS_LENGTH)
    {
        return STATUS_BUFFER_TOO_SMALL;
    }

    // Generate random MAC address with proper format
    WCHAR hexDigits[] = L"0123456789ABCDEF";
    WCHAR mac[MAC_ADDRESS_LENGTH] = {0};

    // Determine separator from original MAC
    WCHAR separator = L':';
    if (wcschr(OriginalMac, L'-') != NULL)
    {
        separator = L'-';
    }

    for (int i = 0; i < 6; i++)
    {
        if (i > 0)
        {
            mac[i * 3 - 1] = separator;
        }

        int digit1 = GetRandomInt() % 16;
        int digit2 = GetRandomInt() % 16;

        // Ensure valid MAC address (not multicast, not broadcast)
        if (i == 0)
        {
            digit1 &= 0xFE; // Clear multicast bit
            digit1 |= 0x02; // Set locally administered bit
        }

        mac[i * 3] = hexDigits[digit1];
        mac[i * 3 + 1] = hexDigits[digit2];
    }

    RtlStringCchCopyW(GeneratedMac, BufferSize, mac);
    return STATUS_SUCCESS;
}

NTSTATUS GenerateBiosSerial(
    _In_ PCWSTR OriginalSerial,
    _Out_ PWCHAR GeneratedSerial,
    _In_ SIZE_T BufferSize
)
{
    InitializeRandom();

    if (BufferSize < SERIAL_NUMBER_LENGTH)
    {
        return STATUS_BUFFER_TOO_SMALL;
    }

    // BIOS serial numbers are typically alphanumeric, 10-20 characters
    WCHAR serial[SERIAL_NUMBER_LENGTH] = {0};
    WCHAR chars[] = L"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    int length = 12 + (GetRandomInt() % 8); // 12-20 characters
    for (int i = 0; i < length; i++)
    {
        serial[i] = chars[GetRandomInt() % (sizeof(chars) / sizeof(WCHAR) - 1)];
    }

    RtlStringCchCopyW(GeneratedSerial, BufferSize, serial);
    return STATUS_SUCCESS;
}

NTSTATUS GenerateTpmId(
    _In_ PCWSTR OriginalTpm,
    _Out_ PWCHAR GeneratedTpm,
    _In_ SIZE_T BufferSize
)
{
    InitializeRandom();

    if (BufferSize < TPM_ID_LENGTH)
    {
        return STATUS_BUFFER_TOO_SMALL;
    }

    // TPM IDs are typically longer hexadecimal strings
    WCHAR tpmId[TPM_ID_LENGTH] = {0};
    GenerateRandomHex(tpmId, 20, TRUE); // 20 hex characters = 40 hex digits

    RtlStringCchCopyW(GeneratedTpm, BufferSize, tpmId);
    return STATUS_SUCCESS;
}

NTSTATUS GenerateDiskSerial(
    _In_ PCWSTR OriginalSerial,
    _Out_ PWCHAR GeneratedSerial,
    _In_ SIZE_T BufferSize
)
{
    InitializeRandom();

    if (BufferSize < SERIAL_NUMBER_LENGTH)
    {
        return STATUS_BUFFER_TOO_SMALL;
    }

    // Disk serials are often alphanumeric, sometimes with dashes
    WCHAR serial[SERIAL_NUMBER_LENGTH] = {0};
    WCHAR chars[] = L"0123456789ABCDEF";

    int length = 16 + (GetRandomInt() % 8); // 16-24 characters
    for (int i = 0; i < length; i++)
    {
        if (i > 0 && i % 4 == 0 && (GetRandomInt() % 3 == 0))
        {
            serial[i] = L'-';
            i++;
        }
        serial[i] = chars[GetRandomInt() % (sizeof(chars) / sizeof(WCHAR) - 1)];
    }

    RtlStringCchCopyW(GeneratedSerial, BufferSize, serial);
    return STATUS_SUCCESS;
}

NTSTATUS GenerateBaseboardSerial(
    _In_ PCWSTR OriginalSerial,
    _Out_ PWCHAR GeneratedSerial,
    _In_ SIZE_T BufferSize
)
{
    // Similar to BIOS serial but often shorter
    InitializeRandom();

    if (BufferSize < SERIAL_NUMBER_LENGTH)
    {
        return STATUS_BUFFER_TOO_SMALL;
    }

    WCHAR serial[SERIAL_NUMBER_LENGTH] = {0};
    WCHAR chars[] = L"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    int length = 8 + (GetRandomInt() % 6); // 8-14 characters
    for (int i = 0; i < length; i++)
    {
        serial[i] = chars[GetRandomInt() % (sizeof(chars) / sizeof(WCHAR) - 1)];
    }

    RtlStringCchCopyW(GeneratedSerial, BufferSize, serial);
    return STATUS_SUCCESS;
}

NTSTATUS GenerateGpuId(
    _In_ PCWSTR OriginalGpuId,
    _Out_ PWCHAR GeneratedGpuId,
    _In_ SIZE_T BufferSize
)
{
    InitializeRandom();

    if (BufferSize < SERIAL_NUMBER_LENGTH)
    {
        return STATUS_BUFFER_TOO_SMALL;
    }

    // Generate random hex serial for GPU
    WCHAR gpuId[SERIAL_NUMBER_LENGTH] = {0};
    GenerateRandomHex(gpuId, 16, TRUE);
    RtlStringCchCopyW(GeneratedGpuId, BufferSize, gpuId);
    return STATUS_SUCCESS;
}

// Format detection functions
BOOLEAN IsMacAddressFormat(_In_ PCWSTR Value)
{
    if (Value == NULL) return FALSE;
    
    // Check for MAC address patterns: XX:XX:XX:XX:XX:XX or XX-XX-XX-XX-XX-XX
    size_t len = wcslen(Value);
    if (len != 17) return FALSE;
    
    for (size_t i = 0; i < len; i++)
    {
        if (i % 3 == 2)
        {
            if (Value[i] != L':' && Value[i] != L'-') return FALSE;
        }
        else
        {
            if (!IsHexChar(Value[i])) return FALSE;
        }
    }
    return TRUE;
}

BOOLEAN IsBiosSerialFormat(_In_ PCWSTR Value)
{
    if (Value == NULL) return FALSE;
    
    // BIOS serials are typically alphanumeric, 8-20 characters
    size_t len = wcslen(Value);
    if (len < 8 || len > 20) return FALSE;
    
    for (size_t i = 0; i < len; i++)
    {
        if (!iswalnum(Value[i])) return FALSE;
    }
    return TRUE;
}

BOOLEAN IsTpmIdFormat(_In_ PCWSTR Value)
{
    if (Value == NULL) return FALSE;
    
    // TPM IDs are typically longer hexadecimal strings
    size_t len = wcslen(Value);
    if (len < 16 || len > 40) return FALSE;
    
    for (size_t i = 0; i < len; i++)
    {
        if (!IsHexChar(Value[i])) return FALSE;
    }
    return TRUE;
}

BOOLEAN IsDiskSerialFormat(_In_ PCWSTR Value)
{
    if (Value == NULL) return FALSE;
    
    // Disk serials can be alphanumeric with optional dashes
    size_t len = wcslen(Value);
    if (len < 12 || len > 24) return FALSE;
    
    for (size_t i = 0; i < len; i++)
    {
        if (!iswalnum(Value[i]) && Value[i] != L'-') return FALSE;
    }
    return TRUE;
}

BOOLEAN IsBaseboardSerialFormat(_In_ PCWSTR Value)
{
    if (Value == NULL) return FALSE;
    
    // Baseboard serials are similar to BIOS but often shorter
    size_t len = wcslen(Value);
    if (len < 6 || len > 16) return FALSE;
    
    for (size_t i = 0; i < len; i++)
    {
        if (!iswalnum(Value[i])) return FALSE;
    }
    return TRUE;
}

BOOLEAN IsGpuIdFormat(_In_ PCWSTR Value)
{
    if (Value == NULL) return FALSE;
    
    // GPU IDs are often shorter hexadecimal strings
    size_t len = wcslen(Value);
    if (len < 4 || len > 8) return FALSE;
    
    for (size_t i = 0; i < len; i++)
    {
        if (!IsHexChar(Value[i])) return FALSE;
    }
    return TRUE;
}

BOOLEAN IsDevicePathFormat(_In_ PCWSTR Value)
{
    if (Value == NULL) return FALSE;
    return (wcsstr(Value, L"\\Device\\") == Value);
}

BOOLEAN IsVolumeSerialFormat(_In_ PCWSTR Value)
{
    if (Value == NULL) return FALSE;

    // Check for volume serial pattern: C::XXXXXXXX,D::XXXXXXXX
    return (wcschr(Value, L':') != NULL && wcschr(Value, L':') == wcsstr(Value, L"::"));
}

BOOLEAN IsDescriptiveStringFormat(_In_ PCWSTR Value)
{
    if (Value == NULL) return FALSE;

    // Check for descriptive strings that contain spaces and are not purely numeric/hex
    return (wcschr(Value, L' ') != NULL);
}

BOOLEAN IsNumericOnlyFormat(_In_ PCWSTR Value)
{
    if (Value == NULL) return FALSE;

    for (size_t i = 0; i < wcslen(Value); i++)
    {
        if (!iswdigit(Value[i])) return FALSE;
    }
    return TRUE;
}

NTSTATUS GenerateVolumeSerials(_In_ PCWSTR OriginalSerials, _Out_ PWCHAR GeneratedSerials, _In_ SIZE_T BufferSize)
{
    UNREFERENCED_PARAMETER(OriginalSerials);
    UNREFERENCED_PARAMETER(GeneratedSerials);
    UNREFERENCED_PARAMETER(BufferSize);
    return STATUS_NOT_IMPLEMENTED;
}

// Utility functions
BOOLEAN IsHexChar(WCHAR c) {
    return (c >= L'0' && c <= L'9') || (c >= L'a' && c <= L'f') || (c >= L'A' && c <= L'F');
}

VOID GenerateRandomHex(_Out_ PWCHAR Buffer, _In_ SIZE_T Length, _In_ BOOLEAN Uppercase)
{
    WCHAR hexDigitsLower[] = L"0123456789abcdef";
    WCHAR hexDigitsUpper[] = L"0123456789ABCDEF";
    WCHAR* digits = Uppercase ? hexDigitsUpper : hexDigitsLower;

    for (SIZE_T i = 0; i < Length; i++)
    {
        Buffer[i] = digits[GetRandomInt() % 16];
    }
    Buffer[Length] = L'\0';
}

VOID GenerateRandomDecimal(_Out_ PWCHAR Buffer, _In_ SIZE_T Length)
{
    WCHAR decimalDigits[] = L"0123456789";

    for (SIZE_T i = 0; i < Length; i++)
    {
        Buffer[i] = decimalDigits[GetRandomInt() % 10];
    }
    Buffer[Length] = L'\0';
}

ULONG CountMacAddresses(_In_ PCWSTR MacAddresses)
{
    if (MacAddresses == NULL) return 0;
    
    ULONG count = 1;
    PCWSTR ptr = MacAddresses;
    
    while ((ptr = wcschr(ptr, L',')) != NULL)
    {
        count++;
        ptr++;
    }
    
    return count;
}

ULONG CountSerialNumbers(_In_ PCWSTR Serials)
{
    if (Serials == NULL) return 0;
    
    ULONG count = 1;
    PCWSTR ptr = Serials;
    
    while ((ptr = wcschr(ptr, L',')) != NULL)
    {
        count++;
        ptr++;
    }
    
    return count;
}
