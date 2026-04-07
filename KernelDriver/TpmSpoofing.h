// KernelDriver/TpmSpoofing.h
#pragma once

#include <ntddk.h>
#include <wdf.h>

// Simplified TPM 2.0 structures for spoofing purposes
// These are not exhaustive but cover common identifying fields.

// TPM2B_DIGEST: Used for hashes (e.g., PCR values, names)
typedef struct _TPM2B_DIGEST {
    USHORT size;
    UCHAR buffer[32]; // SHA256 digest size
} TPM2B_DIGEST, *PTPM2B_DIGEST;

// TPMI_ALG_PUBLIC: Public algorithm identifier
typedef USHORT TPMI_ALG_PUBLIC;
#define TPM_ALG_RSA 0x0001
#define TPM_ALG_SHA256 0x000B

// TPM2B_PUBLIC_KEY_RSA: RSA public key modulus
typedef struct _TPM2B_PUBLIC_KEY_RSA {
    USHORT size;
    UCHAR buffer[256]; // 2048-bit RSA modulus size
} TPM2B_PUBLIC_KEY_RSA, *PTPM2B_PUBLIC_KEY_RSA;

// TPMS_RSA_PARMS: RSA parameters
typedef struct _TPMS_RSA_PARMS {
    TPMI_ALG_PUBLIC symmetric; // Symmetric algorithm for storage
    TPMI_ALG_PUBLIC scheme;    // Scheme for signing/encryption
    TPMI_ALG_PUBLIC keyBits;   // Key size in bits
    UINT32 exponent;           // Public exponent
} TPMS_RSA_PARMS, *PTPMS_RSA_PARMS;

// TPMT_PUBLIC: Public area of a TPM object (simplified)
typedef struct _TPMT_PUBLIC {
    TPMI_ALG_PUBLIC type;       // Algorithm ID (e.g., TPM_ALG_RSA)
    TPMI_ALG_PUBLIC nameAlg;    // Name algorithm (e.g., TPM_ALG_SHA256)
    UINT32 objectAttributes;    // Attributes of the object
    TPMS_RSA_PARMS parameters;  // Algorithm-specific parameters
    TPM2B_PUBLIC_KEY_RSA unique; // Unique data (e.g., RSA modulus)
} TPMT_PUBLIC, *PTPMT_PUBLIC;

// TPM2B_PUBLIC: Blob containing TPMT_PUBLIC
typedef struct _TPM2B_PUBLIC {
    USHORT size;
    TPMT_PUBLIC publicArea;
} TPM2B_PUBLIC, *PTPM2B_PUBLIC;

// Function declarations for generating spoofed data
VOID GenerateSpoofedTpm2bDigest(_Out_ PTPM2B_DIGEST Digest);
VOID GenerateSpoofedTpm2bPublic(_Out_ PTPM2B_PUBLIC Public);
VOID GenerateSpoofedPcrValues(_Out_writes_bytes_(NumPcr * sizeof(TPM2B_DIGEST)) PTPM2B_DIGEST PcrArray, _In_ ULONG NumPcr);
