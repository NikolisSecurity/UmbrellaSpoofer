#pragma once

#include "Driver.h"

// DEVICE_CONTEXT is now defined in Driver.h to avoid conflicts

NTSTATUS HandleSpoofHardware(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext);
NTSTATUS HandleRestoreHardware(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext);
NTSTATUS HandleQuerySystemInfo(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext);
NTSTATUS HandleMemoryManipulation(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext);
NTSTATUS HandleSpoofAll(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext);
NTSTATUS HandleRestoreAll(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext);
NTSTATUS HandleQueryGeneratedValues(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext);
NTSTATUS HandleReadWmiResponse(_In_ WDFREQUEST Request, _In_ PDEVICE_CONTEXT DeviceContext);

VOID InitializeSpoofedRegistryValues();
VOID UmbrellaEvtDeviceContextCleanup(_In_ WDFOBJECT DeviceObject);