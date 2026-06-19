using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Ui.Shell;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views.Options;
using Serilog;

namespace HakamiqChdTool.App.Views;

public sealed class OptionsAppliedEventArgs : EventArgs
{
    public OptionsAppliedEventArgs(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
    }

    public AppSettings Settings { get; }
}

public partial class OptionsWindow : Window
{
    public const string GeneralTabKey = "General";
    public const string PathsTabKey = "Paths";
    public const string RedumpTabKey = "Redump";
    public const string ProcessingTabKey = "Processing";
    public const string ExternalToolsTabKey = "ExternalTools";
    public const string PerformanceTabKey = "Performance";

    private static readonly ILogger Logger = Log.ForContext<OptionsWindow>();
    private HqOptionsShell? _coordinator;

    public OptionsWindow(
        AppSettings currentSettings,
        IAppFeatureService appFeatureService)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        ArgumentNullException.ThrowIfNull(appFeatureService);

        InitializeComponent();
        AppLanguageService.ApplyToWindow(this);
        WindowBackdrop.ApplyDialog(this);

        ResultSettings = currentSettings.Clone();
        ViewModel = new OptionsViewModel();
        DataContext = ViewModel;

        _coordinator = new HqOptionsShell(
            this,
            currentSettings,
            appFeatureService);

        _coordinator.Attach();
        RedumpPanel.DownloadDatabaseRequested += _coordinator.DownloadDatabase;
        RedumpPanel.ImportRedumpDatabaseRequested += _coordinator.ImportRedumpDatabase;
        ExternalToolsPanel.RecheckRequested += _coordinator.RecheckExternalTools;
        ExternalToolsPanel.OpenToolsFolderRequested += _coordinator.OpenExternalToolsFolder;
        ExternalToolsPanel.CopySetupInstructionsRequested += _coordinator.CopyExternalToolsSetupInstructions;
        Loaded += OptionsWindow_Loaded;
        Closed += OptionsWindow_Closed;

        _coordinator.Initialize();
    }

    public OptionsViewModel ViewModel { get; }

    public AppSettings ResultSettings { get; internal set; }

    public string ActiveTabKey => _coordinator?.ActiveTabKey ?? GeneralTabKey;

    public event EventHandler<OptionsAppliedEventArgs>? SettingsApplied;

    internal RadioButton GeneralTabButtonView => GeneralTabButton;

    internal RadioButton PathsTabButtonView => PathsTabButton;

    internal RadioButton RedumpTabButtonView => RedumpTabButton;

    internal RadioButton ProcessingTabButtonView => ProcessingTabButton;

    internal RadioButton ExternalToolsTabButtonView => ExternalToolsTabButton;

    internal RadioButton PerformanceTabButtonView => PerformanceTabButton;

    internal GeneralSettingsView GeneralPanelView => GeneralPanel;

    internal PathsSettingsView PathsPanelView => PathsPanel;

    internal RedumpSettingsView RedumpPanelView => RedumpPanel;

    internal ProcessingSettingsView ProcessingPanelView => ProcessingPanel;

    internal ExternalToolsSettingsView ExternalToolsPanelView => ExternalToolsPanel;

    internal PerformanceSettingsView PerformancePanelView => PerformancePanel;

    internal void NotifySettingsApplied(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SettingsApplied?.Invoke(this, new OptionsAppliedEventArgs(settings));
    }

    public void SelectTab(string? tabKey) => _coordinator?.SelectTab(tabKey);

    private void OptionsWindow_Loaded(object sender, RoutedEventArgs e) => _coordinator?.OnLoaded();

    private void OptionsWindow_Closed(object? sender, EventArgs e)
    {
        Loaded -= OptionsWindow_Loaded;
        Closed -= OptionsWindow_Closed;

        if (_coordinator is null)
        {
            return;
        }

        RedumpPanel.DownloadDatabaseRequested -= _coordinator.DownloadDatabase;
        RedumpPanel.ImportRedumpDatabaseRequested -= _coordinator.ImportRedumpDatabase;
        ExternalToolsPanel.RecheckRequested -= _coordinator.RecheckExternalTools;
        ExternalToolsPanel.OpenToolsFolderRequested -= _coordinator.OpenExternalToolsFolder;
        ExternalToolsPanel.CopySetupInstructionsRequested -= _coordinator.CopyExternalToolsSetupInstructions;
        _coordinator.Dispose();
        _coordinator = null;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException ex)
        {
            Logger.Debug(
                ex,
                "Options window drag was ignored because DragMove was not valid for the current mouse state.");
        }
    }

    private void OnTabButtonChecked(object sender, RoutedEventArgs e) => _coordinator?.OnTabButtonChecked(sender, e);

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e) =>
        _coordinator?.RestoreDefaults(sender, e);

    private void ApplyButton_Click(object sender, RoutedEventArgs e) => _coordinator?.Apply(sender, e);

    private void OkButton_Click(object sender, RoutedEventArgs e) => _coordinator?.Ok(sender, e);

    private void CancelButton_Click(object sender, RoutedEventArgs e) => CloseWithDialogResult(false);

    private void CloseWithDialogResult(bool result)
    {
        try
        {
            DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }
}
