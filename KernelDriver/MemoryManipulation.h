#pragma once

#include "Driver.h"

NTSTATUS ReadMemory(_In_ PVOID TargetAddress, _Out_ PVOID Buffer, _In_ ULONG Size);
NTSTATUS WriteMemory(_In_ PVOID TargetAddress, _In_ PVOID Buffer, _In_ ULONG Size);
NTSTATUS ProtectMemory(_In_ PVOID TargetAddress, _In_ ULONG Size, _In_ UCHAR NewProtection);

NTSTATUS ReadPhysicalMemory(_In_ PHYSICAL_ADDRESS PhysicalAddress, _Out_ PVOID Buffer, _In_ ULONG Size);
NTSTATUS WritePhysicalMemory(_In_ PHYSICAL_ADDRESS PhysicalAddress, _In_ PVOID Buffer, _In_ ULONG Size);

NTSTATUS ReadProcessMemory(_In_ HANDLE ProcessId, _In_ PVOID Address, _Out_ PVOID Buffer, _In_ ULONG Size);
NTSTATUS WriteProcessMemory(_In_ HANDLE ProcessId, _In_ PVOID Address, _In_ PVOID Buffer, _In_ ULONG Size);

NTSTATUS AllocateKernelMemory(_Out_ PVOID* Address, _In_ ULONG Size, _In_ ULONG PoolType);
NTSTATUS FreeKernelMemory(_In_ PVOID Address);

NTSTATUS HookFunction(_In_ PVOID TargetFunction, _In_ PVOID HookFunction, _Out_ PVOID* OriginalFunction);
NTSTATUS UnhookFunction(_In_ PVOID TargetFunction, _In_ PVOID OriginalFunction);

NTSTATUS PatchMemory(_In_ PVOID Address, _In_ PUCHAR Pattern, _In_ ULONG PatternSize, _In_ PUCHAR Replacement, _In_ ULONG ReplacementSize);
NTSTATUS FindPattern(_In_ PVOID BaseAddress, _In_ ULONG Size, _In_ PUCHAR Pattern, _In_ PUCHAR Mask, _Out_ PVOID* FoundAddress);