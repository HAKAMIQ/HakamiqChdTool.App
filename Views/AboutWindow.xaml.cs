using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.ViewModels;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Input;

namespace HakamiqChdTool.App.Views;

public partial class AboutWindow : Window
{
    private const string QuantularityDiscordInviteUrl = "https://discord.gg/bside";
    private const string MohammedDiscordInviteUrl = "https://discord.gg/xEV5wutKXM";

    public AboutWindow(AboutWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        AppLanguageService.ApplyToWindow(this);
        DataContext = viewModel;
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
        }
        catch (InvalidOperationException)
        {
            // DragMove can fail if the mouse state changes before WPF starts the drag operation.
        }
    }

    private void ContributorDiscordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url })
        {
            return;
        }

        if (!TryCreateAllowedDiscordInviteUri(url, out Uri? inviteUri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = inviteUri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (InvalidOperationException)
        {
            // The system could not start the default browser.
        }
        catch (Win32Exception)
        {
            // The shell could not open the invite URL.
        }
    }

    private static bool TryCreateAllowedDiscordInviteUri(
        string url,
        [NotNullWhen(true)] out Uri? inviteUri)
    {
        inviteUri = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri))
        {
            return false;
        }

        if (!string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(parsedUri.Host, "discord.gg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(parsedUri.AbsoluteUri, QuantularityDiscordInviteUrl, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parsedUri.AbsoluteUri, MohammedDiscordInviteUrl, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        inviteUri = parsedUri;
        return true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}