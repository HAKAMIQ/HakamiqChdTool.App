using HakamiqChdTool.App.Core.Queue;
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

    private static int _dispatcherFatalUiCount;

    private bool _globalExceptionHandlersRegistered;

    internal AppSettingsService SettingsService { get; private set; } = null!;

    internal AppSettings Settings { get; private set; } = new();

    internal AppMetadata AppMetadata { get; } = AppMetadata.CreateDefault();

    internal QueueManager QueueManager { get; private set; } = null!;

    protected override void OnStartup(WpfStartupEventArgs e)
    {
        AppPaths.SetPortableMode(AppPaths.DetectPortableModePreference());
        AppLogger.Initialize();
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
        e.Handled = true;

        Exception ex = e.Exception;
        if (DiagnosticLogPolicy.IsExpectedCancellation(ex))
        {
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
            Log.Error(ex, "Unhandled dispatcher exception.");
            TryAppendCrashReport(ex);
        }
        catch
        {
        }

        if (Interlocked.Increment(ref _dispatcherFatalUiCount) != 1)
        {
            return;
        }

        try
        {
            Dispatcher? dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            _ = dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    try
                    {
                        ShowApplicationNotice(
                            ResolveUiText(ApplicationTitleKey),
                            RuntimeDiagnosticFormatter.BuildCrashDialogMessage(ex));
                    }
                    catch (Exception dialogException)
                    {
                        try
                        {
                            Log.Error(dialogException, "Failed to show error dialog.");
                        }
                        catch
                        {
                        }
                    }
                }));
        }
        catch
        {
        }
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
