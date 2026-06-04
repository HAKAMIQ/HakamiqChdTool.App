using HakamiqChdTool.App.Coordination;
using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Licensing;
using HakamiqChdTool.App.Services.PostProcessing;
using HakamiqChdTool.App.Services.UiPorts;
using HakamiqChdTool.UiPorts.Dispatching;
using HakamiqChdTool.UiPorts.Resources;
using HakamiqChdTool.UiPorts.Shell;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace HakamiqChdTool.App;

[SupportedOSPlatform("windows10.0.17763.0")]
public partial class MainWindow : Window
{
    private readonly ConcurrentDictionary<Guid, TaskQueueStateAdapter> _sinkIndex = new();
    private readonly QueueRowStore _queueRowStore = new();
    private readonly QueueViewportService _viewport;
    private readonly VirtualizedQueueCollection _queueView;
    private readonly ConcurrentDictionary<Guid, ChdQueueItem> _pendingQueueUiSnapshots = new();
    private readonly AppSettingsService _settingsService;
    private readonly QueueManager _queue;
    private readonly QueueController _queueController;
    private readonly MainWindowViewModel _viewModel;
    private readonly AppMetadata _appMetadata;
    private readonly RuntimeToolService _runtimeTools;
    private readonly ChdmanPathResolver _chdmanPathResolver;
    private readonly IExternalLinkService _externalLinkService;
    private readonly PostConversionArtifactService _postConversionArtifacts;
    private readonly ILicenseService _licenseService;
    private readonly IFeatureAccessService _featureAccessService;
    private readonly OrphanedWorkItemScanner _orphanedScanner;
    private readonly OrphanedWorkItemCleanupService _orphanedCleanup;
    private readonly IWindowActivationService _windowActivationService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IResourceTextProvider _resourceTextProvider;
    private readonly AppSettings _settings;
    private readonly QueueContextMenuViewModel _queueContextMenuViewModel;
    private readonly TextBlock _sidebarWorkflowAfterSuccessText = new()
    {
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false
    };
    private readonly HashSet<string> _loggedExecutionSignatures = new(StringComparer.Ordinal);
    private readonly Queue<string> _executionLogLines = new();
    private readonly TaskbarSessionProgressViewModel _taskbarSessionProgress;
    private readonly QueueUiAggregateTracker _queueUiAggregates = new();
    private readonly CancellationTokenSource _windowLifetimeCts = new();
    private readonly MainWindowStartupCoordinator _startupCoordinator;

    private IAppSessionCoordinator _coordinator = null!;
    private IAppSessionUiPort _uiPort = null!;
    private bool _queueViewportUpdateQueued;
    private DataGridRowsPresenter? _queueRowsPresenterCache;
    private int _blockingBackgroundOps;
    private int _pendingUiStateRefresh;
    private int _pendingQueueUiFlush;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;
    private Task? _startupUpdateCheckTask;

    private const int MaxExecutionLogLines = 400;
    private const int MaxExecutionSignatures = 2048;

    private bool IsQueueInteractionLocked =>
        (_coordinator?.IsProcessing ?? false) || Volatile.Read(ref _blockingBackgroundOps) > 0;

    private DataGrid TasksDataGrid => QueueWorkspace.TaskGrid;
    private Rectangle DropZoneDashedFrame => QueueWorkspace.DropZoneFrame;
    private Border DropHighlightOverlay => QueueWorkspace.DropHighlight;
    private Border EmptyQueueDropHint => QueueWorkspace.EmptyDropHint;
    private Border MainFooterStatusStrip => FooterStatusStrip.RootBorder;
    private TextBlock FooterQueueSummaryText => FooterStatusStrip.QueueSummaryTextBlock;
    private TextBlock FooterWaitingCountText => FooterStatusStrip.WaitingCountTextBlock;
    private TextBlock FooterActiveCountText => FooterStatusStrip.ActiveCountTextBlock;
    private TextBlock FooterCompletedCountText => FooterStatusStrip.CompletedCountTextBlock;
    private TextBlock FooterFailedCountText => FooterStatusStrip.FailedCountTextBlock;
    private TextBlock FooterSkippedCountText => FooterStatusStrip.SkippedCountTextBlock;
    private Grid FooterProgressStrip => FooterStatusStrip.ProgressStrip;
    private ProgressBar QueueProgressBar => FooterStatusStrip.SessionProgressBar;
    private TextBlock QueueProgressText => FooterStatusStrip.ProgressTextBlock;
    private TextBlock FooterSessionPhaseText => FooterStatusStrip.SessionPhaseTextBlock;
    private TextBlock StatusBarSettingsText => FooterStatusStrip.SettingsTextBlock;
    private TextBlock SidebarWorkflowAfterSuccessText => _sidebarWorkflowAfterSuccessText;
    private TaskbarIcon TrayNotifyIcon => TrayNotifyIconHost.NotifyIcon;

    internal MainWindow(MainWindowBootstrap bootstrap, QueueManager queueManager)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(queueManager);

        InitializeComponent();

        _settingsService = bootstrap.SettingsService;
        _settings = bootstrap.Settings;
        _appMetadata = bootstrap.AppMetadata;
        _runtimeTools = bootstrap.RuntimeTools;
        _chdmanPathResolver = bootstrap.ChdmanPathResolver;
        _externalLinkService = bootstrap.ExternalLinkService;
        _postConversionArtifacts = bootstrap.PostConversionArtifacts;
        _licenseService = bootstrap.LicenseService;
        _featureAccessService = bootstrap.FeatureAccessService;
        _orphanedScanner = bootstrap.OrphanedScanner;
        _orphanedCleanup = bootstrap.OrphanedCleanup;
        _windowActivationService = bootstrap.WindowActivationService;
        _uiDispatcher = new WpfUiDispatcher(Dispatcher);
        _resourceTextProvider = new WpfResourceTextProvider();

        _featureAccessService.ApplyFreeFeatureRestrictions(_settings);

        _startupCoordinator = new MainWindowStartupCoordinator(
            this,
            _uiDispatcher,
            _resourceTextProvider,
            _runtimeTools,
            _settings,
            _settingsService,
            bootstrap.OrphanedScanner,
            bootstrap.OrphanedCleanup,
            SetFooterStatus,
            ApplySettingsToUi,
            UpdateUiState,
            UpdateHeaderModeText);

        _viewport = new QueueViewportService(
            _queueRowStore,
            vmFactory: row => new TaskQueueItemViewModel(row),
            applyRowToVm: (vm, row) =>
            {
                if (_shutdownStarted ||
                    _shutdownCompleted ||
                    Dispatcher.HasShutdownStarted ||
                    Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                if (Dispatcher.CheckAccess())
                {
                    vm.ApplyRowMutation(row);
                    return;
                }

                try
                {
                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        if (_shutdownStarted ||
                            _shutdownCompleted ||
                            Dispatcher.HasShutdownStarted ||
                            Dispatcher.HasShutdownFinished)
                        {
                            return;
                        }

                        vm.ApplyRowMutation(row);
                    });
                }
                catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                }
                catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                }
            });

        _queueView = new VirtualizedQueueCollection(
            _queueRowStore,
            _viewport,
            static row => row.IsVisibleInCurrentOperationMode);

        _viewport.SetVisibleIndexResolver(_queueView.IndexOfRowId);

        _queueRowStore.CollectionChanged += OnQueueRowStoreCollectionChanged;
        _queueRowStore.RowMutated += OnQueueRowStoreRowMutated;
        _viewport.VmMaterialized += OnVmMaterialized;
        _viewport.VmReleased += OnVmReleased;

        _queue = queueManager;
        _queue.ConfigurePresentationBindings(
            id => _queueRowStore.GetById(id)?.ToSnapshot(),
            id => _sinkIndex.TryGetValue(id, out TaskQueueStateAdapter? sink) ? sink : null,
            RequestUiStateRefresh);

        _queue.ItemUpdated += OnQueueItemUpdated;
        _queueController = new QueueController(_queue);
        _uiPort = new UiPortAdapter(this);
        _coordinator = new AppSessionCoordinator(_queueController, _uiPort);

        _viewModel = new MainWindowViewModel(
            new SessionAdapter(this),
            _coordinator,
            _queueView);

        _viewModel.IsRedumpFeatureVisible =
            _settings.EnableDeepIntegrityCheck
            && _featureAccessService.CanUseFeature(PremiumFeature.RedumpDeepIntegrity);
        DataContext = _viewModel;

        _taskbarSessionProgress = new TaskbarSessionProgressViewModel();
        BindTaskbarProgress();

        _queueContextMenuViewModel = new QueueContextMenuViewModel(
            () => IsQueueInteractionLocked,
            item => RunProcessSelectedInternalAsync(item),
            item => RunVerifySelectedChdInternalAsync(item),
            item => RunIntegrityContextAsync(item),
            item => OpenFolderForQueueItem(item),
            item => item is not null && CanQueueItemRunPipelineForSelectedMode(item),
            CanProcessIsoCueGdiPredicate,
            CanProcessArchivePredicate,
            CanProcessChdExtractPredicate,
            item => item is not null && CanQueueItemVerifyChd(item),
            item => item is not null && CanQueueItemIntegrityCheck(item),
            item => item is not null && TryGetQueueItemExplorerTarget(item, out _));

        _viewModel.AttachQueueContextCommands(_queueContextMenuViewModel);

        Loaded += MainWindow_Loaded;
        ThemeService.Instance.ThemeChanged += ThemeService_ThemeChanged;

        _viewModel.RefreshFeatureAccessDisplay();
        SyncThemeSelectorFromService();
        UpdateUiState();
        UpdateHeaderModeText();
    }

    private void BindTaskbarProgress()
    {
        var taskbarItemInfo = new TaskbarItemInfo();

        BindingOperations.SetBinding(
            taskbarItemInfo,
            TaskbarItemInfo.ProgressStateProperty,
            new Binding(nameof(TaskbarSessionProgressViewModel.State))
            {
                Source = _taskbarSessionProgress,
                Mode = BindingMode.OneWay
            });

        BindingOperations.SetBinding(
            taskbarItemInfo,
            TaskbarItemInfo.ProgressValueProperty,
            new Binding(nameof(TaskbarSessionProgressViewModel.NormalizedProgress))
            {
                Source = _taskbarSessionProgress,
                Mode = BindingMode.OneWay
            });

        TaskbarItemInfo = taskbarItemInfo;
    }
}
