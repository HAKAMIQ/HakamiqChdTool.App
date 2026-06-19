using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;
using Microsoft.Win32;
using Serilog;

namespace HakamiqChdTool.App.Ui.Shell;

internal sealed partial class HqOptionsShell : IDisposable
{
    private const string DatabaseInitialStatusKey = "LocOptions_RedumpDatabaseInitialStatus";
    private const string InvalidFieldFallbackKey = "LocOptions_InvalidFieldFallback";
    private const string InvalidDataTitleKey = "LocOptions_InvalidDataTitle";
    private const string SelectRedumpDatTitleKey = "LocOptions_SelectRedumpDatTitle";
    private const string RedumpDatFilterKey = "LocOptions_RedumpDatFilter";
    private const string RedumpImportRunningKey = "LocOptions_RedumpImportRunning";
    private const string RedumpImportRowsProcessedKey = "LocOptions_RedumpImportRowsProcessed";
    private const string RedumpImportSuccessTitleKey = "LocOptions_RedumpImportSuccessTitle";
    private const string RedumpImportFailedTitleKey = "LocOptions_RedumpImportFailedTitle";
    private const string RedumpImportErrorTitleKey = "LocOptions_RedumpImportErrorTitle";
    private const string OperationErrorTitleKey = "LocOptions_OperationErrorTitle";
    private const string MissingDataTitleKey = "LocOptions_MissingDataTitle";
    private const string RedumpDownloadUrlRequiredKey = "LocOptions_RedumpDownloadUrlRequired";
    private const string RedumpDownloadInvalidUrlKey = "LocOptions_RedumpDownloadInvalidUrl";
    private const string RedumpDownloadRunningKey = "LocOptions_RedumpDownloadRunning";
    private const string RedumpSyncFailedTitleKey = "LocOptions_RedumpSyncFailedTitle";
    private const string RedumpDownloadFailedKey = "LocOptions_RedumpDownloadFailed";
    private const string RedumpDownloadErrorTitleKey = "LocOptions_RedumpDownloadErrorTitle";
    private const string DatabaseCheckingKey = "LocOptions_DatabaseChecking";
    private const string DatabaseAvailableKey = "LocOptions_DatabaseAvailable";
    private const string DatabaseMissingKey = "LocOptions_DatabaseMissing";
    private const string DatabaseReadFailedKey = "LocOptions_DatabaseReadFailed";

    private static readonly string GeneralTabKey = OptionsWindow.GeneralTabKey;
    private static readonly string PathsTabKey = OptionsWindow.PathsTabKey;
    private static readonly string RedumpTabKey = OptionsWindow.RedumpTabKey;
    private static readonly string ProcessingTabKey = OptionsWindow.ProcessingTabKey;
    private static readonly string ExternalToolsTabKey = OptionsWindow.ExternalToolsTabKey;
    private static readonly string PerformanceTabKey = OptionsWindow.PerformanceTabKey;

    private static readonly ILogger Logger = Log.ForContext<HqOptionsShell>();

    private readonly OptionsWindow _owner;
    private readonly AppSettings _currentSettings;
    private readonly IAppFeatureService _appFeatureService;
    private readonly RedumpGitHubSyncManager _syncManager = new();
    private readonly CancellationTokenSource _windowLifetimeCts = new();
    private readonly ToolTipEventHandler _toolTipOpeningHandler;

    private CancellationTokenSource? _databaseStateRefreshCts;
    private CancellationTokenSource? _externalToolsRefreshCts;
    private int _databaseStateRefreshGeneration;
    private int _externalToolsRefreshGeneration;
    private bool _isClosed;
    private bool _isAttached;

    public HqOptionsShell(
        OptionsWindow owner,
        AppSettings currentSettings,
        IAppFeatureService appFeatureService)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _currentSettings = currentSettings ?? throw new ArgumentNullException(nameof(currentSettings));
        _appFeatureService = appFeatureService ?? throw new ArgumentNullException(nameof(appFeatureService));
        _toolTipOpeningHandler = OnToolTipOpening;
    }

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _owner.AddHandler(ToolTipService.ToolTipOpeningEvent, _toolTipOpeningHandler, handledEventsToo: true);
        _isAttached = true;
    }

    public void Initialize()
    {
        ApplyFeatureAvailabilityToViewModel();
        _owner.ViewModel.Load(_owner.ResultSettings);
        EnforceFeatureAvailabilityOnViewModel(showDialog: false);
        _owner.ResultSettings = _currentSettings.Clone();
        _owner.ViewModel.IsDatabaseAvailable = false;
        _owner.ViewModel.DatabaseStatusText = ResolveUiText(DatabaseInitialStatusKey);
    }

    public void OnLoaded()
    {
        UpdateVisiblePanel();

        _ = _owner.Dispatcher.BeginInvoke(
            new Action(QueueDatabaseStateRefresh),
            DispatcherPriority.ContextIdle);

        _ = _owner.Dispatcher.BeginInvoke(
            new Action(QueueExternalToolsRefresh),
            DispatcherPriority.ContextIdle);
    }

    public void Dispose()
    {
        _isClosed = true;

        _owner.RedumpPanelView.DownloadDatabaseRequested -= DownloadDatabase;
        _owner.RedumpPanelView.ImportRedumpDatabaseRequested -= ImportRedumpDatabase;
        _owner.ExternalToolsPanelView.RecheckRequested -= RecheckExternalTools;
        _owner.ExternalToolsPanelView.OpenToolsFolderRequested -= OpenExternalToolsFolder;
        _owner.ExternalToolsPanelView.CopySetupInstructionsRequested -= CopyExternalToolsSetupInstructions;

        if (_isAttached)
        {
            _owner.RemoveHandler(ToolTipService.ToolTipOpeningEvent, _toolTipOpeningHandler);
            _isAttached = false;
        }

        _databaseStateRefreshCts?.Cancel();
        _databaseStateRefreshCts?.Dispose();
        _externalToolsRefreshCts?.Cancel();
        _externalToolsRefreshCts?.Dispose();

        _windowLifetimeCts.Cancel();
        _windowLifetimeCts.Dispose();

        _syncManager.Dispose();
    }

    public void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            _owner.DragMove();
        }
        catch (InvalidOperationException ex)
        {
            Logger.Debug(ex, "Options window drag was ignored because DragMove was not valid for the current mouse state.");
        }
    }






















}
