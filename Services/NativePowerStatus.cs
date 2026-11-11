// @file NativePowerStatus.cs
// @author Codex
// @module PowerModeSwitch.Native

using System.Runtime.InteropServices;
using PowerModeSwitch.Models;

namespace PowerModeSwitch.Services;

internal static class NativePowerStatus
{
    private const byte AcOnline = 1;
    private const byte AcOffline = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static AdapterState GetAdapterState()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            return AdapterState.Unknown;
        }

        return status.ACLineStatus switch
        {
            AcOnline => AdapterState.Ac,
            AcOffline => AdapterState.Battery,
            _ => AdapterState.Unknown
        };
    }
}
