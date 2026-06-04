using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Licensing;
using HakamiqChdTool.App.ViewModels;
using Microsoft.Win32;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HakamiqChdTool.App.Views;

public sealed class AdvancedOptionsAppliedEventArgs : EventArgs
{
    public AdvancedOptionsAppliedEventArgs(AppSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public AppSettings Settings { get; }
}

public partial class AdvancedOptionsWindow : Window
{
    private const string DatabaseInitialStatusKey = "LocAdvancedOptions_RedumpDatabaseInitialStatus";
    private const string InvalidFieldFallbackKey = "LocAdvancedOptions_InvalidFieldFallback";
    private const string InvalidDataTitleKey = "LocAdvancedOptions_InvalidDataTitle";
    private const string SelectRedumpDatTitleKey = "LocAdvancedOptions_SelectRedumpDatTitle";
    private const string RedumpDatFilterKey = "LocAdvancedOptions_RedumpDatFilter";
    private const string RedumpImportRunningKey = "LocAdvancedOptions_RedumpImportRunning";
    private const string RedumpImportRowsProcessedKey = "LocAdvancedOptions_RedumpImportRowsProcessed";
    private const string RedumpImportSuccessTitleKey = "LocAdvancedOptions_RedumpImportSuccessTitle";
    private const string RedumpImportFailedTitleKey = "LocAdvancedOptions_RedumpImportFailedTitle";
    private const string RedumpImportErrorTitleKey = "LocAdvancedOptions_RedumpImportErrorTitle";
    private const string OperationErrorTitleKey = "LocAdvancedOptions_OperationErrorTitle";
    private const string MissingDataTitleKey = "LocAdvancedOptions_MissingDataTitle";
    private const string RedumpDownloadUrlRequiredKey = "LocAdvancedOptions_RedumpDownloadUrlRequired";
    private const string RedumpDownloadInvalidUrlKey = "LocAdvancedOptions_RedumpDownloadInvalidUrl";
    private const string RedumpDownloadRunningKey = "LocAdvancedOptions_RedumpDownloadRunning";
    private const string RedumpSyncFailedTitleKey = "LocAdvancedOptions_RedumpSyncFailedTitle";
    private const string RedumpDownloadFailedKey = "LocAdvancedOptions_RedumpDownloadFailed";
    private const string RedumpDownloadErrorTitleKey = "LocAdvancedOptions_RedumpDownloadErrorTitle";
    private const string DatabaseCheckingKey = "LocAdvancedOptions_DatabaseChecking";
    private const string DatabaseAvailableKey = "LocAdvancedOptions_DatabaseAvailable";
    private const string DatabaseMissingKey = "LocAdvancedOptions_DatabaseMissing";
    private const string DatabaseReadFailedKey = "LocAdvancedOptions_DatabaseReadFailed";

    public const string GeneralTabKey = "General";
    public const string PathsTabKey = "Paths";
    public const string RedumpTabKey = "Redump";
    public const string ProcessingTabKey = "Processing";
    public const string PerformanceTabKey = "Performance";

    private static readonly ILogger Logger = Log.ForContext<AdvancedOptionsWindow>();

    private readonly AppSettings _currentSettings;
    private readonly IFeatureAccessService _featureAccessService;
    private readonly RedumpGitHubSyncManager _syncManager = new();
    private readonly CancellationTokenSource _windowLifetimeCts = new();
    private readonly ToolTipEventHandler _toolTipOpeningHandler;

    private CancellationTokenSource? _databaseStateRefreshCts;
    private int _databaseStateRefreshGeneration;
    private bool _isClosed;

    public AdvancedOptionsViewModel ViewModel { get; }

    public AppSettings ResultSettings { get; private set; }

    public string ActiveTabKey => GetActiveTabKey();

    public event EventHandler<AdvancedOptionsAppliedEventArgs>? SettingsApplied;

    public AdvancedOptionsWindow(AppSettings currentSettings, IFeatureAccessService featureAccessService)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        ArgumentNullException.ThrowIfNull(featureAccessService);

        InitializeComponent();
        AppLanguageService.ApplyToWindow(this);

        RedumpPanel.DownloadDatabaseRequested += DownloadDatabaseButton_Click;
        RedumpPanel.ImportRedumpDatabaseRequested += ImportRedumpDatabaseButton_Click;

        _currentSettings = currentSettings;
        _featureAccessService = featureAccessService;
        ResultSettings = currentSettings.Clone();

        ViewModel = new AdvancedOptionsViewModel();
        ApplyFeatureAccessToViewModel();
        DataContext = ViewModel;

        Loaded += AdvancedOptionsWindow_Loaded;
        Closed += AdvancedOptionsWindow_Closed;

        _toolTipOpeningHandler = AdvancedOptionsWindow_ToolTipOpening;
        AddHandler(ToolTipService.ToolTipOpeningEvent, _toolTipOpeningHandler, true);

        ViewModel.Load(ResultSettings);
        ApplyPremiumRestrictionsToViewModel(showDialog: false);
        ResultSettings = _currentSettings.Clone();
        ViewModel.IsDatabaseAvailable = false;
        ViewModel.DatabaseStatusText = ResolveUiText(DatabaseInitialStatusKey);
    }

    private void AdvancedOptionsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateVisiblePanel();

        _ = Dispatcher.BeginInvoke(
            new Action(QueueDatabaseStateRefresh),
            DispatcherPriority.ContextIdle);
    }

    private void AdvancedOptionsWindow_Closed(object? sender, EventArgs e)
    {
        _isClosed = true;

        RedumpPanel.DownloadDatabaseRequested -= DownloadDatabaseButton_Click;
        RedumpPanel.ImportRedumpDatabaseRequested -= ImportRedumpDatabaseButton_Click;

        Loaded -= AdvancedOptionsWindow_Loaded;
        Closed -= AdvancedOptionsWindow_Closed;

        RemoveHandler(ToolTipService.ToolTipOpeningEvent, _toolTipOpeningHandler);

        _databaseStateRefreshCts?.Cancel();
        _databaseStateRefreshCts?.Dispose();

        _windowLifetimeCts.Cancel();
        _windowLifetimeCts.Dispose();

        _syncManager.Dispose();
    }

    private void AdvancedOptionsWindow_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element)
        {
            NormalizeToolTipOwner(element);
        }
    }

    private void NormalizeToolTipOwner(FrameworkElement element)
    {
        if (element.ToolTip is string text && !string.IsNullOrWhiteSpace(text))
        {
            element.ToolTip = CreateFluentToolTip(text);
        }
        else if (element.ToolTip is ToolTip toolTip)
        {
            ApplyFluentToolTipStyle(toolTip);
        }
    }

    private ToolTip CreateFluentToolTip(string text)
    {
        var toolTip = new ToolTip
        {
            Content = CreateToolTipTextBlock(text),
            FlowDirection = FlowDirection
        };

        ApplyFluentToolTipStyle(toolTip);
        return toolTip;
    }

    private TextBlock CreateToolTipTextBlock(string text)
    {
        var content = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FlowDirection = FlowDirection
        };

        content.SetResourceReference(TextElement.ForegroundProperty, "Brush.Text.Primary");
        content.SetResourceReference(TextElement.FontFamilyProperty, "FontFamily.Base");
        content.SetResourceReference(TextElement.FontSizeProperty, "FontSize.Caption");
        content.SetResourceReference(TextBlock.LineHeightProperty, "Font.LineHeight.ToolTip");

        return content;
    }

    private void ApplyFluentToolTipStyle(ToolTip toolTip)
    {
        toolTip.OverridesDefaultStyle = true;

        if (TryFindResource("AdvancedOptionsToolTipStyle") is Style localStyle)
        {
            toolTip.Style = localStyle;
        }
        else if (TryFindResource("FluentToolTipStyle") is Style fluentStyle)
        {
            toolTip.Style = fluentStyle;
        }

        if (toolTip.Content is string text && !string.IsNullOrWhiteSpace(text))
        {
            toolTip.Content = CreateToolTipTextBlock(text);
        }

        toolTip.SetResourceReference(Control.BackgroundProperty, "Brush.Background.Elevated");
        toolTip.SetResourceReference(Control.ForegroundProperty, "Brush.Text.Primary");
        toolTip.SetResourceReference(Control.BorderBrushProperty, "Brush.Border.Subtle");
        toolTip.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Base");
        toolTip.SetResourceReference(Control.FontSizeProperty, "FontSize.Caption");
        toolTip.SetResourceReference(Control.PaddingProperty, "Inset.12.All");
        toolTip.SetResourceReference(Control.BorderThicknessProperty, "Inset.1.All");
        toolTip.SetResourceReference(FrameworkElement.MaxWidthProperty, "Layout.ToolTip.MaxWidth");
    }

    public void SelectTab(string? tabKey)
    {
        string normalizedTabKey = NormalizeTabKey(tabKey);

        GeneralTabButton.IsChecked = string.Equals(normalizedTabKey, GeneralTabKey, StringComparison.Ordinal);
        PathsTabButton.IsChecked = string.Equals(normalizedTabKey, PathsTabKey, StringComparison.Ordinal);
        RedumpTabButton.IsChecked = string.Equals(normalizedTabKey, RedumpTabKey, StringComparison.Ordinal);
        ProcessingTabButton.IsChecked = string.Equals(normalizedTabKey, ProcessingTabKey, StringComparison.Ordinal);
        PerformanceTabButton.IsChecked = string.Equals(normalizedTabKey, PerformanceTabKey, StringComparison.Ordinal);

        UpdateVisiblePanel();
    }

    private string GetActiveTabKey()
    {
        if (PathsTabButton?.IsChecked == true)
        {
            return PathsTabKey;
        }

        if (RedumpTabButton?.IsChecked == true)
        {
            return RedumpTabKey;
        }

        if (ProcessingTabButton?.IsChecked == true)
        {
            return ProcessingTabKey;
        }

        if (PerformanceTabButton?.IsChecked == true)
        {
            return PerformanceTabKey;
        }

        return GeneralTabKey;
    }

    private static string NormalizeTabKey(string? tabKey)
    {
        if (string.IsNullOrWhiteSpace(tabKey))
        {
            return GeneralTabKey;
        }

        string value = tabKey.Trim();

        if (value.Equals(PathsTabKey, StringComparison.OrdinalIgnoreCase))
        {
            return PathsTabKey;
        }

        if (value.Equals(RedumpTabKey, StringComparison.OrdinalIgnoreCase))
        {
            return RedumpTabKey;
        }

        if (value.Equals(ProcessingTabKey, StringComparison.OrdinalIgnoreCase))
        {
            return ProcessingTabKey;
        }

        if (value.Equals(PerformanceTabKey, StringComparison.OrdinalIgnoreCase))
        {
            return PerformanceTabKey;
        }

        return GeneralTabKey;
    }

    private void OnTabButtonChecked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateVisiblePanel();
        }
    }

    private void UpdateVisiblePanel()
    {
        if (GeneralPanel is null || PathsPanel is null || RedumpPanel is null || ProcessingPanel is null || PerformancePanel is null)
        {
            return;
        }

        SetPanelVisibilityIfAvailable(GeneralPanel, GeneralTabButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed);
        SetPanelVisibilityIfAvailable(PathsPanel, PathsTabButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed);
        SetPanelVisibilityIfAvailable(RedumpPanel, RedumpTabButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed);
        SetPanelVisibilityIfAvailable(ProcessingPanel, ProcessingTabButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed);
        SetPanelVisibilityIfAvailable(PerformancePanel, PerformanceTabButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException ex)
        {
            Logger.Debug(ex, "Advanced options window drag was ignored because DragMove was not valid for the current mouse state.");
        }
    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {

        ViewModel.ApplyDefaultEngineSettings();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _ = TryApplySettings();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryApplySettings())
        {
            return;
        }

        DialogResult = true;
        Close();
    }

    private bool TryApplySettings()
    {
        ViewModel.ValidateForSave();
        if (!ViewModel.CanConfirm)
        {
            string firstError = ResolveKeyOrText(ViewModel.GetFirstErrorMessage());
            if (string.IsNullOrWhiteSpace(firstError))
            {
                firstError = ResolveUiText(InvalidFieldFallbackKey);
            }

            ShowNoticeDialog(InvalidDataTitleKey, firstError);

            return false;
        }

        if (!ViewModel.HasPendingChanges)
        {
            return true;
        }

        string previousLanguage = AppLanguageService.Instance.CurrentLanguageName;

        AppSettings pendingSettings = ViewModel.BuildResultSettings(_currentSettings);
        if (!TryValidatePremiumFeatureChanges(pendingSettings))
        {
            return false;
        }

        ResultSettings = pendingSettings;

        SettingsApplied?.Invoke(this, new AdvancedOptionsAppliedEventArgs(ResultSettings.Clone()));
        ViewModel.AcceptAppliedSettings(ResultSettings);

        RefreshAfterAppliedSettings(previousLanguage);
        return true;
    }

    private void RefreshAfterAppliedSettings(string previousLanguage)
    {
        if (_isClosed)
        {
            return;
        }

        AppLanguageService.ApplyToWindow(this);
        UpdateVisiblePanel();

        string currentLanguage = AppLanguageService.Instance.CurrentLanguageName;
        if (string.Equals(previousLanguage, currentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            InvalidateLayoutTree();
            return;
        }

        _ = Dispatcher.BeginInvoke(
            new Action(RefreshLayoutAfterLanguageSwitch),
            DispatcherPriority.Loaded);
    }

    private void RefreshLayoutAfterLanguageSwitch()
    {
        if (_isClosed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        AppLanguageService.ApplyToWindow(this);
        UpdateVisiblePanel();
        InvalidateLayoutTree();
        UpdateLayout();

        _ = Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_isClosed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                UpdateVisiblePanel();
                InvalidateLayoutTree();
                UpdateLayout();
            }),
            DispatcherPriority.ContextIdle);
    }

    private void InvalidateLayoutTree()
    {
        foreach (FrameworkElement element in EnumerateFrameworkElements(this))
        {
            element.InvalidateMeasure();
            element.InvalidateArrange();
            element.InvalidateVisual();
        }
    }

    private static IEnumerable<FrameworkElement> EnumerateFrameworkElements(DependencyObject root)
    {
        if (root is FrameworkElement element)
        {
            yield return element;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            foreach (FrameworkElement descendant in EnumerateFrameworkElements(child))
            {
                yield return descendant;
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    private void ImportRedumpDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RequirePremiumFeature(PremiumFeature.RedumpDatabaseImport))
        {
            return;
        }

        _ = ImportRedumpDatabaseButton_ClickAsync();
    }

    private async Task ImportRedumpDatabaseButton_ClickAsync()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = ResolveUiText(SelectRedumpDatTitleKey),
                Filter = ResolveUiText(RedumpDatFilterKey)
            };

            string prior = ViewModel.RedumpDatXmlPath.Trim();
            if (File.Exists(prior))
            {
                dialog.FileName = Path.GetFileName(prior);

                string? directory = Path.GetDirectoryName(prior);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    dialog.InitialDirectory = directory;
                }
            }

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            ViewModel.RedumpDatXmlPath = dialog.FileName;

            string systemName = string.IsNullOrWhiteSpace(ViewModel.RedumpSystemName)
                ? Path.GetFileNameWithoutExtension(dialog.FileName)
                : ViewModel.RedumpSystemName.Trim();

            RedumpPanel.ImportRedumpDatabaseButtonView.IsEnabled = false;
            RedumpPanel.RedumpImportProgressBarView.Visibility = Visibility.Visible;
            RedumpPanel.RedumpImportProgressBarView.IsIndeterminate = true;
            RedumpPanel.RedumpImportStatusTextBlockView.Visibility = Visibility.Visible;
            RedumpPanel.RedumpImportStatusTextBlockView.Text = ResolveUiText(RedumpImportRunningKey);

            CancellationToken operationToken = _windowLifetimeCts.Token;

            try
            {
                var progress = new Progress<RedumpImportProgress>(progressValue =>
                {
                    if (_isClosed || operationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    RedumpPanel.RedumpImportStatusTextBlockView.Text =
                        ResolveUiText(RedumpImportRowsProcessedKey, progressValue.RowsInserted);
                });

                RedumpImportResult result = await RedumpSqliteManager.Default
                    .ImportDatFileAsync(dialog.FileName, systemName, progress, operationToken)
                    .ConfigureAwait(true);

                if (_isClosed || operationToken.IsCancellationRequested)
                {
                    return;
                }

                string resultMessage = ResolveUiText(result.MessageKey, result.MessageArgs);
                RedumpPanel.RedumpImportStatusTextBlockView.Text = resultMessage;

                ShowNoticeDialog(
                    result.Success ? RedumpImportSuccessTitleKey : RedumpImportFailedTitleKey,
                    resultMessage);

                QueueDatabaseStateRefresh();
            }
            catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (!_isClosed)
                {
                    ShowNoticeDialog(
                        RedumpImportErrorTitleKey,
                        RuntimeDiagnosticFormatter.SummarizeException(ex));
                }
            }
            finally
            {
                if (!_isClosed)
                {
                    RedumpPanel.ImportRedumpDatabaseButtonView.IsEnabled = true;
                    RedumpPanel.RedumpImportProgressBarView.IsIndeterminate = false;
                    RedumpPanel.RedumpImportProgressBarView.Visibility = Visibility.Collapsed;
                    RedumpPanel.RedumpImportStatusTextBlockView.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch (Exception ex)
        {
            ShowNoticeDialog(
                OperationErrorTitleKey,
                RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
    }

    private void DownloadDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RequirePremiumFeature(PremiumFeature.RedumpDatabaseImport))
        {
            return;
        }

        _ = DownloadDatabaseButton_ClickAsync();
    }

    private async Task DownloadDatabaseButton_ClickAsync()
    {
        try
        {
            ViewModel.ValidateForSave();

            if (!ViewModel.CanDownloadSelectedRedumpDatabase)
            {
                ShowNoticeDialog(MissingDataTitleKey, RedumpDownloadUrlRequiredKey);

                return;
            }

            if (ViewModel.GetErrors(nameof(ViewModel.RedumpDatabaseDownloadUrl)).Cast<object>().Any())
            {
                ShowNoticeDialog(MissingDataTitleKey, RedumpDownloadInvalidUrlKey);

                return;
            }

            RedumpPanel.DownloadDatabaseButtonView.IsEnabled = false;
            RedumpPanel.DatabaseDownloadProgressBarView.Visibility = Visibility.Visible;
            RedumpPanel.DatabaseDownloadStatusTextBlockView.Visibility = Visibility.Visible;
            RedumpPanel.DatabaseDownloadProgressBarView.Value = 0;
            RedumpPanel.DatabaseDownloadStatusTextBlockView.Text = ResolveUiText(RedumpDownloadRunningKey);

            CancellationToken operationToken = _windowLifetimeCts.Token;

            try
            {
                var progress = new Progress<RedumpGitHubSyncProgress>(progressValue =>
                {
                    if (_isClosed || operationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    RedumpPanel.DatabaseDownloadProgressBarView.Value = progressValue.Percent;
                    RedumpPanel.DatabaseDownloadStatusTextBlockView.Text =
                        ResolveUiText(progressValue.MessageKey, progressValue.MessageArgs);
                });

                RedumpGitHubSyncResult sync = await _syncManager
                    .SyncFromGitHubAsync(ViewModel.RedumpDatabaseDownloadUrl.Trim(), progress, operationToken)
                    .ConfigureAwait(true);

                if (_isClosed || operationToken.IsCancellationRequested)
                {
                    return;
                }

                string syncMessage = ResolveUiText(sync.MessageKey, sync.MessageArgs);

                if (!sync.Success)
                {
                    RedumpPanel.DatabaseDownloadStatusTextBlockView.Text = syncMessage;

                    ShowNoticeDialog(RedumpSyncFailedTitleKey, syncMessage);

                    return;
                }

                ViewModel.SetDatabaseLastSyncedUtc(sync.SyncedAtUtc.ToString("O", CultureInfo.InvariantCulture));
                QueueDatabaseStateRefresh();

                RedumpPanel.DatabaseDownloadStatusTextBlockView.Text = syncMessage;
            }
            catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (!_isClosed)
                {
                    RedumpPanel.DatabaseDownloadStatusTextBlockView.Text = ResolveUiText(RedumpDownloadFailedKey);

                    ShowNoticeDialog(
                        RedumpDownloadErrorTitleKey,
                        RuntimeDiagnosticFormatter.SummarizeException(ex));
                }
            }
            finally
            {
                if (!_isClosed)
                {
                    RedumpPanel.DownloadDatabaseButtonView.ClearValue(UIElement.IsEnabledProperty);
                    RedumpPanel.DatabaseDownloadProgressBarView.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch (Exception ex)
        {
            ShowNoticeDialog(
                OperationErrorTitleKey,
                RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
    }

    private void QueueDatabaseStateRefresh()
    {
        if (_isClosed)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(QueueDatabaseStateRefresh), DispatcherPriority.ContextIdle);
            return;
        }

        _databaseStateRefreshCts?.Cancel();
        _databaseStateRefreshCts?.Dispose();
        _databaseStateRefreshCts = CancellationTokenSource.CreateLinkedTokenSource(_windowLifetimeCts.Token);

        int generation = Interlocked.Increment(ref _databaseStateRefreshGeneration);
        CancellationToken cancellationToken = _databaseStateRefreshCts.Token;

        ViewModel.IsDatabaseAvailable = false;
        ViewModel.DatabaseStatusText = ResolveUiText(DatabaseCheckingKey);

        _ = RefreshDatabaseStateAsync(generation, cancellationToken);
    }

    private async Task RefreshDatabaseStateAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            bool isAvailable = await Task.Run(LoadDatabaseAvailability, cancellationToken).ConfigureAwait(true);

            if (_isClosed
                || cancellationToken.IsCancellationRequested
                || generation != Volatile.Read(ref _databaseStateRefreshGeneration))
            {
                return;
            }

            ViewModel.IsDatabaseAvailable = isAvailable;
            ViewModel.DatabaseStatusText = ResolveUiText(isAvailable ? DatabaseAvailableKey : DatabaseMissingKey);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_isClosed
                || cancellationToken.IsCancellationRequested
                || generation != Volatile.Read(ref _databaseStateRefreshGeneration))
            {
                return;
            }

            Logger.Warning(ex, "Failed to refresh Redump SQLite database state from the advanced options window.");

            ViewModel.IsDatabaseAvailable = false;
            ViewModel.DatabaseStatusText = ResolveUiText(DatabaseReadFailedKey);
        }
    }

    private static bool LoadDatabaseAvailability()
    {
        RedumpSqliteManager database = RedumpSqliteManager.Default;
        database.EnsureInitialized();
        return database.HasAnyRows();
    }

    private static void SetPanelVisibilityIfAvailable(FrameworkElement? panel, Visibility visibility)
    {
        if (panel is null)
        {
            return;
        }

        panel.Visibility = visibility;
    }

    private void ShowNoticeDialog(string title, string message)
    {
        string resolvedTitle = ResolveKeyOrText(title);
        string resolvedMessage = ResolveKeyOrText(message);

        if (string.IsNullOrWhiteSpace(resolvedTitle) || string.IsNullOrWhiteSpace(resolvedMessage))
        {
            return;
        }

        var dialog = new RedumpNoticeDialog(resolvedTitle, resolvedMessage)
        {
            Owner = this
        };

        _ = dialog.ShowDialog();
    }

    private bool TryValidatePremiumFeatureChanges(AppSettings pendingSettings)
    {
        ArgumentNullException.ThrowIfNull(pendingSettings);

        if (pendingSettings.EnableDeepIntegrityCheck
            && !RequirePremiumFeature(PremiumFeature.RedumpDeepIntegrity))
        {
            ApplyPremiumRestrictionsToViewModel(showDialog: false);
            return false;
        }

        if (pendingSettings.ApplyStandardNamingBasedOnHash
            && !RequirePremiumFeature(PremiumFeature.StandardNamingSuggestion))
        {
            ApplyPremiumRestrictionsToViewModel(showDialog: false);
            return false;
        }

        if (pendingSettings.EnableRedumpAutoSync
            && !RequirePremiumFeature(PremiumFeature.RedumpDatabaseImport))
        {
            ApplyPremiumRestrictionsToViewModel(showDialog: false);
            return false;
        }

        if (!pendingSettings.SuppressStorageAdvisorDialog
            && !RequirePremiumFeature(PremiumFeature.StorageAdvisor))
        {
            ApplyPremiumRestrictionsToViewModel(showDialog: false);
            return false;
        }

        if (RequiresPostProcessingAutomation(pendingSettings)
            && !RequirePremiumFeature(PremiumFeature.PostProcessingAutomation))
        {
            ApplyPremiumRestrictionsToViewModel(showDialog: false);
            return false;
        }

        if (RequiresPerformanceProfiles(pendingSettings)
            && !RequirePremiumFeature(PremiumFeature.PerformanceProfiles))
        {
            ApplyPremiumRestrictionsToViewModel(showDialog: false);
            return false;
        }

        return true;
    }

    private static bool RequiresPostProcessingAutomation(AppSettings settings) =>
        settings.CopyMatchingSbi
        || settings.EnableAutoM3uGeneration
        || settings.OverwriteExistingM3uPlaylists;

    private static bool RequiresPerformanceProfiles(AppSettings settings) =>
        settings.MaxProcessorCount != 0
        || !string.Equals(settings.CompressionCodecs, "preset:default", StringComparison.Ordinal)
        || settings.HunkSizeBytes != 0
        || settings.IsoCreateCommandOverride != IsoCreateCommandOverride.Auto;

    private void ApplyPremiumRestrictionsToViewModel(bool showDialog)
    {
        ApplyFeatureAccessToViewModel();

        if (!_featureAccessService.CanUseFeature(PremiumFeature.RedumpDeepIntegrity))
        {
            ViewModel.EnableDeepIntegrityCheck = false;
        }

        if (!_featureAccessService.CanUseFeature(PremiumFeature.StandardNamingSuggestion))
        {
            ViewModel.ApplyStandardNamingBasedOnHash = false;
        }

        if (!_featureAccessService.CanUseFeature(PremiumFeature.RedumpDatabaseImport))
        {
            ViewModel.EnableRedumpAutoSync = false;
        }

        if (!_featureAccessService.CanUseFeature(PremiumFeature.StorageAdvisor))
        {
            ViewModel.ShowStorageAdvisorDialog = false;
        }

        if (!_featureAccessService.CanUseFeature(PremiumFeature.PostProcessingAutomation))
        {
            ViewModel.CopyMatchingSbi = false;
            ViewModel.EnableAutoM3uGeneration = false;
            ViewModel.OverwriteExistingM3uPlaylists = false;
        }

        if (!_featureAccessService.CanUseFeature(PremiumFeature.PerformanceProfiles))
        {
            ViewModel.ApplyDefaultEngineSettings();
        }

        _ = showDialog;
    }

    private void ApplyFeatureAccessToViewModel()
    {
        ViewModel.CanUsePerformanceProfiles = _featureAccessService.CanUseFeature(PremiumFeature.PerformanceProfiles);
        ViewModel.CanUsePostProcessingAutomation = _featureAccessService.CanUseFeature(PremiumFeature.PostProcessingAutomation);
        ViewModel.CanUseRedumpDeepIntegrity = _featureAccessService.CanUseFeature(PremiumFeature.RedumpDeepIntegrity);
        ViewModel.CanUseRedumpDatabaseImport = _featureAccessService.CanUseFeature(PremiumFeature.RedumpDatabaseImport);
        ViewModel.CanUseStandardNamingSuggestion = _featureAccessService.CanUseFeature(PremiumFeature.StandardNamingSuggestion);
        ViewModel.CanUseStorageAdvisor = _featureAccessService.CanUseFeature(PremiumFeature.StorageAdvisor);
    }

    private bool RequirePremiumFeature(PremiumFeature feature) =>
        _featureAccessService.CanUseFeature(feature);


    private static string ResolveKeyOrText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        return LooksLikeResourceKey(trimmed)
            ? ResolveUiText(trimmed)
            : trimmed;
    }

    private static string ResolveUiText(string key, params object?[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        string template = ArabicUi.ResolveDisplayString(key);

        if (args.Length == 0)
        {
            return template;
        }

        try
        {
            return ArabicUi.FormatText(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private static string ResolveUiText(string key, IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return ResolveUiText(key);
        }

        return ResolveUiText(key, args.ToArray());
    }

    private static bool LooksLikeResourceKey(string value) =>
        value.StartsWith("Loc", StringComparison.Ordinal)
        && value.IndexOfAny([' ', '\t', ':', '.', ',', ';']) < 0;
}
