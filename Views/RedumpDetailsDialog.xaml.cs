using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.ViewModels;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HakamiqChdTool.App.Views;

public sealed partial class RedumpDetailsDialog : Window
{
    private const string UnknownKey = "LocRedumpDetails_Value_Unknown";
    private const string UnavailableKey = "LocRedumpDetails_Value_Unavailable";
    private const string NoSuggestedNameKey = "LocRedumpDetails_NoSuggestedName";
    private const string NoUsefulHashesKey = "LocRedumpDetails_NoUsefulHashes";
    private const string UnnamedFileKey = "LocRedumpDetails_UnnamedFile";
    private const string DefaultNotesNoneKey = "LocRedumpDetails_DefaultNotesNone";
    private const string DefaultNotesNoDatabaseKey = "LocRedumpDetails_DefaultNotesNoDatabase";
    private const string StatusNoteMatchedKey = "LocRedumpDetails_StatusNoteMatched";
    private const string StatusNotePartialKey = "LocRedumpDetails_StatusNotePartial";
    private const string StatusNoteNoDatabaseKey = "LocRedumpDetails_StatusNoteNoDatabase";
    private const string StatusNoteConflictedKey = "LocRedumpDetails_StatusNoteConflicted";
    private const string StatusNoteModifiedOrCorruptKey = "LocRedumpDetails_StatusNoteModifiedOrCorrupt";
    private const string StatusNoteGenericKey = "LocRedumpDetails_StatusNoteGeneric";
    private const string ByteUnitKey = "LocCommon_ByteUnit";
    private const string CopyNameUnavailableKey = "LocRedumpDetails_CopyNameUnavailable";
    private const string CopyNameSuccessKey = "LocRedumpDetails_CopyNameSuccess";
    private const string CopyNameFailedKey = "LocRedumpDetails_CopyNameFailed";
    private const string CopyHashesUnavailableKey = "LocRedumpDetails_CopyHashesUnavailable";
    private const string CopyHashesSuccessKey = "LocRedumpDetails_CopyHashesSuccess";
    private const string CopyHashesFailedKey = "LocRedumpDetails_CopyHashesFailed";

    private const string TechnicalUnavailable = "Unavailable";
    private const string TechnicalUnnamedFile = "Unnamed file";
    private const string TechnicalByteUnit = "bytes";

    private readonly DispatcherTimer _copyFeedbackTimer;
    private readonly Storyboard? _toastShowStoryboard;
    private readonly Storyboard? _toastHideStoryboard;

    public RedumpDetailsDialog(TaskQueueItemViewModel item, bool canApplyName)
    {
        ArgumentNullException.ThrowIfNull(item);

        InitializeComponent();

        _copyFeedbackTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2.5d)
        };
        _copyFeedbackTimer.Tick += CopyFeedbackTimer_Tick;

        _toastShowStoryboard = TryFindResource("RedumpDetails.ToastShowStoryboard") as Storyboard;
        _toastHideStoryboard = TryFindResource("RedumpDetails.ToastHideStoryboard") as Storyboard;

        if (_toastHideStoryboard is not null)
        {
            _toastHideStoryboard.Completed += ToastHideStoryboard_Completed;
        }

        Closed += RedumpDetailsDialog_Closed;

        AppLanguageService.ApplyToWindow(this);
        DataContext = new RedumpDetailsDialogModel(item, canApplyName);
    }

    public bool ApplyNameRequested { get; private set; }

    private void ApplyNameButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyNameRequested = true;
        DialogResult = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void CopyNameButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RedumpDetailsDialogModel model || !model.HasCopyableSuggestedName)
        {
            ShowCopyFeedback(Text(CopyNameUnavailableKey), isError: true);
            return;
        }

        try
        {
            Clipboard.SetText(model.SuggestedNameRaw);
            ShowCopyFeedback(Text(CopyNameSuccessKey), isError: false);
        }
        catch (ExternalException)
        {
            ShowCopyFeedback(Text(CopyNameFailedKey), isError: true);
        }
        catch (InvalidOperationException)
        {
            ShowCopyFeedback(Text(CopyNameFailedKey), isError: true);
        }
    }

    private void CopyHashButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RedumpDetailsDialogModel model || !model.HasCopyableHashes)
        {
            ShowCopyFeedback(Text(CopyHashesUnavailableKey), isError: true);
            return;
        }

        try
        {
            Clipboard.SetText(model.HashesSummaryRaw);
            ShowCopyFeedback(Text(CopyHashesSuccessKey), isError: false);
        }
        catch (ExternalException)
        {
            ShowCopyFeedback(Text(CopyHashesFailedKey), isError: true);
        }
        catch (InvalidOperationException)
        {
            ShowCopyFeedback(Text(CopyHashesFailedKey), isError: true);
        }
    }

    private static string Text(string key)
    {
        return ArabicUi.Get(key);
    }

    private void ShowCopyFeedback(string message, bool isError)
    {
        _copyFeedbackTimer.Stop();
        _toastHideStoryboard?.Stop(this);
        _toastShowStoryboard?.Stop(this);

        CopyFeedbackText.Text = message;

        Brush successBrush = ResolveBrush("Brush.Success", ResolveBrush("Brush.Accent", Brushes.SeaGreen));
        Brush successBackground = ResolveBrush("Brush.Success.Subtle", ResolveBrush("Brush.Accent.Subtle", ResolveBrush("Brush.Layer.Card", Brushes.Transparent)));
        Brush successBorder = ResolveBrush("Brush.Success.Border", ResolveBrush("Brush.Accent.SelectedBorder", successBrush));

        Brush errorBrush = ResolveBrush("Brush.Error", ResolveBrush("Brush.Accent", Brushes.IndianRed));
        Brush errorBackground = ResolveBrush("Brush.Layer.Card", Brushes.Transparent);
        Brush errorBorder = ResolveBrush("Brush.Error", errorBrush);

        CopyFeedbackToast.Background = isError ? errorBackground : successBackground;
        CopyFeedbackToast.BorderBrush = isError ? errorBorder : successBorder;
        CopyFeedbackText.Foreground = isError ? errorBrush : successBrush;
        CopyFeedbackIcon.Stroke = isError ? errorBrush : successBrush;

        CopyFeedbackToast.Visibility = Visibility.Visible;
        CopyFeedbackToast.Opacity = 0d;
        CopyFeedbackToastTranslate.Y = -12d;

        if (_toastShowStoryboard is not null)
        {
            _toastShowStoryboard.Begin(this, true);
        }
        else
        {
            CopyFeedbackToast.Opacity = 1d;
            CopyFeedbackToastTranslate.Y = 0d;
        }

        _copyFeedbackTimer.Start();
    }

    private void CopyFeedbackTimer_Tick(object? sender, EventArgs e)
    {
        _copyFeedbackTimer.Stop();

        if (_toastHideStoryboard is not null)
        {
            _toastHideStoryboard.Begin(this, true);
            return;
        }

        CopyFeedbackToast.Visibility = Visibility.Collapsed;
        CopyFeedbackToast.Opacity = 0d;
        CopyFeedbackText.Text = string.Empty;
    }

    private void ToastHideStoryboard_Completed(object? sender, EventArgs e)
    {
        CopyFeedbackToast.Visibility = Visibility.Collapsed;
        CopyFeedbackText.Text = string.Empty;
    }

    private void RedumpDetailsDialog_Closed(object? sender, EventArgs e)
    {
        Closed -= RedumpDetailsDialog_Closed;

        _copyFeedbackTimer.Stop();
        _copyFeedbackTimer.Tick -= CopyFeedbackTimer_Tick;

        if (_toastHideStoryboard is not null)
        {
            _toastHideStoryboard.Completed -= ToastHideStoryboard_Completed;
        }
    }

    private Brush ResolveBrush(string resourceKey, Brush fallback)
    {
        return TryFindResource(resourceKey) as Brush ?? fallback;
    }

    private sealed class RedumpDetailsDialogModel
    {
        public RedumpDetailsDialogModel(TaskQueueItemViewModel item, bool canApplyName)
        {
            string rawFileName = CleanTechnicalName(item.FileName);

            FileNameRaw = rawFileName;
            FileName = BuildTechnicalDisplayText(rawFileName);

            Status = string.IsNullOrWhiteSpace(item.RedumpStatusDisplay) ? Text(UnknownKey) : item.RedumpStatusDisplay;

            SuggestedNameRaw = string.IsNullOrWhiteSpace(item.SuggestedStandardName)
                ? Text(NoSuggestedNameKey)
                : CleanTechnicalName(item.SuggestedStandardName);

            SuggestedName = IsLocalizedFallbackText(SuggestedNameRaw)
                ? SuggestedNameRaw
                : BuildTechnicalDisplayText(SuggestedNameRaw);

            CanApplyName = canApplyName;

            ParsedRedumpDetails parsed = Parse(item.RedumpDetailsDisplay, item.FileSizeDisplay, item.PlatformBadgeDisplay, item.FileName);

            DisplayTitle = string.IsNullOrWhiteSpace(parsed.GameName) ? item.FileName : parsed.GameName;

            string rawSystemName = string.IsNullOrWhiteSpace(parsed.SystemName)
                ? (string.IsNullOrWhiteSpace(item.PlatformBadgeDisplay) ? Text(UnknownKey) : item.PlatformBadgeDisplay)
                : parsed.SystemName;

            SystemName = NormalizeSystemDisplayName(rawSystemName);
            ManufacturerName = InferManufacturerName(rawSystemName);
            ConsoleTypeName = InferConsoleTypeName(rawSystemName, ManufacturerName);

            string rawSizeDisplay = string.IsNullOrWhiteSpace(parsed.TotalSizeDisplay)
                ? (string.IsNullOrWhiteSpace(item.FileSizeDisplay) ? TechnicalUnavailable : item.FileSizeDisplay)
                : parsed.TotalSizeDisplay;

            SizeDisplay = BuildTechnicalDisplayText(ToTechnicalSizeText(rawSizeDisplay));

            FilesSummaryRaw = string.IsNullOrWhiteSpace(parsed.FilesSummary)
                ? BuildSingleFileSummary(item.FileName, SizeDisplay)
                : parsed.FilesSummary;

            HashesSummaryRaw = string.IsNullOrWhiteSpace(parsed.HashesSummary)
                ? Text(NoUsefulHashesKey)
                : parsed.HashesSummary;

            FilesSummary = BuildTechnicalDisplayText(FilesSummaryRaw);
            HashesSummary = IsLocalizedFallbackText(HashesSummaryRaw)
                ? HashesSummaryRaw
                : BuildTechnicalDisplayText(HashesSummaryRaw);

            NotesText = string.IsNullOrWhiteSpace(parsed.NotesText)
                ? BuildDefaultNotes(Status)
                : parsed.NotesText;
            StatusNote = BuildStatusNote(Status);
            FooterSummary = BuildFooterSummary(Status, SuggestedNameRaw);
        }

        public string FileNameRaw { get; }
        public string FileName { get; }
        public string Status { get; }
        public string StatusNote { get; }
        public string DisplayTitle { get; }
        public string SystemName { get; }
        public string ConsoleTypeName { get; }
        public string ManufacturerName { get; }
        public string SizeDisplay { get; }
        public string SuggestedNameRaw { get; }
        public string SuggestedName { get; }
        public string FilesSummaryRaw { get; }
        public string FilesSummary { get; }
        public string HashesSummaryRaw { get; }
        public string HashesSummary { get; }
        public string NotesText { get; }
        public string FooterSummary { get; }
        public bool CanApplyName { get; }

        public bool HasCopyableSuggestedName =>
            !string.IsNullOrWhiteSpace(SuggestedNameRaw)
            && !string.Equals(SuggestedNameRaw, Text(NoSuggestedNameKey), StringComparison.Ordinal);

        public bool HasCopyableHashes =>
            !string.IsNullOrWhiteSpace(HashesSummaryRaw)
            && !string.Equals(HashesSummaryRaw, Text(NoUsefulHashesKey), StringComparison.Ordinal);

        private static string InferManufacturerName(string systemName)
        {
            string value = CleanSystemName(systemName);
            if (string.IsNullOrWhiteSpace(value) || IsUnknownOrUnavailable(value))
            {
                return Text(UnknownKey);
            }

            (string Token, string DisplayName)[] manufacturers =
            [
                ("Microsoft", "Microsoft"),
                ("Nintendo", "Nintendo"),
                ("PlayStation", "Sony"),
                ("Sony", "Sony"),
                ("Sega", "Sega"),
                ("NEC", "NEC"),
                ("TurboGrafx", "NEC"),
                ("PC Engine", "NEC"),
                ("SNK", "SNK"),
                ("Neo Geo", "SNK"),
                ("Atari", "Atari"),
                ("Bandai", "Bandai"),
                ("WonderSwan", "Bandai"),
                ("Panasonic", "Panasonic"),
                ("Philips", "Philips"),
                ("Commodore", "Commodore"),
                ("Amiga", "Commodore"),
                ("Fujitsu", "Fujitsu"),
                ("FM Towns", "Fujitsu"),
                ("Sharp", "Sharp"),
                ("X68000", "Sharp"),
                ("Apple", "Apple"),
                ("IBM", "IBM"),
                ("Sinclair", "Sinclair"),
                ("Amstrad", "Amstrad"),
                ("Coleco", "Coleco"),
                ("Mattel", "Mattel"),
                ("Nokia", "Nokia"),
                ("Tandy", "Tandy"),
                ("VTech", "VTech"),
                ("Casio", "Casio"),
                ("Tiger", "Tiger")
            ];

            foreach ((string token, string displayName) in manufacturers)
            {
                if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return displayName;
                }
            }

            return Text(UnknownKey);
        }

        private static string InferConsoleTypeName(string systemName, string manufacturerName)
        {
            string value = CleanSystemName(systemName);
            if (string.IsNullOrWhiteSpace(value) || IsUnknownOrUnavailable(value))
            {
                return Text(UnknownKey);
            }

            if (!IsUnknownOrUnavailable(manufacturerName))
            {
                value = RemoveManufacturerPrefix(value, manufacturerName);

            }

            value = value.Trim(' ', '-', '—', '/', '\\', ':');
            return string.IsNullOrWhiteSpace(value) ? CleanSystemName(systemName) : value;
        }

        private static string NormalizeSystemDisplayName(string value)
        {
            string cleaned = CleanSystemName(value);
            if (string.IsNullOrWhiteSpace(cleaned) || IsUnknownOrUnavailable(cleaned))
            {
                return Text(UnknownKey);
            }

            return cleaned;
        }

        private static string CleanSystemName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string cleaned = value
                .Replace("\u2066", string.Empty, StringComparison.Ordinal)
                .Replace("\u2067", string.Empty, StringComparison.Ordinal)
                .Replace("\u2068", string.Empty, StringComparison.Ordinal)
                .Replace("\u2069", string.Empty, StringComparison.Ordinal)
                .Trim();

            cleaned = Regex.Replace(
                cleaned,
                @"\s+-\s+Datfile\b.*$",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

            cleaned = Regex.Replace(
                cleaned,
                @"\s+\((?:\d{4}[-/]\d{2}[-/]\d{2}|\d{2}[-/]\d{2}[-/]\d{4}|\d+)[^)]*\)\s*$",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

            return cleaned;
        }

        private static bool IsUnknownOrUnavailable(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                || string.Equals(value, Text(UnknownKey), StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, Text(UnavailableKey), StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, TechnicalUnavailable, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "-", StringComparison.Ordinal);
        }

        private static string RemoveManufacturerPrefix(string value, string manufacturerName)
        {
            string result = value.Trim();

            if (result.StartsWith(manufacturerName, StringComparison.OrdinalIgnoreCase))
            {
                result = result[manufacturerName.Length..].Trim();
            }

            return result.Trim(' ', '-', '—', '/', '\\', ':');
        }

        private static ParsedRedumpDetails Parse(string rawDetails, string fallbackSize, string fallbackSystem, string fallbackFileName)
        {
            rawDetails = Normalize(rawDetails);

            var files = new List<ParsedFile>();
            var current = new ParsedFileBuilder();
            bool hasCurrent = false;

            string gameName = string.Empty;
            string systemName = string.IsNullOrWhiteSpace(fallbackSystem) ? string.Empty : fallbackSystem;
            var notes = new List<string>();

            foreach (string rawLine in rawDetails.Split('\n'))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.Equals("الملفات المفحوصة:", StringComparison.OrdinalIgnoreCase)
                    || line.Equals("المسارات المُجزأة:", StringComparison.OrdinalIgnoreCase)
                    || line.Equals("تفاصيل المطابقة:", StringComparison.OrdinalIgnoreCase)
                    || line.Equals("نتائج المطابقة:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (line.StartsWith("إجمالي الملفات:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("إجمالي الحجم:", StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add(line);
                    continue;
                }

                if (line.StartsWith("• ", StringComparison.Ordinal))
                {
                    if (hasCurrent && !string.IsNullOrWhiteSpace(current.Name))
                    {
                        files.Add(current.Build());
                    }

                    ParsedFileHeader header = ParseFileHeader(line[2..].Trim());
                    current = new ParsedFileBuilder
                    {
                        Name = header.Name,
                        SizeDisplay = header.SizeDisplay
                    };
                    hasCurrent = true;
                    continue;
                }

                if (!hasCurrent && LooksLikeFileName(line))
                {
                    ParsedFileHeader header = ParseFileHeader(line);
                    current = new ParsedFileBuilder
                    {
                        Name = header.Name,
                        SizeDisplay = header.SizeDisplay
                    };
                    hasCurrent = true;
                    continue;
                }

                if (line.StartsWith("الحجم:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasCurrent)
                    {
                        current = new ParsedFileBuilder { Name = fallbackFileName };
                        hasCurrent = true;
                    }

                    current.SizeDisplay = ParseSizeText(line);
                    continue;
                }

                if (line.StartsWith("CRC:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasCurrent)
                    {
                        current = new ParsedFileBuilder { Name = fallbackFileName };
                        hasCurrent = true;
                    }

                    current.Crc = AfterColon(line);
                    continue;
                }

                if (line.StartsWith("MD5:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasCurrent)
                    {
                        current = new ParsedFileBuilder { Name = fallbackFileName };
                        hasCurrent = true;
                    }

                    current.Md5 = AfterColon(line);
                    continue;
                }

                if (line.StartsWith("SHA1:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasCurrent)
                    {
                        current = new ParsedFileBuilder { Name = fallbackFileName };
                        hasCurrent = true;
                    }

                    current.Sha1 = AfterColon(line);
                    continue;
                }

                if (line.StartsWith("اسم النظام:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("النظام:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("المنصة:", StringComparison.OrdinalIgnoreCase))
                {
                    systemName = AfterColon(line);
                    continue;
                }

                if (line.StartsWith("اللعبة:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("الاسم القياسي:", StringComparison.OrdinalIgnoreCase))
                {
                    gameName = AfterColon(line);
                    continue;
                }

                if (line.StartsWith("اسم السجل:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("السجل:", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(gameName))
                    {
                        gameName = AfterColon(line);
                    }

                    continue;
                }

                if (line.StartsWith("طريقة التحقق:", StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add(line);
                    continue;
                }

                if (!LooksLikeNoise(line))
                {
                    notes.Add(line);
                }
            }

            if (hasCurrent && !string.IsNullOrWhiteSpace(current.Name))
            {
                files.Add(current.Build());
            }

            string totalSizeDisplay = files.Count > 0
                ? BuildTotalSize(files)
                : (string.IsNullOrWhiteSpace(fallbackSize) ? TechnicalUnavailable : fallbackSize);

            string filesSummary = BuildFilesSummary(files, fallbackFileName, totalSizeDisplay);
            string hashesSummary = BuildHashesSummary(files);
            string notesText = BuildNotesText(notes);

            return new ParsedRedumpDetails(gameName, systemName, totalSizeDisplay, filesSummary, hashesSummary, notesText);
        }

        private static string BuildStatusNote(string status)
        {
            if (status.Contains("سليم", StringComparison.OrdinalIgnoreCase))
            {
                return Text(StatusNoteMatchedKey);
            }

            if (status.Contains("غير مكتمل", StringComparison.OrdinalIgnoreCase))
            {
                return Text(StatusNotePartialKey);
            }

            if (status.Contains("بلا قاعدة", StringComparison.OrdinalIgnoreCase))
            {
                return Text(StatusNoteNoDatabaseKey);
            }

            if (status.Contains("متضاربة", StringComparison.OrdinalIgnoreCase))
            {
                return Text(StatusNoteConflictedKey);
            }

            if (status.Contains("تالف", StringComparison.OrdinalIgnoreCase) || status.Contains("معدل", StringComparison.OrdinalIgnoreCase))
            {
                return Text(StatusNoteModifiedOrCorruptKey);
            }

            return Text(StatusNoteGenericKey);
        }

        private static string BuildDefaultNotes(string status)
        {
            if (status.Contains("بلا قاعدة", StringComparison.OrdinalIgnoreCase))
            {
                return Text(DefaultNotesNoDatabaseKey);
            }

            return Text(DefaultNotesNoneKey);
        }

        private static string BuildFooterSummary(string status, string suggestedName)
        {
            if (string.IsNullOrWhiteSpace(suggestedName) || string.Equals(suggestedName, Text(NoSuggestedNameKey), StringComparison.Ordinal))
            {
                return status;
            }

            return status + " — " + suggestedName;
        }

        private static string BuildFilesSummary(IReadOnlyList<ParsedFile> files, string fallbackFileName, string fallbackSize)
        {
            if (files.Count == 0)
            {
                return BuildSingleFileSummary(fallbackFileName, fallbackSize);
            }

            return string.Join(Environment.NewLine, files.Select(static file => BuildSingleFileSummary(file.Name, file.SizeDisplay, file.SizeBytes)));
        }

        private static string BuildSingleFileSummary(string fileName, string sizeDisplay)
        {
            return BuildSingleFileSummary(fileName, sizeDisplay, 0);
        }

        private static string BuildSingleFileSummary(string fileName, string sizeDisplay, long sizeBytes)
        {
            ParsedFileHeader header = ParseFileHeader(fileName);
            string safeFileName = CleanTechnicalName(header.Name);
            string effectiveSize = string.IsNullOrWhiteSpace(header.SizeDisplay) ? sizeDisplay : header.SizeDisplay;
            string safeSize = sizeBytes > 0 ? FormatTechnicalByteSize(sizeBytes) : ToTechnicalSizeText(effectiveSize);

            return string.IsNullOrWhiteSpace(safeSize)
                ? safeFileName
                : string.Create(CultureInfo.InvariantCulture, $"{safeFileName} — {safeSize}");
        }

        private static string BuildHashesSummary(IReadOnlyList<ParsedFile> files)
        {
            if (files.Count == 0)
            {
                return string.Empty;
            }

            var blocks = new List<string>();
            bool includeFileHeader = files.Count > 1;

            foreach (ParsedFile file in files)
            {
                var builder = new StringBuilder();

                if (includeFileHeader)
                {
                    builder.AppendLine(CleanTechnicalName(file.Name));
                }

                if (!string.IsNullOrWhiteSpace(file.Crc))
                {
                    builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"CRC : {CleanHashValue(file.Crc)}"));
                }

                builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"MD5 : {TechnicalFallback(file.Md5)}"));
                builder.Append(string.Create(CultureInfo.InvariantCulture, $"SHA1: {TechnicalFallback(file.Sha1)}"));

                blocks.Add(builder.ToString());
            }

            return string.Join(Environment.NewLine + Environment.NewLine, blocks);
        }

        private static string BuildNotesText(IReadOnlyList<string> notes)
        {
            List<string> filtered = notes
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return filtered.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, filtered.Select(static value => "• " + value));
        }

        private static string BuildTotalSize(IReadOnlyList<ParsedFile> files)
        {
            long totalBytes = files.Sum(static file => file.SizeBytes);
            if (totalBytes > 0)
            {
                return FormatTechnicalByteSize(totalBytes);
            }

            string? firstNonEmpty = files
                .Select(static item => item.SizeDisplay)
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

            return string.IsNullOrWhiteSpace(firstNonEmpty)
                ? TechnicalUnavailable
                : ToTechnicalSizeText(firstNonEmpty!);
        }

        private static ParsedFileHeader ParseFileHeader(string value)
        {
            string text = StripLeadingBullet(NormalizeInline(value));
            if (string.IsNullOrWhiteSpace(text))
            {
                return new ParsedFileHeader(string.Empty, string.Empty);
            }

            string[] separators = { " — ", " – ", " - " };
            foreach (string separator in separators)
            {
                int index = text.LastIndexOf(separator, StringComparison.Ordinal);
                if (index <= 0 || index >= text.Length - separator.Length)
                {
                    continue;
                }

                string left = text[..index].Trim();
                string right = text[(index + separator.Length)..].Trim();

                if (!string.IsNullOrWhiteSpace(left) && LooksLikeSizeText(right))
                {
                    return new ParsedFileHeader(left, right);
                }
            }

            return new ParsedFileHeader(text, string.Empty);
        }

        private static string CleanTechnicalName(string value)
        {
            string text = StripLeadingBullet(NormalizeInline(value));
            text = RemoveBidiFormattingCharacters(text);
            text = NormalizeTechnicalDigits(text);

            return string.IsNullOrWhiteSpace(text) || string.Equals(text, Text(UnnamedFileKey), StringComparison.Ordinal)
                ? TechnicalUnnamedFile
                : text;
        }

        private static string ToTechnicalSizeText(string sizeDisplay)
        {
            string text = NormalizeTechnicalDigits(NormalizeInline(sizeDisplay));
            if (string.IsNullOrWhiteSpace(text)
                || string.Equals(text, Text(UnavailableKey), StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, Text(UnknownKey), StringComparison.OrdinalIgnoreCase))
            {
                return TechnicalUnavailable;
            }

            long bytes = ParseByteCountFromText(text);
            if (bytes > 0)
            {
                return FormatTechnicalByteSize(bytes);
            }

            string localizedByteUnit = Text(ByteUnitKey);
            if (!string.IsNullOrWhiteSpace(localizedByteUnit))
            {
                text = text.Replace(localizedByteUnit, TechnicalByteUnit, StringComparison.OrdinalIgnoreCase);
            }

            text = text.Replace("بايت", TechnicalByteUnit, StringComparison.OrdinalIgnoreCase);
            return NormalizeTechnicalDigits(RemoveBidiFormattingCharacters(text));
        }

        private static string ParseSizeText(string line)
        {
            string after = AfterColon(line);
            long bytes = ParseByteCountFromText(after);
            return bytes > 0 ? FormatTechnicalByteSize(bytes) : ToTechnicalSizeText(after);
        }

        private static long ParseByteCountFromText(string text)
        {
            text = NormalizeTechnicalDigits(text);

            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            Match match = Regex.Match(
                text,
                @"\((?<bytes>[0-9][0-9,\.]*)\s*(?:بايت|bytes)\)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                match = Regex.Match(
                    text,
                    @"^(?<bytes>[0-9][0-9,\.]*)\s*(?:بايت|bytes)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            if (!match.Success)
            {
                return 0;
            }

            string digits = Regex.Replace(match.Groups["bytes"].Value, @"[^0-9]", string.Empty);
            return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long bytes) ? bytes : 0;
        }

        private static string FormatTechnicalByteSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = Math.Abs((double)bytes);
            int unitIndex = 0;

            while (value >= 1024d && unitIndex < units.Length - 1)
            {
                value /= 1024d;
                unitIndex++;
            }

            if (unitIndex == 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:N0} bytes", bytes);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1} ({2:N0} bytes)", value, units[unitIndex], bytes);
        }

        private static bool LooksLikeSizeText(string text)
        {
            return !string.IsNullOrWhiteSpace(text)
                && (Regex.IsMatch(text, @"\b(?:B|KB|MB|GB|TB)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    || text.Contains("بايت", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("bytes", StringComparison.OrdinalIgnoreCase));
        }

        private static bool LooksLikeFileName(string line)
        {
            return line.Contains('.') && !line.Contains(':') && line.Length < 260;
        }

        private static bool LooksLikeNoise(string line)
        {
            return line.Equals("✓ مطابقة Redump:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLocalizedFallbackText(string value)
        {
            return string.Equals(value, Text(NoSuggestedNameKey), StringComparison.Ordinal)
                || string.Equals(value, Text(NoUsefulHashesKey), StringComparison.Ordinal)
                || string.Equals(value, Text(UnavailableKey), StringComparison.Ordinal)
                || string.Equals(value, Text(UnknownKey), StringComparison.Ordinal);
        }

        private static string BuildTechnicalDisplayText(string value)
        {
            string normalized = Normalize(RemoveBidiFormattingCharacters(value));
            normalized = NormalizeTechnicalDigits(normalized);

            return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
        }

        private static string RemoveBidiFormattingCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);

            foreach (char ch in value)
            {
                if (ch is '\u200E' or '\u200F'
                    or '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E'
                    or '\u2066' or '\u2067' or '\u2068' or '\u2069')
                {
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        private static string CleanHashValue(string value)
        {
            return NormalizeTechnicalDigits(RemoveBidiFormattingCharacters(value.Trim()));
        }

        private static string Normalize(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string NormalizeInline(string text)
        {
            return Normalize(text).Replace('\n', ' ').Trim();
        }

        private static string StripLeadingBullet(string text)
        {
            string value = text.Trim();

            while (value.StartsWith('•'))
            {
                value = value[1..].TrimStart();
            }

            return value;
        }

        private static string AfterColon(string text)
        {
            int index = text.IndexOf(':');
            return index < 0 ? text.Trim() : text[(index + 1)..].Trim();
        }

        private static string TechnicalFallback(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? TechnicalUnavailable : CleanHashValue(value);
        }

        private static string NormalizeTechnicalDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);

            foreach (char ch in value)
            {
                builder.Append(ch switch
                {
                    >= '\u0660' and <= '\u0669' => (char)('0' + (ch - '\u0660')),
                    >= '\u06F0' and <= '\u06F9' => (char)('0' + (ch - '\u06F0')),
                    _ => ch
                });
            }

            return builder.ToString();
        }

        private static string Text(string key)
        {
            return ArabicUi.Get(key);
        }

        private sealed record ParsedRedumpDetails(
            string GameName,
            string SystemName,
            string TotalSizeDisplay,
            string FilesSummary,
            string HashesSummary,
            string NotesText);

        private sealed record ParsedFileHeader(
            string Name,
            string SizeDisplay);

        private sealed record ParsedFile(
            string Name,
            string SizeDisplay,
            long SizeBytes,
            string Crc,
            string Md5,
            string Sha1);

        private sealed class ParsedFileBuilder
        {
            public string Name { get; set; } = string.Empty;
            public string SizeDisplay { get; set; } = string.Empty;
            public string Crc { get; set; } = string.Empty;
            public string Md5 { get; set; } = string.Empty;
            public string Sha1 { get; set; } = string.Empty;

            public ParsedFile Build()
            {
                long sizeBytes = ParseByteCountFromText(SizeDisplay ?? string.Empty);

                return new ParsedFile(
                    string.IsNullOrWhiteSpace(Name) ? Text(UnnamedFileKey) : Name,
                    string.IsNullOrWhiteSpace(SizeDisplay) ? TechnicalUnavailable : SizeDisplay,
                    sizeBytes,
                    Crc ?? string.Empty,
                    Md5 ?? string.Empty,
                    Sha1 ?? string.Empty);
            }
        }
    }
}