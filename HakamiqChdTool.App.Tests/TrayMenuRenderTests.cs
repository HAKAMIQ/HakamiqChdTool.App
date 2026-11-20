using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HakamiqChdTool.App.Views.Main;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The tray menu is drawn in the theme colour across each row. The default ContextMenu template paints a
    // light icon column that, in Arabic, sat under the text and made it unreadable in the dark theme.
    private static void TestTrayMenuRowsFollowTheTheme()
    {
        List<string> findings = [];
        Exception? failure = null;

        Thread thread = new(() =>
        {
            try
            {
                var app = new global::HakamiqChdTool.App.App
                {
                    IsIntegrationTestHost = true,
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                app.InitializeComponent();

                foreach (string language in new[] { "ar-SA", "en-US" })
                {
                    foreach (string theme in new[] { "Dark", "Light" })
                    {
                        ApplyLayoutTestTheme(app, theme);
                        ApplyLayoutTestLanguage(app, language);

                        var view = new TrayNotifyIconView();
                        ContextMenu menu = view.NotifyIcon.ContextMenu;
                        menu.FlowDirection = language == "ar-SA" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                        menu.ApplyTemplate();
                        menu.Measure(new Size(400, 300));
                        menu.Arrange(new Rect(menu.DesiredSize));
                        menu.UpdateLayout();

                        var bitmap = new RenderTargetBitmap(
                            Math.Max(1, (int)Math.Ceiling(menu.ActualWidth)),
                            Math.Max(1, (int)Math.Ceiling(menu.ActualHeight)),
                            96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(menu);

                        Color menuBackground = ((SolidColorBrush)app.FindResource("Brush.Layer.Card")).Color;
                        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
                        {
                            // Both ends of the row, outside the text, must show the menu colour.
                            foreach (double x in new[] { 6d, item.ActualWidth - 6 })
                            {
                                Point probe = item.TranslatePoint(new Point(x, 3), menu);
                                Color pixel = ReadPixel(bitmap, (int)probe.X, (int)probe.Y);
                                if (ColourDistance(pixel, menuBackground) > 40)
                                {
                                    findings.Add($"{language}/{theme}: row pixel #{pixel.R:X2}{pixel.G:X2}{pixel.B:X2} at {probe.X:F0},{probe.Y:F0} is not the menu colour #{menuBackground.R:X2}{menuBackground.G:X2}{menuBackground.B:X2}.");
                                }
                            }
                        }

                        view.Dispose();
                    }
                }

                app.Shutdown();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        AssertTrue(thread.Join(TimeSpan.FromSeconds(60)), "The tray menu render test finished.");
        AssertTrue(failure is null, "The tray menu render test threw: " + failure);

        foreach (string finding in findings)
        {
            Console.WriteLine("[TRAY] " + finding);
        }

        AssertEqual(0, findings.Count, "Tray menu rows follow the theme colour in both languages and themes.");
    }

    private static Color ReadPixel(BitmapSource bitmap, int x, int y)
    {
        x = Math.Clamp(x, 0, bitmap.PixelWidth - 1);
        y = Math.Clamp(y, 0, bitmap.PixelHeight - 1);
        byte[] pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static double ColourDistance(Color a, Color b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));
}
