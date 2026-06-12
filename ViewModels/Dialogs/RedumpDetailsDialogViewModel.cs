using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.ViewModels;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.ViewModels.Dialogs;

    public sealed partial class RedumpDetailsDialogViewModel : INotifyPropertyChanged
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

        public RedumpDetailsDialogViewModel(
            TaskQueueItemViewModel item,
            bool canApplyName,
            IRedumpDetailsTextCopyService textCopyService,
            IRedumpDetailsFeedbackTimer feedbackTimer)
        {
            ArgumentNullException.ThrowIfNull(item);
            _textCopyService = textCopyService ?? throw new ArgumentNullException(nameof(textCopyService));
            _feedbackTimer = feedbackTimer ?? throw new ArgumentNullException(nameof(feedbackTimer));

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

            CopyNameCommand = new RelayCommand(CopyName);
            CopyHashesCommand = new RelayCommand(CopyHashes);
        CloseCommand = new RelayCommand(RequestClose);
        ApplyNameCommand = new RelayCommand(RequestApplyName, () => CanApplyName);
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

        public IRelayCommand CopyNameCommand { get; }
        public IRelayCommand CopyHashesCommand { get; }
    public IRelayCommand CloseCommand { get; }
    public IRelayCommand ApplyNameCommand { get; }

    public event Action<bool>? CloseRequested;

    private readonly IRedumpDetailsTextCopyService _textCopyService;
        private readonly IRedumpDetailsFeedbackTimer _feedbackTimer;
        private string _copyFeedbackText = string.Empty;
        private bool _isCopyFeedbackVisible;
        private bool _isCopyFeedbackError;
        private double _copyFeedbackOpacity;

        public string CopyFeedbackText
        {
            get => _copyFeedbackText;
            private set => SetProperty(ref _copyFeedbackText, value);
        }

        public bool IsCopyFeedbackVisible
        {
            get => _isCopyFeedbackVisible;
            private set => SetProperty(ref _isCopyFeedbackVisible, value);
        }

        public bool IsCopyFeedbackError
        {
            get => _isCopyFeedbackError;
            private set => SetProperty(ref _isCopyFeedbackError, value);
        }

        public double CopyFeedbackOpacity
        {
            get => _copyFeedbackOpacity;
            private set => SetProperty(ref _copyFeedbackOpacity, value);
        }

        public bool HasCopyableSuggestedName =>
            !string.IsNullOrWhiteSpace(SuggestedNameRaw)
            && !string.Equals(SuggestedNameRaw, Text(NoSuggestedNameKey), StringComparison.Ordinal);

        public bool HasCopyableHashes =>
            !string.IsNullOrWhiteSpace(HashesSummaryRaw)
            && !string.Equals(HashesSummaryRaw, Text(NoUsefulHashesKey), StringComparison.Ordinal);

        public event PropertyChangedEventHandler? PropertyChanged;

        public void DisposeFeedbackTimer()
        {
            _feedbackTimer.Dispose();
        }

    private void RequestClose()
    {
        CloseRequested?.Invoke(false);
    }

    private void RequestApplyName()
    {
        if (!CanApplyName)
        {
            return;
        }

        CloseRequested?.Invoke(true);
    }
    
    private void CopyName()
        {
            if (!HasCopyableSuggestedName)
            {
                ShowCopyFeedback(Text(CopyNameUnavailableKey), isError: true);
                return;
            }

            TryCopyText(SuggestedNameRaw, CopyNameSuccessKey, CopyNameFailedKey);
        }

        private void CopyHashes()
        {
            if (!HasCopyableHashes)
            {
                ShowCopyFeedback(Text(CopyHashesUnavailableKey), isError: true);
                return;
            }

            TryCopyText(HashesSummaryRaw, CopyHashesSuccessKey, CopyHashesFailedKey);
        }

        private void TryCopyText(string value, string successKey, string failureKey)
        {
            bool copied = _textCopyService.TrySetText(value);
            ShowCopyFeedback(Text(copied ? successKey : failureKey), isError: !copied);
        }

        private void ShowCopyFeedback(string message, bool isError)
        {
            _feedbackTimer.Stop();

            CopyFeedbackText = message;
            IsCopyFeedbackError = isError;
            IsCopyFeedbackVisible = true;
            CopyFeedbackOpacity = 1d;

            _feedbackTimer.Restart(HideCopyFeedback);
        }

        private void HideCopyFeedback()
        {
            IsCopyFeedbackVisible = false;
            CopyFeedbackOpacity = 0d;
            CopyFeedbackText = string.Empty;
            IsCopyFeedbackError = false;
        }

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

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

            value = value.Trim(' ', '-', '\u2014', '/', '\\', ':');
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

            return result.Trim(' ', '-', '\u2014', '/', '\\', ':');
        }

    }
