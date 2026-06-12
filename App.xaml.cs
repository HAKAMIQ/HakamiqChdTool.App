using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Startup;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Views;
using Serilog;
using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LanguageService = HakamiqChdTool.App.Localization.AppLanguageService;
using WpfApplication = System.Windows.Application;
using WpfExitEventArgs = System.Windows.ExitEventArgs;
using WpfStartupEventArgs = System.Windows.StartupEventArgs;

namespace HakamiqChdTool.App;

public partial class App : WpfApplication
{
    private const string ApplicationTitleKey = "LocUi_WindowTitle";
    private const string AdministratorWarningBodyKey = "LocApp_AdministratorWarningBody";

    private const string SingleInstanceMutexName = @"Local\HakamiqChdTool.App.SingleInstance";

    private Mutex? _singleInstanceMutex;

    private bool _globalExceptionHandlersRegistered;

    internal AppSettingsService SettingsService { get; private set; } = null!;

    internal AppSettings Settings { get; private set; } = new();

    internal AppMetadata AppMetadata { get; } = AppMetadata.CreateDefault();

    internal QueueManager QueueManager { get; private set; } = null!;

    protected override void OnStartup(WpfStartupEventArgs e)
    {
        AppPaths.SetPortableMode(AppPaths.DetectPortableModePreference());
        AppLogger.Initialize();

        if (!TryAcquireSingleInstanceMutex())
        {
            Log.Information("Another Hakamiq CHD Tool instance is already running. Exiting duplicate instance.");
            AppLogger.Shutdown();
            Shutdown(0);
            return;
        }

        RegisterGlobalExceptionHandlers();

        SettingsService = new AppSettingsService();
        Settings = SettingsService.Load();

        AppPaths.SetPortableMode(Settings.PortableMode);
        ThemeService.Instance.Initialize(Settings.Theme);
        LanguageService.Instance.Initialize(Settings.UiLanguage);

        Log.Information("Application startup.");

        base.OnStartup(e);

        MainWindowBootstrap bootstrap = CreateMainWindowBootstrap();
        QueueManager = CreateQueueManager(bootstrap);

        MainWindow mainWindow = new(bootstrap, QueueManager);
        MainWindow = mainWindow;
        mainWindow.Show();

        QueueAdministratorWarningIfNeeded();

#if DEBUG
        Log.Debug("Startup diagnostics (Debug build).");
#endif
    }

    private bool TryAcquireSingleInstanceMutex()
    {
        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: SingleInstanceMutexName,
                createdNew: out bool createdNew);

            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or ApplicationException)
        {
            Log.Warning(ex, "Could not create the single-instance mutex. Continuing startup to avoid a false launch block.");
            return true;
        }
    }

    private void ReleaseSingleInstanceMutex()
    {
        Mutex? mutex = _singleInstanceMutex;
        _singleInstanceMutex = null;

        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException ex)
        {
            Log.Debug(ex, "Single-instance mutex release skipped because the current thread does not own it.");
        }
        finally
        {
            mutex.Dispose();
        }
    }

    internal MainWindowBootstrap CreateMainWindowBootstrap()
    {
        return MainWindowBootstrap.CreateDefault(
            SettingsService,
            Settings,
            AppMetadata,
            RuntimeToolService.Instance);
    }

    internal QueueManager CreateQueueManager(MainWindowBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);

        return new QueueManager(
            bootstrap.WorkflowOrchestrator,
            () => Settings,
            () => bootstrap.ChdmanPathResolver.ResolvePath(Settings),
            maxConcurrentItems: AppSettings.NormalizeMaxConcurrentConversions(Settings.MaxConcurrentConversions),
            canUseAppFeature: bootstrap.AppFeatureService.IsEnabled);
    }

    protected override void OnExit(WpfExitEventArgs e)
    {
        UnregisterGlobalExceptionHandlers();

        try
        {
            if (SettingsService is not null)
            {
                AppSettings persistedSettings = SettingsService.Load();

                persistedSettings.UiLanguage = LanguageService.NormalizeLanguageName(persistedSettings.UiLanguage);
                persistedSettings.HasSeenAdministratorWarning =
                    persistedSettings.HasSeenAdministratorWarning || Settings.HasSeenAdministratorWarning;

                SettingsService.Save(persistedSettings);
                SettingsService.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Settings flush on application exit failed.");
        }

        ReleaseSingleInstanceMutex();

        Log.Information("Application exit. Code: {ExitCode}", e.ApplicationExitCode);
        AppLogger.Shutdown();

        base.OnExit(e);
    }

    private void QueueAdministratorWarningIfNeeded()
    {
        if (Settings.HasSeenAdministratorWarning)
        {
            return;
        }

        bool elevated;
        try
        {
            elevated = IsRunningElevated();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not determine whether the app is running elevated.");
            return;
        }

        if (!elevated)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                try
                {
                    ShowApplicationNotice(
                        ResolveUiText(ApplicationTitleKey),
                        ResolveUiText(AdministratorWarningBodyKey));

                    Settings.HasSeenAdministratorWarning = true;
                    SettingsService.Save(Settings);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Administrator warning could not be shown or persisted.");
                }
            }));
    }

    private static bool IsRunningElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);

        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        if (_globalExceptionHandlersRegistered)
        {
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _globalExceptionHandlersRegistered = true;
    }

    private void UnregisterGlobalExceptionHandlers()
    {
        if (!_globalExceptionHandlersRegistered)
        {
            return;
        }

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        _globalExceptionHandlersRegistered = false;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            e.SetObserved();

            AggregateException agg = e.Exception;
            if (DiagnosticLogPolicy.IsExpectedCancellation(agg))
            {
                Log.Debug("Observed expected cancelled task exception.");
                return;
            }

            Log.Error(agg, "Unobserved task exception.");

            if (agg.InnerExceptions.Count > 0)
            {
                TryAppendCrashReport(agg.InnerExceptions[0]);
            }
        }
        catch
        {
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Exception ex = e.Exception;

        if (DiagnosticLogPolicy.IsExpectedCancellation(ex))
        {
            e.Handled = true;

            try
            {
                Log.Debug("Handled expected dispatcher cancellation exception.");
            }
            catch
            {
            }

            return;
        }

        try
        {
            if (IsUiConstructionFault(ex))
            {
                Log.Error(ex, "Fatal UI construction exception. The application will shut down safely.");
            }
            else
            {
                Log.Error(ex, "Fatal dispatcher exception. The application will shut down safely.");
            }

            TryAppendCrashReport(ex);
        }
        catch
        {
        }

        e.Handled = true;
        RequestFatalShutdown();
    }

    private static void RequestFatalShutdown()
    {
        try
        {
            Dispatcher? dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                Environment.Exit(1);
                return;
            }

            dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(() =>
                {
                    try
                    {
                        WpfApplication.Current?.Shutdown(1);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Fatal dispatcher shutdown failed; forcing process exit.");
                        Environment.Exit(1);
                    }
                }));
        }
        catch
        {
            Environment.Exit(1);
        }
    }

    private static bool IsUiConstructionFault(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            string typeName = current.GetType().FullName ?? current.GetType().Name;
            if (typeName.Contains("XamlParseException", StringComparison.Ordinal)
                || typeName.Contains("XamlObjectWriterException", StringComparison.Ordinal))
            {
                return true;
            }

            string stack = current.StackTrace ?? string.Empty;
            if (stack.Contains("InitializeComponent", StringComparison.Ordinal)
                || stack.Contains("System.Windows.Application.LoadComponent", StringComparison.Ordinal)
                || stack.Contains("System.Windows.Markup.WpfXamlLoader", StringComparison.Ordinal)
                || stack.Contains("System.Xaml.XamlObjectWriter", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            if (DiagnosticLogPolicy.IsExpectedCancellation(ex))
            {
                Log.Debug(
                    "UnhandledException received expected cancellation. IsTerminating: {IsTerminating}",
                    e.IsTerminating);

                return;
            }

            Log.Error(
                ex,
                "Unhandled application exception. IsTerminating: {IsTerminating}",
                e.IsTerminating);

            TryAppendCrashReport(ex);
        }
        else
        {
            Log.Error(
                "Unhandled application exception object. IsTerminating: {IsTerminating}",
                e.IsTerminating);
        }
    }

    private static void TryAppendCrashReport(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);

            string path = Path.Combine(AppPaths.LogsDirectory, "last-crash.txt");

            string block =
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}" +
                $"OS: {Environment.OSVersion}{Environment.NewLine}" +
                $"CLR: {Environment.Version}{Environment.NewLine}" +
                $"AppBase: {AppContext.BaseDirectory}{Environment.NewLine}" +
                $"{ex}{Environment.NewLine}{new string('-', 60)}{Environment.NewLine}";

            File.AppendAllText(path, block);
        }
        catch
        {
        }
    }

    private static void ShowApplicationNotice(string title, string message)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Window? owner = WpfApplication.Current?.MainWindow;
        var dialog = new RedumpNoticeDialog(title.Trim(), message.Trim());

        if (owner is not null && owner.IsLoaded)
        {
            dialog.Owner = owner;
        }

        _ = dialog.ShowDialog();
    }

    private static string ResolveUiText(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return ArabicUi.ResolveDisplayString(key);
    }
}
