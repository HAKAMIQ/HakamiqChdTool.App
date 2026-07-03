using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Input;

using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.ViewModels;

namespace HakamiqChdTool.App.Views;

public partial class AboutWindow : Window
{
    private const string MohammedDiscordInviteUrl = "https://discord.gg/xEV5wutKXM";

    public AboutWindow(AboutWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
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
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool TryCreateAllowedDiscordInviteUri(
        string url,
        [NotNullWhen(true)] out Uri? inviteUri)
    {
        inviteUri = null;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? parsedUri))
        {
            return false;
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(parsedUri.Scheme, Uri.UriSchemeHttps))
        {
            return false;
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(parsedUri.Host, "discord.gg"))
        {
            return false;
        }

        string absoluteUri = parsedUri.AbsoluteUri;

            !StringComparer.OrdinalIgnoreCase.Equals(absoluteUri, MohammedDiscordInviteUrl))
        {
            return false;
        }

        inviteUri = parsedUri;
        return true;
    }

    private static void TryOpenExternalUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
