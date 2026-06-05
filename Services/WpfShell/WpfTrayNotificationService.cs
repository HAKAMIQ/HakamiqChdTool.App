using Hardcodet.Wpf.TaskbarNotification;
using System;

namespace HakamiqChdTool.App.Services.WpfShell;

public sealed class WpfTrayNotificationService : ITrayNotificationService
{
    private readonly Action<string, string, BalloonIcon> _showBalloonTip;

    public WpfTrayNotificationService(Action<string, string, BalloonIcon> showBalloonTip)
    {
        _showBalloonTip = showBalloonTip ?? throw new ArgumentNullException(nameof(showBalloonTip));
    }

    public void ShowInfo(string title, string message)
    {
        _showBalloonTip(title, message, BalloonIcon.Info);
    }
}
