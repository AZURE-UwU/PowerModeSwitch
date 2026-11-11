// @file ModeRegistryMonitor.cs
// @author Codex
// @module PowerModeSwitch.Services

using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PowerModeSwitch.Services;

internal sealed class ModeRegistryMonitor : IDisposable
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Services\LITSSVC\LNBITS\IC\MMC";
    private const uint RegNotifyChangeLastSet = 0x00000004;
    private const uint RegNotifyThreadAgnostic = 0x10000000;

    private readonly Action _changed;
    private readonly RegistryKey? _key;
    private readonly AutoResetEvent _event = new(false);
    private RegisteredWaitHandle? _wait;
    private bool _disposed;

    public ModeRegistryMonitor(Action changed)
    {
        _changed = changed;
        _key = RegistryKey
            .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(KeyPath, writable: false);
    }

    public bool IsAvailable => _key is not null;

    public void Start()
    {
        if (_key is null || _disposed)
        {
            return;
        }

        Arm();
        _wait = ThreadPool.RegisterWaitForSingleObject(
            _event,
            (_, _) =>
            {
                if (_disposed)
                {
                    return;
                }

                Arm();
                _changed();
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private void Arm()
    {
        if (_key is null || _disposed)
        {
            return;
        }

        try
        {
            RegNotifyChangeKeyValue(
                _key.Handle.DangerousGetHandle(),
                bWatchSubTree: false,
                RegNotifyChangeLastSet | RegNotifyThreadAgnostic,
                _event.SafeWaitHandle,
                asynchronous: true);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _wait?.Unregister(null);
        _key?.Dispose();
        _event.Dispose();
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        IntPtr key,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubTree,
        uint notifyFilter,
        SafeHandle eventHandle,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
