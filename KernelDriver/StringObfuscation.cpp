// KernelDriver/StringObfuscation.cpp
#include "StringObfuscation.h"

// Simple XOR-based decryption
VOID DecryptString(_Inout_ PCHAR EncryptedString, _In_ ULONG Length, _In_ UCHAR Key) {
    for (ULONG i = 0; i < Length; ++i) {
        EncryptedString[i] ^= Key;
    }
}
