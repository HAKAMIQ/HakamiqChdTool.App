using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Services;

internal static class ChdProgressPolicy
{
    public static bool ShouldParseRawPercent(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || string.IsNullOrWhiteSpace(arguments[0]))
        {
            return false;
        }

        return ShouldParseRawPercent(arguments[0]);
    }

    public static bool ShouldParseRawPercent(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        // chdman extractcd can print values above 100% on some CD layouts.
        // For extraction, progress is derived from output growth against CHD logical size.
        return !command.StartsWith("extract", StringComparison.OrdinalIgnoreCase);
    }
}
