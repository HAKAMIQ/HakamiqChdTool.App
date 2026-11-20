using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services.Power;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace HakamiqChdTool.App.Views;

/// <summary>
/// Counts down before the after-run sleep or shutdown. Cancel is the default button; the dialog returns
/// true only when the countdown ends or the user picks Now.
/// </summary>
public partial class AfterRunCountdownDialog : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly AfterRunAction _action;
    private int _secondsLeft;

    internal AfterRunCountdownDialog(AfterRunAction action, TimeSpan countdown)
    {
        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
        AppLanguageService.ApplyToWindow(this);
        _action = action;
        _secondsLeft = Math.Max(1, (int)countdown.TotalSeconds);
        Title = ArabicUi.Get(TitleKey(action));
        TitleText.Text = Title;
        UpdateMessage();
        _timer.Tick += (_, _) =>
        {
            _secondsLeft--;
            if (_secondsLeft <= 0)
            {
                Finish(true);
                return;
            }

            UpdateMessage();
        };
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    internal static string TitleKey(AfterRunAction action) =>
        action == AfterRunAction.Sleep ? "LocAfterRun_SleepTitle" : "LocAfterRun_ShutdownTitle";

    internal static string MessageKey(AfterRunAction action) =>
        action == AfterRunAction.Sleep ? "LocAfterRun_SleepMessage" : "LocAfterRun_ShutdownMessage";

    private void UpdateMessage() =>
        MessageText.Text = ArabicUi.Get(MessageKey(_action))
            .Replace("{0}", "‎" + _secondsLeft.ToString(CultureInfo.InvariantCulture) + "‎", StringComparison.Ordinal);

    private void NowButton_Click(object sender, RoutedEventArgs e) => Finish(true);

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Finish(false);

    private void Finish(bool proceed)
    {
        _timer.Stop();
        try
        {
            DialogResult = proceed;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }
}
