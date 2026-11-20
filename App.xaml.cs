using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Startup;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Configuration;
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
using HakamiqChdTool.App.Ui.WpfAdapters;

namespace HakamiqChdTool.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The WPF application lifetime releases and disposes the single-instance mutex through OnExit; failed acquisition is disposed immediately.")]
public partial class App : WpfApplication
{
    private const string ApplicationTitleKey = "LocUi_WindowTitle";
    private const string AdministratorWarningBodyKey = "LocApp_AdministratorWarningBody";

    private Mutex? _singleInstanceMutex;

    private InstanceChannel? _launchChannel;

    // Where a later launch goes: the main window, or the quick window while it is open.
    private Action<LaunchRequest>? _launchHandler;

    private bool _globalExceptionHandlersRegistered;

    internal bool IsIntegrationTestHost { get; set; }

    internal AppSettingsService SettingsService { get; private set; } = null!;

    internal AppSettings Settings { get; private set; } = new();

    internal AppMetadata AppMetadata { get; } = AppMetadata.CreateDefault();

    internal QueueManager QueueManager { get; private set; } = null!;

    protected override void OnStartup(WpfStartupEventArgs e)
    {
        AccessibleText.Register();

        if (IsIntegrationTestHost)
        {
            base.OnStartup(e);
            return;
        }

        AppPaths.SetPortableMode(AppPaths.DetectPortableModePreference());
        AppLogger.Initialize();

        LaunchRequest launch = ParseLaunchRequest(e.Args, out string? launchError);

        if (!TryAcquireSingleInstanceMutex())
        {
            if (launchError is not null)
            {
                ShowLaunchUsageWithoutWindow(launchError);
            }

            Log.Information("Another CHD Tool instance is already running in this Windows session. Handing over the launch request and exiting.");
            _ = InstanceChannel.TrySend(
                InstanceChannel.DefaultPipeName,
                launch,
                launch.HasPaths ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(2));
            AppLogger.Shutdown();
            Shutdown(0);
            return;
        }

        RegisterGlobalExceptionHandlers();

        SettingsService = new AppSettingsService();
        Settings = SettingsService.Load();

        AppPaths.SetPortableMode(Settings.PortableMode);
        HelperToolPriority.Mode = Settings.ChdmanPriorityMode;
        ThemeService.Instance.Initialize(Settings.Theme);
        LanguageService.Instance.Initialize(Settings.UiLanguage);

        Log.Information("Application startup.");

        base.OnStartup(e);

        MainWindowBootstrap bootstrap = CreateMainWindowBootstrap();
        QueueManager = CreateQueueManager(bootstrap);

        if (QuickLaunch.OpensQuickWindow(launch, Settings))
        {
            ShowQuickWindow(bootstrap, launch);
        }
        else
        {
            MainWindow mainWindow = ShowMainWindow(bootstrap);
            mainWindow.ExitWhenDone = launch.ExitWhenDone;
            if (launch.HasPaths)
            {
                mainWindow.HandleLaunchRequest(launch);
            }

            if (launchError is not null)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => mainWindow.ShowLaunchUsage(launchError)));
            }

            QueueAdministratorWarningIfNeeded();
        }

        StartLaunchChannel();

        if (Settings.ExplorerMenuEnabled)
        {
            // Keeps the entries pointing at this copy of the app and in the current language.
            _ = HakamiqChdTool.App.MainWindow.SyncExplorerMenu(enabled: true);
        }

#if DEBUG
        Log.Debug("Startup diagnostics (Debug build).");
#endif
    }

    private static LaunchRequest ParseLaunchRequest(string[] args, out string? launchError)
    {
        launchError = null;
        if (LaunchArgs.TryParse(args, out LaunchRequest request, out string? error))
        {
            return request;
        }

        Log.Warning("Ignoring launch arguments: {Error}", error);
        launchError = error ?? string.Empty;
        return LaunchRequest.Empty;
    }

    // A second launch with bad arguments has no window of its own; it shows the usage and still activates
    // the running window.
    private static void ShowLaunchUsageWithoutWindow(string error)
    {
        try
        {
            AppSettings settings = new AppSettingsService().Load();
            LanguageService.Instance.Initialize(settings.UiLanguage);
            ShowLaunchUsageMessage(owner: null, error);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not show launch usage.");
        }
    }

    private MainWindow ShowMainWindow(MainWindowBootstrap bootstrap)
    {
        MainWindow mainWindow = new(bootstrap, QueueManager);
        MainWindow = mainWindow;
        _launchHandler = request => mainWindow.HandleLaunchRequest(request);
        mainWindow.Show();
        return mainWindow;
    }

    // An Explorer menu launch: the quick window runs the files with the saved settings. Later Explorer launches join
    // it; "Open in app" hands the files that did not finish to the main window in this same process.
    private void ShowQuickWindow(MainWindowBootstrap bootstrap, LaunchRequest launch)
    {
        bool mayDeleteSource = QuickLaunch.MayDeleteSource(launch.Mode, Settings);
        QueueExecutionProfile profile = QuickProfileFor(launch.Mode);
        var window = new QuickRunWindow();
        var host = new HakamiqChdTool.App.QueueRun.QuickRunHost(
            QueueManager,
            () => Settings,
            bootstrap.PostConversionArtifacts,
            Dispatcher,
            () => window,
            autoStart: !mayDeleteSource);
        var viewModel = new HakamiqChdTool.App.ViewModels.QuickRunViewModel(host, profile, mayDeleteSource, ArabicUi.Get);
        bool handedToMainWindow = false;

        host.ErrorRaised += (title, body) =>
        {
            var dialog = new RedumpNoticeDialog(ResolveUiText(title), ResolveUiText(body)) { Owner = window };
            _ = dialog.ShowDialog();
        };

        viewModel.OpenInAppRequested += async paths =>
        {
            if (handedToMainWindow || host.IsBusy)
            {
                return;
            }

            handedToMainWindow = true;
            await host.DisposeAsync().ConfigureAwait(true);
            QueueManager = CreateQueueManager(bootstrap);
            MainWindow mainWindow = ShowMainWindow(bootstrap);
            if (paths.Count > 0)
            {
                mainWindow.HandleLaunchRequest(new LaunchRequest(launch.Mode, paths));
            }

            window.CloseNow();
        };

        // The queue is shut down before the window closes, so the app exits with nothing running.
        window.BeforeCloseWhenIdle = () => handedToMainWindow ? Task.CompletedTask : host.DisposeAsync().AsTask();

        window.Attach(viewModel);
        MainWindow = window;
        _launchHandler = request =>
        {
            if (handedToMainWindow)
            {
                return;
            }

            if (!request.HasPaths)
            {
                window.Activate();
                return;
            }

            LaunchMode mode = request.Mode == LaunchMode.None ? launch.Mode : request.Mode;
            _ = host.AddAsync(QuickProfileFor(mode), request.Paths);
        };

        window.Show();
        _ = StartQuickRunAsync(bootstrap, host, profile, launch.Paths);
    }

    private static async Task StartQuickRunAsync(
        MainWindowBootstrap bootstrap,
        HakamiqChdTool.App.QueueRun.QuickRunHost host,
        QueueExecutionProfile profile,
        IReadOnlyList<string> paths)
    {
        try
        {
            // The main window does this while it loads; the bundled tools must be ready before a run.
            await Task.Run(bootstrap.RuntimeTools.EnsureInitialized).ConfigureAwait(true);
            await host.AddAsync(profile, paths).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Quick window run failed to start.");
        }
    }

    private static QueueExecutionProfile QuickProfileFor(LaunchMode mode) => mode switch
    {
        LaunchMode.Extract => QueueExecutionProfile.QuickExtract,
        LaunchMode.Verify => QueueExecutionProfile.QuickVerify,
        _ => QueueExecutionProfile.QuickConvert
    };

    private void StartLaunchChannel()
    {
        try
        {
            _launchChannel = InstanceChannel.Start(
                InstanceChannel.DefaultPipeName,
                request => Dispatcher.BeginInvoke(new Action(() => _launchHandler?.Invoke(request))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warning(ex, "Launch channel could not start.");
        }
    }

    // A Windows message box, not a WPF dialog: it honors the left-to-right embedding of each usage line inside
    // Arabic text, which WPF text layout does not.
    internal static void ShowLaunchUsageMessage(Window? owner, string error)
    {
        string text = ResolveUiText("LocLaunch_UsageBody") + Environment.NewLine + Environment.NewLine + LaunchArgs.FormatUsageForDialog(error);
        string title = ResolveUiText("LocLaunch_UsageTitle");
        MessageBoxOptions options = LanguageService.Instance.CurrentLanguageName.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
            ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
            : MessageBoxOptions.None;
        _ = owner is null
            ? MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, options)
            : MessageBox.Show(owner, text, title, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, options);
    }

    private bool TryAcquireSingleInstanceMutex()
    {
        if (SingleInstanceGate.TryAcquire(out Mutex? mutex, out Exception? error))
        {
            _singleInstanceMutex = mutex;
            return true;
        }

        if (error is not null)
        {
            Log.Warning(error, "Could not create the single-instance mutex. Continuing startup without single-instance protection.");
            return true;
        }

        return false;
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
            maxConcurrentItems: AppSettings.NormalizeMaxConcurrentConversions(Settings.MaxConcurrentConversions));
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

        _launchChannel?.Dispose();
        _launchChannel = null;

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
