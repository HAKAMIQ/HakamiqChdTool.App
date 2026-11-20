using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.QueueRun;

namespace HakamiqChdTool.App.ViewModels;

/// <summary>State and commands of the quick window. The run itself is done by <see cref="QuickRunHost"/>.</summary>
internal sealed partial class QuickRunViewModel : ObservableObject
{
    private readonly QuickRunHost _host;
    private readonly Func<string, string> _text;
    private readonly bool _mayDeleteSource;

    public QuickRunViewModel(QuickRunHost host, QueueExecutionProfile profile, bool mayDeleteSource, Func<string, string> text)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _mayDeleteSource = mayDeleteSource;
        Profile = profile;

        StartCommand = new AsyncRelayCommand(() => _host.StartAsync(), () => IsWaitingForStart);
        CancelCommand = new RelayCommand(_host.Cancel, () => IsRunning);
        DetailsCommand = new RelayCommand(() => DetailsRequested?.Invoke(ResultTitle, _host.LastResult?.Report ?? string.Empty), () => HasResult);
        OpenInAppCommand = new RelayCommand(() => OpenInAppRequested?.Invoke(_host.UnfinishedSourcePaths()), () => !_host.IsBusy);

        _host.Changed += Refresh;
    }

    public QueueExecutionProfile Profile { get; }

    public ObservableCollection<TaskQueueItemViewModel> Items => _host.Items;

    public string Title => _text(Profile switch
    {
        QueueExecutionProfile.QuickExtract => "LocQuick_TitleExtract",
        QueueExecutionProfile.QuickVerify => "LocQuick_TitleVerify",
        _ => "LocQuick_TitleConvert"
    });

    public bool IsRunning => _host.IsProcessing;

    public bool IsBusy => _host.IsBusy;

    /// <summary>Files are listed and wait for Start, because the run may delete the source.</summary>
    public bool IsWaitingForStart => !_host.AutoStart && !_host.IsBusy && _host.HasWaitingFiles;

    public bool ShowDeleteWarning => _mayDeleteSource && !_host.AutoStart;

    public string DeleteWarningText => _text(Profile == QueueExecutionProfile.QuickExtract
        ? "LocQuick_DeleteWarningExtract"
        : "LocQuick_DeleteWarningConvert");

    public bool HasResult => _host.LastResult is not null && !_host.IsBusy;

    public string ResultTitle => _host.LastResult?.Card.Title ?? string.Empty;

    public string ResultMessage => _host.LastResult?.Card.Message ?? string.Empty;

    public string StatusText => IsWaitingForStart ? _text("LocQuick_WaitingForStart") : _host.StatusText;

    public IAsyncRelayCommand StartCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand DetailsCommand { get; }

    public IRelayCommand OpenInAppCommand { get; }

    /// <summary>Title and report text to show in a dialog.</summary>
    public event Action<string, string>? DetailsRequested;

    /// <summary>The files that did not finish, to open in the main window.</summary>
    public event Action<IReadOnlyList<string>>? OpenInAppRequested;

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsWaitingForStart));
        OnPropertyChanged(nameof(ShowDeleteWarning));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultMessage));
        OnPropertyChanged(nameof(StatusText));
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        DetailsCommand.NotifyCanExecuteChanged();
        OpenInAppCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Re-reads the texts after the language changes.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DeleteWarningText));
        Refresh();
    }

    public void Detach() => _host.Changed -= Refresh;
}
