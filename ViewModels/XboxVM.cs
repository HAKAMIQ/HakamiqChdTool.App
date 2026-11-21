using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Xbox;

namespace HakamiqChdTool.App.ViewModels;

internal delegate Task<XboxSourceKind> XboxDetector(string path, CancellationToken cancellationToken);

internal delegate Task<XboxConvertResult> XboxConverter(string path, string outputFolder, Xbox360Format format, CancellationToken cancellationToken);

internal interface IXboxPicker
{
    IReadOnlyList<string> PickImages();

    string? PickFolder(string titleKey);
}

internal sealed record XboxFormatOption(Xbox360Format Format, string Label);

internal sealed partial class XboxItem(string path, XboxSourceKind kind, string kindText) : ObservableObject
{
    public string Path { get; } = path;

    public string Name { get; } = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));

    public XboxSourceKind Kind { get; } = kind;

    public string KindText { get; } = kindText;

    public bool IsSupported => Kind != XboxSourceKind.Unsupported;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    private XboxConvertOutcome? _outcome;

    public long SourceBytes { get; set; }

    public long OutputBytes { get; set; }
}

/// <summary>
/// The Xbox window: Xbox images and game folders become XISO; Xbox 360 ones become Games on Demand or ZAR, as chosen.
/// Items run one at a time; Stop cancels the current item and leaves the rest waiting.
/// </summary>
internal sealed partial class XboxViewModel : ObservableObject
{
    public const int MaxItems = 2_000;

    private readonly XboxDetector _detect;
    private readonly XboxConverter _convert;
    private readonly IXboxPicker _picker;
    private readonly Func<string, string> _text;
    private readonly Action<Xbox360Format> _saveFormat;
    private CancellationTokenSource? _cts;

    public XboxViewModel(
        XboxDetector detect,
        XboxConverter convert,
        IXboxPicker picker,
        Func<string, string> text,
        Xbox360Format savedFormat,
        Action<Xbox360Format> saveFormat)
    {
        _detect = detect ?? throw new ArgumentNullException(nameof(detect));
        _convert = convert ?? throw new ArgumentNullException(nameof(convert));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _saveFormat = saveFormat ?? throw new ArgumentNullException(nameof(saveFormat));
        FormatOptions =
        [
            new(Xbox360Format.God, text("LocXbox_FormatGod")),
            new(Xbox360Format.Zar, text("LocXbox_FormatZar"))
        ];
        _selectedFormat = FormatOptions.First(option => option.Format == savedFormat);
    }

    public ObservableCollection<XboxItem> Items { get; } = [];

    public IReadOnlyList<XboxFormatOption> FormatOptions { get; }

    [ObservableProperty]
    private XboxFormatOption _selectedFormat;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(AddFilesCommand), nameof(AddFolderCommand), nameof(ClearCommand), nameof(BrowseOutputCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditOptions))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(AddFilesCommand), nameof(AddFolderCommand), nameof(ClearCommand))]
    private bool _isAdding;

    public bool CanEditOptions => !IsRunning;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    partial void OnSelectedFormatChanged(XboxFormatOption value) => _saveFormat(value.Format);

    /// <summary>
    /// Adds images and folders. A folder that is a game (default.xbe or default.xex) is added as one game; any other
    /// folder adds the games and images directly inside it.
    /// </summary>
    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        IsAdding = true;
        try
        {
            var known = new HashSet<string>(Items.Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in ExpandFolders(paths))
            {
                if (Items.Count >= MaxItems)
                {
                    break;
                }

                string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
                if (!known.Add(path))
                {
                    continue;
                }

                XboxSourceKind kind = await _detect(path, CancellationToken.None).ConfigureAwait(true);
                var item = new XboxItem(path, kind, _text(KindKey(kind)));
                item.StatusText = _text(kind == XboxSourceKind.Unsupported ? XboxConvertService.NotXboxKey : "LocXbox_Waiting");
                Items.Add(item);
            }
        }
        finally
        {
            IsAdding = false;
            StartCommand.NotifyCanExecuteChanged();
            ClearCommand.NotifyCanExecuteChanged();
        }
    }

    internal static IEnumerable<string> ExpandFolders(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (!Directory.Exists(path) || IsGameFolder(path))
            {
                yield return path;
                continue;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(path).Where(IsGameFolder)
                    .Concat(Directory.EnumerateFiles(path).Where(file =>
                        Path.GetExtension(file) is { } extension
                        && (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xiso", StringComparison.OrdinalIgnoreCase))))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                children = [];
            }

            foreach (string child in children)
            {
                yield return child;
            }
        }
    }

    private static bool IsGameFolder(string folder) =>
        File.Exists(Path.Combine(folder, "default.xbe")) || File.Exists(Path.Combine(folder, "default.xex"));

    private static string KindKey(XboxSourceKind kind) => kind switch
    {
        XboxSourceKind.XboxIso => "LocXbox_KindXboxIso",
        XboxSourceKind.XboxFolder => "LocXbox_KindXboxFolder",
        XboxSourceKind.Xbox360Iso => "LocXbox_Kind360Iso",
        XboxSourceKind.Xbox360Folder => "LocXbox_Kind360Folder",
        _ => "LocXbox_KindUnsupported"
    };

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task AddFilesAsync() => AddPathsAsync(_picker.PickImages());

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task AddFolderAsync()
    {
        string? folder = _picker.PickFolder("LocXbox_PickGameFolder");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            await AddPathsAsync([folder]).ConfigureAwait(true);
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

    [RelayCommand(CanExecute = nameof(CanEditOutput))]
    private void BrowseOutput()
    {
        string? folder = _picker.PickFolder("LocXbox_PickOutputFolder");
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
        Xbox360Format format = SelectedFormat.Format;
        string output = OutputFolder.Trim();
        try
        {
            foreach (XboxItem item in Items.Where(item => item.IsSupported && item.Outcome is null or XboxConvertOutcome.Failed or XboxConvertOutcome.Cancelled).ToList())
            {
                if (_cts.IsCancellationRequested)
                {
                    break;
                }

                item.IsWorking = true;
                item.StatusText = _text("LocXbox_Working");
                XboxConvertResult result;
                try
                {
                    result = await _convert(item.Path, output, format, _cts.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    result = new XboxConvertResult(XboxConvertOutcome.Cancelled, XboxConvertService.CancelledKey);
                }

                item.IsWorking = false;
                item.Outcome = result.Outcome;
                item.SourceBytes = result.SourceBytes;
                item.OutputBytes = result.OutputBytes;
                item.StatusText = result.Outcome == XboxConvertOutcome.Done
                    ? _text(result.MessageKey) + " · " + Ltr(RunSummaryCardBuilder.FormatBytes(result.SourceBytes) + " → " + RunSummaryCardBuilder.FormatBytes(result.OutputBytes))
                    : _text(result.MessageKey);
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

    public void CancelRun() => _cts?.Cancel();

    /// <summary>"{0} done · {1} skipped · {2} failed · {3} saved", numbers kept left to right in Arabic.</summary>
    public string BuildSummary()
    {
        XboxItem[] done = [.. Items.Where(item => item.Outcome == XboxConvertOutcome.Done)];
        long saved = done.Sum(item => Math.Max(0, item.SourceBytes - item.OutputBytes));
        return _text("LocXbox_Summary")
            .Replace("{0}", Ltr(done.Length.ToString(CultureInfo.InvariantCulture)), StringComparison.Ordinal)
            .Replace("{1}", Ltr(Items.Count(item => item.Outcome == XboxConvertOutcome.Skipped).ToString(CultureInfo.InvariantCulture)), StringComparison.Ordinal)
            .Replace("{2}", Ltr(Items.Count(item => item.Outcome == XboxConvertOutcome.Failed).ToString(CultureInfo.InvariantCulture)), StringComparison.Ordinal)
            .Replace("{3}", Ltr(RunSummaryCardBuilder.FormatBytes(saved)), StringComparison.Ordinal);
    }

    // WPF orders a number next to a unit or a Latin word correctly in Arabic text only when every word carries its own
    // left-to-right mark; one mark around the whole value is not enough.
    internal static string Ltr(string value) =>
        string.Join(" ", value.Split(' ').Select(word => "‎" + word + "‎"));

    private bool CanEdit() => !IsRunning && !IsAdding;

    private bool CanEditOutput() => !IsRunning;

    private bool CanClear() => !IsRunning && !IsAdding && Items.Count > 0;

    private bool CanStart() =>
        !IsRunning
        && !IsAdding
        && !string.IsNullOrWhiteSpace(OutputFolder)
        && Items.Any(item => item.IsSupported && item.Outcome is null or XboxConvertOutcome.Failed or XboxConvertOutcome.Cancelled);
}
