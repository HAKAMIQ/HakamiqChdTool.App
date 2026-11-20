using System.Globalization;
using System.Windows;
using HakamiqChdTool.App.Views.Options;

namespace HakamiqChdTool.App.Ui.Shell;

internal sealed partial class HqOptionsShell
{
    /// <summary>
    /// Shows only the option rows whose title, description or section heading contains the text, in every tab. A
    /// section with no match is hidden; if the open tab has no match, the first tab that has one is opened. Empty
    /// text shows everything again. Rows hidden by their own setting (for example Redump rows while Redump is off)
    /// stay hidden: the filter never changes a row's Visibility.
    /// </summary>
    public int FilterOptions(string? query)
    {
        string needle = NormalizeSearchText(query);
        var tabs = new (string Key, FrameworkElement? Panel)[]
        {
            (ProcessingTabKey, _owner.ProcessingPanelView),
            (PathsTabKey, _owner.PathsPanelView),
            (PerformanceTabKey, _owner.PerformancePanelView),
            (WindowsTabKey, _owner.WindowsPanelView),
            (RedumpTabKey, _owner.RedumpPanelView),
            (ExternalToolsTabKey, _owner.ExternalToolsPanelView)
        };

        string activeTab = ActiveTabKey;
        string? firstTabWithMatch = null;
        bool activeTabHasMatch = false;
        int total = 0;

        foreach ((string key, FrameworkElement? panel) in tabs)
        {
            if (panel is null)
            {
                continue;
            }

            int tabMatches = 0;
            foreach (OptSection section in FindLogicalChildren<OptSection>(panel))
            {
                bool headerMatches = needle.Length > 0 && NormalizeSearchText(section.Header).Contains(needle, StringComparison.Ordinal);
                int sectionMatches = 0;
                foreach (OptRow row in FindLogicalChildren<OptRow>(section))
                {
                    bool match = needle.Length == 0
                        || headerMatches
                        || NormalizeSearchText(row.Title).Contains(needle, StringComparison.Ordinal)
                        || NormalizeSearchText(row.Description).Contains(needle, StringComparison.Ordinal);
                    row.IsFilteredOut = !match;
                    sectionMatches += match ? 1 : 0;
                }

                // Rows made from data (the bundled tools list) are not in the logical tree; the heading decides.
                bool generated = section.ItemsSource is not null;
                section.IsFilteredOut = needle.Length > 0 && sectionMatches == 0 && !(generated && headerMatches);
                if (needle.Length > 0 && section.IsCollapsible && !section.IsFilteredOut)
                {
                    section.IsExpanded = true;
                }
                tabMatches += section.IsFilteredOut ? 0 : Math.Max(sectionMatches, generated ? 1 : 0);
            }

            total += tabMatches;
            if (tabMatches > 0)
            {
                firstTabWithMatch ??= key;
                activeTabHasMatch |= string.Equals(key, activeTab, StringComparison.Ordinal);
            }
        }

        if (needle.Length > 0 && !activeTabHasMatch && firstTabWithMatch is not null)
        {
            SelectTab(firstTabWithMatch);
        }

        _owner.ViewModel.SearchHasNoResults = needle.Length > 0 && total == 0;
        return total;
    }

    // Case and Arabic letter forms do not matter: أ إ آ match ا, ة matches ه and ى matches ي.
    internal static string NormalizeSearchText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        foreach (char c in text.Trim().ToLower(CultureInfo.InvariantCulture))
        {
            builder.Append(c switch
            {
                'أ' or 'إ' or 'آ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                '‎' or '‏' or '⁦' or '⁧' or '⁨' or '⁩' => '\0',
                _ => c
            });
        }

        return builder.ToString().Replace("\0", string.Empty, StringComparison.Ordinal);
    }

    private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is not DependencyObject node)
            {
                continue;
            }

            if (node is T match)
            {
                yield return match;
                if (match is OptSection)
                {
                    continue;
                }
            }

            foreach (T nested in FindLogicalChildren<T>(node))
            {
                yield return nested;
            }
        }
    }
}
