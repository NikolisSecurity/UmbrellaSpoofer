// KernelDriver/TpmSpoofing.cpp
#include "TpmSpoofing.h"
#include "HardwareIdGenerator.h" // For InitializeRandom and GenerateRandomBytes

// Helper to generate random bytes
VOID GenerateRandomBytes(PUCHAR Buffer, ULONG Length) {
    for (ULONG i = 0; i < Length; i++) {
        Buffer[i] = (UCHAR)(GetRandomInt() % 256);
    }
}

VOID GenerateSpoofedTpm2bDigest(_Out_ PTPM2B_DIGEST Digest) {
    InitializeRandom();
    Digest->size = 32; // SHA256 digest size
    GenerateRandomBytes(Digest->buffer, Digest->size);
}

VOID GenerateSpoofedTpm2bPublic(_Out_ PTPM2B_PUBLIC Public) {
    InitializeRandom();

    Public->size = sizeof(TPMT_PUBLIC); // Size of the public area

    // Set common values for a spoofed RSA public key
    Public->publicArea.type = TPM_ALG_RSA;
    Public->publicArea.nameAlg = TPM_ALG_SHA256;
    Public->publicArea.objectAttributes = 0x00060000; // Example attributes: fixedTPM, fixedParent, sensitiveDataOrigin
    
    // RSA parameters
    Public->publicArea.parameters.symmetric = 0x0001; // TPM_ALG_NULL
    Public->publicArea.parameters.scheme = 0x0010;    // TPM_ALG_RSAPSS (example)
    Public->publicArea.parameters.keyBits = 2048;
    Public->publicArea.parameters.exponent = 0x00000000; // Default exponent (65537)

    // Generate a random RSA modulus
    Public->publicArea.unique.size = 256; // 2048-bit RSA modulus
    GenerateRandomBytes(Public->publicArea.unique.buffer, Public->publicArea.unique.size);
}

VOID GenerateSpoofedPcrValues(_Out_writes_bytes_(NumPcr * sizeof(TPM2B_DIGEST)) PTPM2B_DIGEST PcrArray, _In_ ULONG NumPcr) {
    InitializeRandom();
    for (ULONG i = 0; i < NumPcr; ++i) {
        GenerateSpoofedTpm2bDigest(&PcrArray[i]);
    }
}
