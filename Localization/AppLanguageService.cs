using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using AppHost = System.Windows.Application;

namespace HakamiqChdTool.App.Localization;

public sealed class AppLanguageService
{
    public const string ArabicLanguageName = "ar-SA";
    public const string EnglishLanguageName = "en-US";

    private const string ArabicDictionaryPath = "Resources/ArabicStrings.xaml";
    private const string EnglishDictionaryPath = "Resources/EnglishStrings.xaml";
    private const string AppFlowDirectionResourceKey = "App.FlowDirection";
    private const string AppXmlLanguageResourceKey = "App.XmlLanguage";
    private const string AppCaptionButtonsFlowDirectionResourceKey = "App.CaptionButtons.FlowDirection";

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<AppLanguageService>();
    private static readonly Lazy<AppLanguageService> LazyInstance = new(() => new AppLanguageService());

    private AppLanguageService()
    {
    }

    public static AppLanguageService Instance => LazyInstance.Value;

    public string CurrentLanguageName { get; private set; } = ArabicLanguageName;

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo(ArabicLanguageName);

    public FlowDirection CurrentFlowDirection =>
        IsArabic(CurrentLanguageName) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public XmlLanguage CurrentXmlLanguage => XmlLanguage.GetLanguage(CurrentCulture.IetfLanguageTag);

    public event EventHandler? LanguageChanged;

    public void Initialize(string? languageName)
    {
        ApplyLanguage(languageName, raiseChanged: false);
    }

    public void SetLanguage(string? languageName)
    {
        ApplyLanguage(languageName, raiseChanged: true);
    }

    public static void ApplyToWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.SetResourceReference(FrameworkElement.FlowDirectionProperty, AppFlowDirectionResourceKey);
        window.SetResourceReference(FrameworkElement.LanguageProperty, AppXmlLanguageResourceKey);
    }

    public string ToggleLanguage()
    {
        string next = IsArabic(CurrentLanguageName) ? EnglishLanguageName : ArabicLanguageName;
        SetLanguage(next);
        return CurrentLanguageName;
    }

    public static bool IsSupportedLanguage(string? languageName)
    {
        if (string.IsNullOrWhiteSpace(languageName))
        {
            return false;
        }

        string value = languageName.Trim();

        return value.Equals(ArabicLanguageName, StringComparison.OrdinalIgnoreCase)
            || value.Equals(EnglishLanguageName, StringComparison.OrdinalIgnoreCase)
            || value.Equals("ar", StringComparison.OrdinalIgnoreCase)
            || value.Equals("en", StringComparison.OrdinalIgnoreCase)
            || value.Equals("arabic", StringComparison.OrdinalIgnoreCase)
            || value.Equals("english", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeLanguageName(string? languageName)
    {
        if (string.IsNullOrWhiteSpace(languageName))
        {
            return ArabicLanguageName;
        }

        string value = languageName.Trim();

        if (value.Equals(EnglishLanguageName, StringComparison.OrdinalIgnoreCase)
            || value.Equals("en", StringComparison.OrdinalIgnoreCase)
            || value.Equals("english", StringComparison.OrdinalIgnoreCase))
        {
            return EnglishLanguageName;
        }

        if (value.Equals(ArabicLanguageName, StringComparison.OrdinalIgnoreCase)
            || value.Equals("ar", StringComparison.OrdinalIgnoreCase)
            || value.Equals("arabic", StringComparison.OrdinalIgnoreCase))
        {
            return ArabicLanguageName;
        }

        return ArabicLanguageName;
    }

    public static bool IsRightToLeftLanguage(string? languageName)
    {
        return IsArabic(languageName);
    }

    private void ApplyLanguage(string? languageName, bool raiseChanged)
    {
        string normalized = NormalizeLanguageName(languageName);

        AppHost? app = AppHost.Current;
        if (app is null)
        {
            ApplyCultureState(normalized);
            return;
        }

        Dispatcher dispatcher = app.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            ApplyCultureState(normalized);
            return;
        }

        if (dispatcher.CheckAccess())
        {
            ApplyLanguageOnDispatcher(app, normalized, raiseChanged);
            return;
        }

        try
        {
            dispatcher.Invoke(
                () => ApplyLanguageOnDispatcher(app, normalized, raiseChanged),
                DispatcherPriority.Send);
        }
        catch (TaskCanceledException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            ApplyCultureState(normalized);
        }
        catch (InvalidOperationException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            ApplyCultureState(normalized);
        }
    }

    private void ApplyLanguageOnDispatcher(AppHost app, string languageName, bool raiseChanged)
    {
        string requestedLanguage = NormalizeLanguageName(languageName);

        try
        {
            ApplyLanguageDictionary(app, requestedLanguage, raiseChanged);
        }
        catch (Exception ex) when (!IsArabic(requestedLanguage))
        {
            Logger.Warning(ex, "English language dictionary could not be loaded. Falling back to Arabic.");
            ApplyLanguageDictionary(app, ArabicLanguageName, raiseChanged);
        }
    }

    private void ApplyLanguageDictionary(AppHost app, string languageName, bool raiseChanged)
    {
        string normalized = NormalizeLanguageName(languageName);
        ResourceDictionary dictionary = CreateLanguageDictionary(normalized);

        RemoveExistingLanguageDictionaries(app.Resources.MergedDictionaries);
        InsertLanguageDictionary(app.Resources.MergedDictionaries, dictionary);

        ApplyCultureState(normalized);
        ApplyApplicationLanguageResources(app);
        ApplyLanguageToOpenWindows(app);

        if (raiseChanged)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyCultureState(string languageName)
    {
        string normalized = NormalizeLanguageName(languageName);
        CultureInfo culture = CultureInfo.GetCultureInfo(normalized);

        CurrentLanguageName = normalized;
        CurrentCulture = culture;

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private void ApplyApplicationLanguageResources(AppHost app)
    {
        app.Resources[AppFlowDirectionResourceKey] = CurrentFlowDirection;
        app.Resources[AppXmlLanguageResourceKey] = CurrentXmlLanguage;
        app.Resources[AppCaptionButtonsFlowDirectionResourceKey] = FlowDirection.RightToLeft;
    }

    private static void ApplyLanguageToOpenWindows(AppHost app)
    {
        foreach (Window window in app.Windows.OfType<Window>())
        {
            ApplyToWindow(window);
            window.InvalidateProperty(FrameworkElement.FlowDirectionProperty);
            window.InvalidateProperty(FrameworkElement.LanguageProperty);
        }
    }

    private static ResourceDictionary CreateLanguageDictionary(string languageName)
    {
        string path = IsArabic(languageName) ? ArabicDictionaryPath : EnglishDictionaryPath;

        return new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{path}", UriKind.Absolute)
        };
    }

    private static void RemoveExistingLanguageDictionaries(Collection<ResourceDictionary> dictionaries)
    {
        for (int i = dictionaries.Count - 1; i >= 0; i--)
        {
            string source = NormalizeSource(dictionaries[i].Source);

            if (source.EndsWith(ArabicDictionaryPath, StringComparison.OrdinalIgnoreCase)
                || source.EndsWith(EnglishDictionaryPath, StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }
    }

    private static void InsertLanguageDictionary(
        Collection<ResourceDictionary> dictionaries,
        ResourceDictionary dictionary)
    {
        dictionaries.Add(dictionary);
    }

    private static string NormalizeSource(Uri? source)
    {
        return (source?.OriginalString ?? string.Empty).Replace('\\', '/');
    }

    private static bool IsArabic(string? languageName)
    {
        return NormalizeLanguageName(languageName)
            .Equals(ArabicLanguageName, StringComparison.OrdinalIgnoreCase);
    }
}