using HakamiqChdTool.App.Services.Power;
using HakamiqChdTool.App.Services.Storage;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Conversion;

internal sealed record ConversionSessionNotification(
    string MessageKey,
    StorageHealthSeverity Severity,
    int? CurrentTemperatureCelsius);

internal sealed class ConversionSessionGuard
{
    private readonly IConversionPowerGuard _powerGuard;
    private readonly IStorageTemperatureMonitor _temperatureMonitor;
    private readonly StorageHealthPolicy _healthPolicy;
    private readonly ILogger _log;

    public ConversionSessionGuard(
        IConversionPowerGuard powerGuard,
        IStorageTemperatureMonitor temperatureMonitor,
        StorageHealthPolicy healthPolicy,
        ILogger log)
    {
        _powerGuard = powerGuard ?? throw new ArgumentNullException(nameof(powerGuard));
        _temperatureMonitor = temperatureMonitor ?? throw new ArgumentNullException(nameof(temperatureMonitor));
        _healthPolicy = healthPolicy ?? throw new ArgumentNullException(nameof(healthPolicy));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public ConversionSessionScope BeginCriticalConversionSession(
        StorageDeviceIdentity device,
        StorageTemperaturePolicy temperaturePolicy,
        Action<ConversionSessionNotification>? onNotification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(temperaturePolicy);

        _powerGuard.BeginCriticalConversionSession();

        return new ConversionSessionScope(
            _powerGuard,
            _temperatureMonitor,
            _healthPolicy,
            device,
            temperaturePolicy,
            onNotification,
            _log,
            cancellationToken);
    }
}

internal sealed class ConversionSessionScope : IAsyncDisposable
{
    private readonly IConversionPowerGuard _powerGuard;
    private readonly IStorageTemperatureMonitor _temperatureMonitor;
    private readonly StorageHealthPolicy _healthPolicy;
    private readonly StorageDeviceIdentity _device;
    private readonly StorageTemperaturePolicy _temperaturePolicy;
    private readonly Action<ConversionSessionNotification>? _onNotification;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _monitorCts;
    private readonly Task _monitorTask;

    private StorageHealthSeverity _lastNotifiedSeverity = StorageHealthSeverity.Normal;
    private bool _unavailableNotified;
    private bool _temperatureCapabilityLogged;
    private StorageTemperatureCapability _temperatureCapability = StorageTemperatureCapability.Unknown;
    private int? _maxTemperatureCelsius;
    private bool _temperatureAvailable;

    public ConversionSessionScope(
        IConversionPowerGuard powerGuard,
        IStorageTemperatureMonitor temperatureMonitor,
        StorageHealthPolicy healthPolicy,
        StorageDeviceIdentity device,
        StorageTemperaturePolicy temperaturePolicy,
        Action<ConversionSessionNotification>? onNotification,
        ILogger log,
        CancellationToken cancellationToken)
    {
        _powerGuard = powerGuard ?? throw new ArgumentNullException(nameof(powerGuard));
        _temperatureMonitor = temperatureMonitor ?? throw new ArgumentNullException(nameof(temperatureMonitor));
        _healthPolicy = healthPolicy ?? throw new ArgumentNullException(nameof(healthPolicy));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _temperaturePolicy = temperaturePolicy ?? throw new ArgumentNullException(nameof(temperaturePolicy));
        _onNotification = onNotification;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = Task.Run(() => MonitorTemperatureAsync(_monitorCts.Token), CancellationToken.None);
    }

    public bool PowerGuardEnabled { get; private set; } = true;

    public bool TemperatureAvailable => _temperatureAvailable;

    public StorageTemperatureCapability TemperatureCapability => _temperatureCapability;

    public int? MaxTemperatureCelsius => _maxTemperatureCelsius;

    public async ValueTask DisposeAsync()
    {
        try
        {
            _monitorCts.Cancel();

            try
            {
                await _monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "Storage temperature monitor ended with a non-fatal error.");
            }
        }
        finally
        {
            _monitorCts.Dispose();
            _powerGuard.EndCriticalConversionSession();
            PowerGuardEnabled = false;
        }
    }

    private async Task MonitorTemperatureAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                StorageTemperatureReading reading = await _temperatureMonitor
                    .TryReadAsync(_device, cancellationToken)
                    .ConfigureAwait(false);

                if (!ObserveReading(reading))
                {
                    return;
                }

                await Task.Delay(_temperaturePolicy.PollingInterval, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool ObserveReading(StorageTemperatureReading reading)
    {
        if (reading.IsAvailable && reading.CurrentCelsius is int current)
        {
            _temperatureAvailable = true;
            _temperatureCapability = StorageTemperatureCapability.Available;
            _maxTemperatureCelsius = _maxTemperatureCelsius is int previous
                ? Math.Max(previous, current)
                : current;
        }
        else
        {
            if (reading.Capability != StorageTemperatureCapability.Unknown)
            {
                _temperatureCapability = reading.Capability;
            }

            LogTemperatureCapabilityOnce(reading);
        }

        StorageHealthDecision decision = _healthPolicy.Evaluate(reading, _temperaturePolicy);
        if (decision.Severity == StorageHealthSeverity.Normal)
        {
            return true;
        }

        if (decision.Severity == StorageHealthSeverity.Unavailable)
        {
            if (_unavailableNotified)
            {
                return !IsStableUnavailableCapability(_temperatureCapability);
            }

            _unavailableNotified = true;
            _lastNotifiedSeverity = decision.Severity;

            _log.Debug(
                "Storage temperature monitoring unavailable. Severity={Severity}; Device={Device}; TemperatureCapability={TemperatureCapability}; Reason={ReasonCode}; MessageKey={MessageKey}",
                decision.Severity,
                _device.DisplayName,
                _temperatureCapability,
                reading.UnavailableReasonCode,
                decision.MessageKey);

            _onNotification?.Invoke(new ConversionSessionNotification(
                decision.MessageKey,
                decision.Severity,
                decision.CurrentCelsius));

            return !IsStableUnavailableCapability(_temperatureCapability);
        }

        if (decision.Severity <= _lastNotifiedSeverity)
        {
            return true;
        }

        _lastNotifiedSeverity = decision.Severity;
        _log.Warning(
            "Storage health notification. Severity={Severity}; Device={Device}; Temperature={Temperature}; MessageKey={MessageKey}",
            decision.Severity,
            _device.DisplayName,
            decision.CurrentCelsius,
            decision.MessageKey);

        _onNotification?.Invoke(new ConversionSessionNotification(
            decision.MessageKey,
            decision.Severity,
            decision.CurrentCelsius));

        return true;
    }

    private void LogTemperatureCapabilityOnce(StorageTemperatureReading reading)
    {
        if (_temperatureCapabilityLogged)
        {
            return;
        }

        _temperatureCapabilityLogged = true;

        if (reading.DiagnosticException is Exception ex
            && !IsStableUnavailableCapability(reading.Capability))
        {
            _log.Debug(
                ex,
                "Storage temperature capability unavailable for conversion session. Device={Device}; TemperatureCapability={TemperatureCapability}; Reason={ReasonCode}",
                _device.DisplayName,
                _temperatureCapability,
                reading.UnavailableReasonCode);
            return;
        }

        _log.Debug(
            "Storage temperature capability unavailable for conversion session. Device={Device}; TemperatureCapability={TemperatureCapability}; Reason={ReasonCode}",
            _device.DisplayName,
            _temperatureCapability,
            reading.UnavailableReasonCode);
    }

    private static bool IsStableUnavailableCapability(StorageTemperatureCapability capability) =>
        capability is StorageTemperatureCapability.AccessDenied
            or StorageTemperatureCapability.Unsupported
            or StorageTemperatureCapability.Unavailable;
}
