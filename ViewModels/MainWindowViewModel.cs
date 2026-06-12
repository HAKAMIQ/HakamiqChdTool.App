using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.QueueRun;
using HakamiqChdTool.App.Localization;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.Versioning;

namespace HakamiqChdTool.App.ViewModels;

[SupportedOSPlatform("windows10.0.17763.0")]
public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IMainWindowSession _session;
    private readonly IQueueRunCoordinator _coordinator;
    private readonly INotifyCollectionChanged? _observableQueueItems;
    private bool _disposed;
    private bool _isRedumpFeatureVisible;

    [ObservableProperty]
    private TaskQueueItemViewModel? selectedTask;

    [ObservableProperty]
    private bool canStartProcessing = true;

    [ObservableProperty]
    private bool isShellEnabled = true;

    [ObservableProperty]
    private bool isQueueActivityCardVisible;

    [ObservableProperty]
    private bool isQueueActivityBusy;

    [ObservableProperty]
    private bool isQueueActivityCancelable;

    [ObservableProperty]
    private bool isAddingFiles;

    [ObservableProperty]
    private string queueActivityCardTitle = string.Empty;

    [ObservableProperty]
    private string queueActivityCardMessage = string.Empty;


    public MainWindowViewModel(
        IMainWindowSession session,
        IQueueRunCoordinator coordinator,
        IList queueItems)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        QueueItems = queueItems ?? throw new ArgumentNullException(nameof(queueItems));

        _observableQueueItems = queueItems as INotifyCollectionChanged;
        if (_observableQueueItems is not null)
        {
            _observableQueueItems.CollectionChanged += OnQueueItemsCollectionChanged;
        }

        _coordinator.RunStateChanged += OnQueueRunStateChanged;

        OpenOutputFolderCommand = new RelayCommand(
            () => _session.OpenExplorerForSelectedQueueItem(SelectedTask),
            () => _session.CanOpenExplorerTarget(SelectedTask));

        OpenOperationLogCommand = new AsyncRelayCommand<TaskQueueItemViewModel?>(
            item => _session.OpenOperationLogForQueueItemAsync(item ?? SelectedTask),
            item => _session.CanOpenOperationLogTarget(item ?? SelectedTask));

        StartProcessingCommand = new AsyncRelayCommand(
            () => _coordinator.StartProcessingAsync(),
            () => CanStartProcessing && !_session.IsQueueInteractionLocked);

        ProcessSelectedToolbarCommand = new AsyncRelayCommand(
            () => _coordinator.ProcessSelectedAsync(SelectedTask),
            () => SelectedTask is not null && !_session.IsQueueInteractionLocked);

        VerifySelectedToolbarCommand = new AsyncRelayCommand(
            () => _coordinator.VerifySelectedChdAsync(SelectedTask),
            () => SelectedTask is { IsDirectChd: true } && !_session.IsQueueInteractionLocked);

        VerifySelectedRedumpToolbarCommand = new AsyncRelayCommand(
            () => _session.RunRedumpIntegrityForSelectedQueueItemAsync(SelectedTask),
            () => IsRedumpFeatureVisible && _session.CanRunRedumpIntegrityForSelectedQueueItem(SelectedTask));

        VerifyAllRedumpToolbarCommand = new AsyncRelayCommand(
            () => _session.RunRedumpIntegrityForAllQueueItemsAsync(),
            () => IsRedumpFeatureVisible && _session.CanRunRedumpIntegrityForAnyQueueItem());

        VerifyRowRedumpCommand = new AsyncRelayCommand<TaskQueueItemViewModel?>(
            item => _session.RunRedumpIntegrityForSelectedQueueItemAsync(item ?? SelectedTask),
            item => IsRedumpFeatureVisible && _session.CanRunRedumpIntegrityForSelectedQueueItem(item ?? SelectedTask));

        ShowRedumpDetailsCommand = new AsyncRelayCommand<TaskQueueItemViewModel?>(
            item => _session.ShowRedumpDetails(item ?? SelectedTask),
            item => IsRedumpFeatureVisible && (item ?? SelectedTask) is not null);

        ApplyRedumpSuggestedNameCommand = new AsyncRelayCommand<TaskQueueItemViewModel?>(
            item => _session.ApplyRedumpSuggestedNameAsync(item ?? SelectedTask),
            item => IsRedumpFeatureVisible && _session.CanApplyRedumpSuggestedName(item ?? SelectedTask));

        RemoveSelectedCommand = new RelayCommand(
            () =>
            {
                if (SelectedTask is not null)
                {
                    _session.RemoveQueueItem(SelectedTask);
                }
            },
            () => SelectedTask is not null && !_session.IsQueueInteractionLocked);

        CancelProcessingCommand = new RelayCommand(
            () => _coordinator.RequestCancel(),
            () => _coordinator.IsProcessing && !_coordinator.CancellationRequested);

        SelectFolderCommand = new AsyncRelayCommand(
            () => _coordinator.SelectFolderAsync(),
            () => CanAcceptQueueInput());

        QuickConvertCommand = new AsyncRelayCommand(
            () => _coordinator.QuickConvertAsync(),
            () => CanAcceptQueueInput());

        QuickExtractCommand = new AsyncRelayCommand(
            () => _coordinator.QuickExtractAsync(),
            () => CanAcceptQueueInput());

        ScanFolderQuickConvertCommand = new AsyncRelayCommand(
            () => _coordinator.ScanFolderQuickConvertAsync(),
            () => CanAcceptQueueInput());

        ScanFolderQuickExtractCommand = new AsyncRelayCommand(
            () => _coordinator.ScanFolderQuickExtractAsync(),
            () => CanAcceptQueueInput());

        OpenOptionsCommand = new RelayCommand(
            () => _session.OpenOptions(),
            () => !_session.IsQueueInteractionLocked);

        OpenAboutCommand = new RelayCommand(
            () => _session.OpenAbout(),
            () => !_session.IsQueueInteractionLocked);


        RetryQueueItemCommand = new RelayCommand<TaskQueueItemViewModel?>(
            item => _session.RetryQueueItem(item),
            item => item is not null && !_session.IsQueueInteractionLocked);

        RemoveQueueItemCommand = new RelayCommand<TaskQueueItemViewModel?>(
            item => _session.RemoveQueueItem(item),
            item => item is not null && !_session.IsQueueInteractionLocked);

        CancelQueueJobCommand = new RelayCommand<TaskQueueItemViewModel?>(
            item => _session.CancelQueueJob(item),
            item => item is not null
                && item.HasActiveQueueBinding
                && !TaskQueueStateCodes.IsTerminal(item.CurrentState));
    }

    public bool IsRedumpFeatureVisible
    {
        get => _isRedumpFeatureVisible;
        set
        {
            if (SetProperty(ref _isRedumpFeatureVisible, value))
            {
                NotifyQueueCommandsCanExecuteChanged();
            }
        }
    }

    public IList QueueItems { get; }

    public IRelayCommand OpenOutputFolderCommand { get; }
    public IAsyncRelayCommand<TaskQueueItemViewModel?> OpenOperationLogCommand { get; }
    public IAsyncRelayCommand StartProcessingCommand { get; }
    public IAsyncRelayCommand ProcessSelectedToolbarCommand { get; }
    public IAsyncRelayCommand VerifySelectedRedumpToolbarCommand { get; }
    public IAsyncRelayCommand VerifyAllRedumpToolbarCommand { get; }
    public IAsyncRelayCommand<TaskQueueItemViewModel?> VerifyRowRedumpCommand { get; }
    public IAsyncRelayCommand<TaskQueueItemViewModel?> ShowRedumpDetailsCommand { get; }
    public IAsyncRelayCommand<TaskQueueItemViewModel?> ApplyRedumpSuggestedNameCommand { get; }
    public IAsyncRelayCommand VerifySelectedToolbarCommand { get; }
    public IRelayCommand RemoveSelectedCommand { get; }
    public IRelayCommand CancelProcessingCommand { get; }
    public IAsyncRelayCommand SelectFolderCommand { get; }
    public IAsyncRelayCommand QuickConvertCommand { get; }
    public IAsyncRelayCommand QuickExtractCommand { get; }
    public IAsyncRelayCommand ScanFolderQuickConvertCommand { get; }
    public IAsyncRelayCommand ScanFolderQuickExtractCommand { get; }
    public IRelayCommand OpenOptionsCommand { get; }
    public IRelayCommand OpenAboutCommand { get; }
    public IRelayCommand<TaskQueueItemViewModel?> RetryQueueItemCommand { get; }
    public IRelayCommand<TaskQueueItemViewModel?> RemoveQueueItemCommand { get; }
    public IRelayCommand<TaskQueueItemViewModel?> CancelQueueJobCommand { get; }

    public IAsyncRelayCommand<TaskQueueItemViewModel?> ProcessIsoCueGdiCommand { get; private set; } = null!;
    public IAsyncRelayCommand<TaskQueueItemViewModel?> ProcessArchiveCommand { get; private set; } = null!;
    public IAsyncRelayCommand<TaskQueueItemViewModel?> ProcessChdExtractCommand { get; private set; } = null!;
    public IAsyncRelayCommand<TaskQueueItemViewModel?> ProcessSelectedCommand { get; private set; } = null!;
    public IAsyncRelayCommand<TaskQueueItemViewModel?> VerifyChdCommand { get; private set; } = null!;
    public IAsyncRelayCommand<TaskQueueItemViewModel?> IntegrityCheckCommand { get; private set; } = null!;
    public IRelayCommand<TaskQueueItemViewModel?> OpenFolderCommand { get; private set; } = null!;

    public void NotifyQueueCommandsCanExecuteChanged()
    {
        if (_disposed)
        {
            return;
        }

        OpenOutputFolderCommand.NotifyCanExecuteChanged();
        OpenOperationLogCommand.NotifyCanExecuteChanged();
        StartProcessingCommand.NotifyCanExecuteChanged();
        ProcessSelectedToolbarCommand.NotifyCanExecuteChanged();
        VerifySelectedRedumpToolbarCommand.NotifyCanExecuteChanged();
        VerifyAllRedumpToolbarCommand.NotifyCanExecuteChanged();
        VerifyRowRedumpCommand.NotifyCanExecuteChanged();
        ShowRedumpDetailsCommand.NotifyCanExecuteChanged();
        ApplyRedumpSuggestedNameCommand.NotifyCanExecuteChanged();
        VerifySelectedToolbarCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        CancelProcessingCommand.NotifyCanExecuteChanged();
        SelectFolderCommand.NotifyCanExecuteChanged();
        QuickConvertCommand.NotifyCanExecuteChanged();
        QuickExtractCommand.NotifyCanExecuteChanged();
        ScanFolderQuickConvertCommand.NotifyCanExecuteChanged();
        ScanFolderQuickExtractCommand.NotifyCanExecuteChanged();
        RetryQueueItemCommand.NotifyCanExecuteChanged();
        RemoveQueueItemCommand.NotifyCanExecuteChanged();
        CancelQueueJobCommand.NotifyCanExecuteChanged();
        OpenOptionsCommand.NotifyCanExecuteChanged();
        OpenAboutCommand.NotifyCanExecuteChanged();
        SelectFilesCommand.NotifyCanExecuteChanged();
        CancelAddingFilesCommand.NotifyCanExecuteChanged();
        ClearQueueCommand.NotifyCanExecuteChanged();
    }


    internal void AttachQueueContextCommands(QueueContextMenuViewModel queueContext)
    {
        ArgumentNullException.ThrowIfNull(queueContext);

        ProcessIsoCueGdiCommand = queueContext.ProcessIsoCueGdiCommand;
        ProcessArchiveCommand = queueContext.ProcessArchiveCommand;
        ProcessChdExtractCommand = queueContext.ProcessChdExtractCommand;
        ProcessSelectedCommand = queueContext.ProcessSelectedCommand;
        VerifyChdCommand = queueContext.VerifyChdCommand;
        IntegrityCheckCommand = queueContext.IntegrityCheckCommand;
        OpenFolderCommand = queueContext.OpenFolderCommand;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_observableQueueItems is not null)
        {
            _observableQueueItems.CollectionChanged -= OnQueueItemsCollectionChanged;
        }

        _coordinator.RunStateChanged -= OnQueueRunStateChanged;
        _intakeCancellationCts?.Cancel();
        _intakeCancellationCts?.Dispose();
        _intakeCancellationCts = null;
        GC.SuppressFinalize(this);
    }

    private void OnQueueRunStateChanged()
    {
        NotifyQueueCommandsCanExecuteChanged();
    }

    private void OnQueueItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifyQueueCommandsCanExecuteChanged();
    }

    partial void OnSelectedTaskChanged(TaskQueueItemViewModel? value)
    {
        NotifyQueueCommandsCanExecuteChanged();
    }

    partial void OnCanStartProcessingChanged(bool value)
    {
        NotifyQueueCommandsCanExecuteChanged();
    }
}
