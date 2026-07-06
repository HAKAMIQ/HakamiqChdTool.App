using Microsoft.Win32.SafeHandles;
using Serilog;
using System;
using System.Runtime.InteropServices;

namespace HakamiqChdTool.App.Services.Power;

internal sealed class WindowsConversionPowerGuard : IConversionPowerGuard
{
    private const uint PowerRequestContextVersion = 0;
    private const string PowerRequestReason = "Hakamiq CHD conversion is running.";

    private readonly object _gate = new();
    private readonly ILogger _log;
    private int _activeCount;
    private bool _powerRequestActive;
    private bool _disposed;
    private SafePowerRequestHandle? _powerRequestHandle;

    public WindowsConversionPowerGuard()
        : this(Log.ForContext<WindowsConversionPowerGuard>())
    {
    }

    public WindowsConversionPowerGuard(ILogger log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void BeginCriticalConversionSession()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _activeCount++;
            if (_activeCount > 1)
            {
                return;
            }

            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            SafePowerRequestHandle handle = CreatePowerRequestHandle();
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                _log.Warning("Power guard could not create conversion power request. Win32Error={Win32Error}", error);
                return;
            }

            if (!PowerSetRequest(handle, PowerRequestType.SystemRequired))
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                _log.Warning("Power guard could not request system-required state for conversion. Win32Error={Win32Error}", error);
                return;
            }

            _powerRequestHandle = handle;
            _powerRequestActive = true;
            _log.Information("Power guard enabled for conversion session.");
        }
    }

    public void EndCriticalConversionSession()
    {
        lock (_gate)
        {
            if (_activeCount <= 0)
            {
                return;
            }

            _activeCount--;
            if (_activeCount > 0)
            {
                return;
            }

            ClearPowerRequest();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _activeCount = 0;
            ClearPowerRequest();
            _disposed = true;
        }
    }

    private void ClearPowerRequest()
    {
        SafePowerRequestHandle? handle = _powerRequestHandle;
        _powerRequestHandle = null;

        if (!_powerRequestActive)
        {
            handle?.Dispose();
            return;
        }

        _powerRequestActive = false;

        if (!OperatingSystem.IsWindows())
        {
            handle?.Dispose();
            return;
        }

        try
        {
            if (handle is not null && !handle.IsInvalid)
            {
                if (!PowerClearRequest(handle, PowerRequestType.SystemRequired))
                {
                    int error = Marshal.GetLastWin32Error();
                    _log.Warning("Power guard could not clear conversion power request. Win32Error={Win32Error}", error);
                }
                else
                {
                    _log.Information("Power guard disabled for conversion session.");
                }
            }
        }
        finally
        {
            handle?.Dispose();
        }
    }

    private static SafePowerRequestHandle CreatePowerRequestHandle()
    {
        IntPtr reason = IntPtr.Zero;

        try
        {
            reason = Marshal.StringToHGlobalUni(PowerRequestReason);

            PowerRequestContext context = new()
            {
                Version = PowerRequestContextVersion,
                Flags = PowerRequestContextFlags.SimpleString,
                SimpleReasonString = reason,
                DetailedReason = IntPtr.Zero
            };

            return PowerCreateRequest(ref context);
        }
        finally
        {
            if (reason != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(reason);
            }
        }
    }

    [Flags]
    private enum PowerRequestContextFlags : uint
    {
        SimpleString = 0x00000001
    }

    private enum PowerRequestType
    {
        DisplayRequired = 0,
        SystemRequired = 1,
        AwayModeRequired = 2,
        ExecutionRequired = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerRequestContext
    {
        public uint Version;
        public PowerRequestContextFlags Flags;
        public IntPtr SimpleReasonString;
        public IntPtr DetailedReason;
    }

    private sealed class SafePowerRequestHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafePowerRequestHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafePowerRequestHandle PowerCreateRequest(ref PowerRequestContext context);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(
        SafePowerRequestHandle powerRequest,
        PowerRequestType requestType);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(
        SafePowerRequestHandle powerRequest,
        PowerRequestType requestType);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}