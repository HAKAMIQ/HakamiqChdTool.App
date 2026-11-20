using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Recompress;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.ViewModels;

/// <summary>Recompresses one file; the production binding is <see cref="ChdRecompressService.RecompressAsync"/>.</summary>
public delegate Task<RecompressResult> RecompressRunner(
    string sourcePath,
    RecompressOptions options,
    IProgress<int> progress,
    CancellationToken cancellationToken);

/// <summary>File and folder pickers, so the view model stays free of dialogs.</summary>
public interface IRecompressPicker
{
    IReadOnlyList<string> PickChdFiles();

    string? PickFolder(string titleKey);
}

public sealed record RecompressPresetOption(string Key, string Label);

public sealed partial class RecompressItem : ObservableObject
{
    public RecompressItem(string path, long originalBytes, string waitingText)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        OriginalBytes = originalBytes;
        _statusText = waitingText;
        _sizeText = RunSummaryCardBuilder.FormatBytes(originalBytes);
    }

    public string Path { get; }

    public string FileName { get; }

    public long OriginalBytes { get; }

    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private string _sizeText;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    private RecompressOutcome? _outcome;

    public long NewBytes { get; set; }
}

/// <summary>
/// The Recompress CHD window: a list of CHD files, a compression choice, and either a target folder or
/// "replace the originals". Files run one at a time; Stop cancels the current file and leaves the rest waiting.
/// </summary>
public sealed partial class RecompressViewModel : ObservableObject
{
    public const int MaxItems = 10_000;

    private readonly RecompressRunner _runner;
    private readonly IRecompressPicker _picker;
    private readonly Func<string, string> _text;
    private CancellationTokenSource? _cts;

    public RecompressViewModel(RecompressRunner runner, IRecompressPicker picker, Func<string, string> text, string? currentCompressionSetting)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        PresetOptions =
        [
            new("preset:max", text("LocRecompress_PresetMax")),
            new("preset:balanced", text("LocRecompress_PresetBalanced")),
            new("preset:fast", text("LocRecompress_PresetFast")),
            new("preset:default", text("LocRecompress_PresetDefault"))
        ];
        _selectedPreset = PresetOptions.FirstOrDefault(option => string.Equals(option.Key, currentCompressionSetting, StringComparison.OrdinalIgnoreCase))
            ?? PresetOptions[0];
        _summaryText = string.Empty;
    }

    public ObservableCollection<RecompressItem> Items { get; } = [];

    public IReadOnlyList<RecompressPresetOption> PresetOptions { get; }

    [ObservableProperty]
    private RecompressPresetOption _selectedPreset;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyPropertyChangedFor(nameof(IsOutputFolderEnabled))]
    private bool _replaceOriginal;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(AddFilesCommand), nameof(AddFolderCommand), nameof(ClearCommand), nameof(BrowseOutputCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditOptions), nameof(IsOutputFolderEnabled))]
    private bool _isRunning;

    public bool CanEditOptions => !IsRunning;

    public bool IsOutputFolderEnabled => !IsRunning && !ReplaceOriginal;

    [ObservableProperty]
    private string _summaryText;

    public void AddPaths(IEnumerable<string> paths)
    {
        var known = new HashSet<string>(Items.Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (Items.Count >= MaxItems)
            {
                break;
            }

            string full;
            try
            {
                full = System.IO.Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!string.Equals(System.IO.Path.GetExtension(full), ".chd", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(full)
                || !known.Add(full))
            {
                continue;
            }

            long size;
            try
            {
                size = new FileInfo(full).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            Items.Add(new RecompressItem(full, size, _text("LocRecompress_Waiting")));
        }

        StartCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    public static IEnumerable<string> EnumerateChdFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.chd", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        });

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddFiles() => AddPaths(_picker.PickChdFiles());

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddFolder()
    {
        string? folder = _picker.PickFolder("LocRecompress_PickSourceFolder");
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            AddPaths(EnumerateChdFiles(folder));
        }
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        Items.Clear();
        SummaryText = string.Empty;
        StartCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void BrowseOutput()
    {
        string? folder = _picker.PickFolder("LocRecompress_PickOutputFolder");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            OutputFolder = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsRunning = true;
        SummaryText = string.Empty;
        _cts = new CancellationTokenSource();
        var options = new RecompressOptions(SelectedPreset.Key, ReplaceOriginal, ReplaceOriginal ? null : OutputFolder.Trim());
        try
        {
            foreach (RecompressItem item in Items.Where(item => item.Outcome is null or RecompressOutcome.Cancelled or RecompressOutcome.Failed).ToList())
            {
                if (_cts.IsCancellationRequested)
                {
                    break;
                }

                item.IsWorking = true;
                item.Progress = 0;
                item.StatusText = _text("LocRecompress_Working");
                var progress = new Progress<int>(value => item.Progress = Math.Clamp(value, 0, 100));
                RecompressResult result;
                try
                {
                    result = await _runner(item.Path, options, progress, _cts.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    result = new RecompressResult(RecompressOutcome.Cancelled, item.Path, null, item.OriginalBytes, 0, ChdRecompressService.CancelledKey);
                }

                item.IsWorking = false;
                item.Outcome = result.Outcome;
                item.StatusText = _text(result.MessageKey);
                if (result.Outcome is RecompressOutcome.Saved or RecompressOutcome.Replaced)
                {
                    item.NewBytes = result.NewBytes;
                    item.SizeText = $"{RunSummaryCardBuilder.FormatBytes(result.OriginalBytes)} → {RunSummaryCardBuilder.FormatBytes(result.NewBytes)}";
                }
            }
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
            SummaryText = BuildSummary();
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => _cts?.Cancel();

    /// <summary>"{0} of {1} files are smaller · {2} saved", with the failed count when there are failures.</summary>
    public string BuildSummary()
    {
        RecompressItem[] smaller = [.. Items.Where(item => item.Outcome is RecompressOutcome.Saved or RecompressOutcome.Replaced)];
        long saved = smaller.Sum(item => Math.Max(0, item.OriginalBytes - item.NewBytes));
        int failed = Items.Count(item => item.Outcome == RecompressOutcome.Failed);
        string summary = _text("LocRecompress_Summary")
            .Replace("{0}", "‎" + smaller.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "‎", StringComparison.Ordinal)
            .Replace("{1}", "‎" + Items.Count(item => item.Outcome is not null).ToString(System.Globalization.CultureInfo.InvariantCulture) + "‎", StringComparison.Ordinal)
            .Replace("{2}", "‎" + RunSummaryCardBuilder.FormatBytes(saved) + "‎", StringComparison.Ordinal);
        return failed > 0
            ? summary + " · " + _text("LocRecompress_SummaryFailed").Replace("{0}", "‎" + failed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "‎", StringComparison.Ordinal)
            : summary;
    }

    private bool CanEdit() => !IsRunning;

    public void CancelRun() => _cts?.Cancel();

    private bool CanClear() => !IsRunning && Items.Count > 0;

    private bool CanStart() =>
        !IsRunning
        && Items.Count > 0
        && (ReplaceOriginal || !string.IsNullOrWhiteSpace(OutputFolder));
}
