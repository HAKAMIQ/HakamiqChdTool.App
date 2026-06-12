using Hardcodet.Wpf.TaskbarNotification;
using System;

namespace HakamiqChdTool.App.Ui.Shell;

public sealed class TrayNotifier : ITrayNotificationService
{
    private readonly Action<string, string, BalloonIcon> _showBalloonTip;

    public TrayNotifier(Action<string, string, BalloonIcon> showBalloonTip)
    {
        _showBalloonTip = showBalloonTip ?? throw new ArgumentNullException(nameof(showBalloonTip));
    }

    public void ShowInfo(string title, string message)
    {
        _showBalloonTip(title, message, BalloonIcon.Info);
    }
}
