using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.Ui.Shell;

internal sealed partial class HqOptionsShell
{
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
