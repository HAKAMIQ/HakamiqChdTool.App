using System.IO;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Services;

public static class ArchiveNamingRuleValidator
{
    private static readonly Regex Pattern = new(
        @"^[^_]+_[A-Z]{2,3}_\d{4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValid(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string? name = Path.GetFileNameWithoutExtension(fileName);
        return !string.IsNullOrWhiteSpace(name) && Pattern.IsMatch(name);
    }
}