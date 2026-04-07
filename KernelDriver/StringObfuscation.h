// KernelDriver/StringObfuscation.h
#pragma once

#include <ntddk.h>

// Function to decrypt a string in place
VOID DecryptString(_Inout_ PCHAR EncryptedString, _In_ ULONG Length, _In_ UCHAR Key);

// Macro to encrypt a string at compile time
// Usage: ENCRYPT_STRING("MySecretString", 0xAA)
#define ENCRYPT_STRING(str, key) \
    ([]() -> PCHAR { \
        static const char encrypted[] = { /* Encrypted bytes will be placed here */ }; \
        static char decrypted[sizeof(encrypted)]; \
        RtlCopyMemory(decrypted, encrypted, sizeof(encrypted)); \
        DecryptString(decrypted, sizeof(encrypted) - 1, key); \
        return decrypted; \
    }())
