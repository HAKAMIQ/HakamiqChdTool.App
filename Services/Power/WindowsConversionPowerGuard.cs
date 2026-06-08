using Serilog;
using System;
using System.Runtime.InteropServices;

namespace HakamiqChdTool.App.Services.Power;

internal sealed class WindowsConversionPowerGuard : IConversionPowerGuard
{
    private const ExecutionState EsContinuous = ExecutionState.Continuous;
    private const ExecutionState EsSystemRequired = ExecutionState.SystemRequired;

    private readonly object _gate = new();
    private readonly ILogger _log;
    private int _activeCount;
    private bool _disposed;

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

            ExecutionState previous = SetThreadExecutionState(EsContinuous | EsSystemRequired);
            if (previous == 0)
            {
                _log.Warning("Power guard could not request ES_SYSTEM_REQUIRED for conversion.");
            }
            else
            {
                _log.Information("Power guard enabled for conversion session.");
            }
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

            ClearExecutionState();
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
            ClearExecutionState();
            _disposed = true;
        }
    }

    private void ClearExecutionState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ExecutionState previous = SetThreadExecutionState(EsContinuous);
        if (previous == 0)
        {
            _log.Warning("Power guard could not clear conversion execution state.");
            return;
        }

        _log.Information("Power guard disabled for conversion session.");
    }

    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);
}

