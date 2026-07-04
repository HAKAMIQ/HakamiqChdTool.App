using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HakamiqChdTool.App.Services.RedumpCatalog;

namespace HakamiqChdTool.App.Services;

internal static class ConsoleAlias
{
    private const string RedumpAliasReason = "RedumpCatalogAlias";

    private static readonly Lazy<IReadOnlyList<ConsoleAliasRule>> Rules = new(
        BuildRules,
        isThreadSafe: true);

    public static bool TryDetect(string path, out ConsoleIdResult result)
    {
        result = ConsoleIdResultFactory.Unknown(path);

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string normalizedPath = Normalize(path);
        ConsoleAliasMatch? bestMatch = null;

        foreach (ConsoleAliasRule rule in Rules.Value)
        {
            foreach (string alias in rule.Aliases)
            {
                if (!ContainsAlias(normalizedPath, alias))
                {
                    continue;
                }

                var match = new ConsoleAliasMatch(
                    rule.PlatformName,
                    rule.Reason,
                    rule.ConfidenceScore,
                    alias.Length);

                if (bestMatch is null || IsBetter(match, bestMatch))
                {
                    bestMatch = match;
                }
            }
        }

        if (bestMatch is null)
        {
            return false;
        }

        result = new ConsoleIdResult(
            path,
            bestMatch.PlatformName,
            bestMatch.Reason,
            bestMatch.ConfidenceScore);

        return result.IsIdentified;
    }

    private static IReadOnlyList<ConsoleAliasRule> BuildRules()
    {
        var rules = new List<ConsoleAliasRule>();

        foreach (RedumpSystemCatalogEntry entry in RedumpSystemCatalog.CurrentRedumpSystems)
        {
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddAlias(aliases, entry.EnglishName);
            AddAlias(aliases, RemoveKnownManufacturerPrefix(entry.EnglishName));

            if (ShouldUseKeyAlias(entry.Key))
            {
                AddAlias(aliases, entry.Key);
            }

            AddCommonAliases(entry.Key, aliases);

            string[] normalizedAliases =
            [
                .. aliases
                    .Select(NormalizeAlias)
                    .Where(static alias => alias.Length >= 3)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(static alias => alias.Length)
                    .ThenBy(static alias => alias, StringComparer.OrdinalIgnoreCase)
            ];

            if (normalizedAliases.Length == 0)
            {
                continue;
            }

            rules.Add(new ConsoleAliasRule(
                entry.EnglishName,
                RedumpAliasReason,
                82,
                normalizedAliases));
        }

        return rules
            .OrderBy(static rule => rule.PlatformName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsBetter(ConsoleAliasMatch candidate, ConsoleAliasMatch current)
    {
        if (candidate.AliasLength != current.AliasLength)
        {
            return candidate.AliasLength > current.AliasLength;
        }

        if (candidate.ConfidenceScore != current.ConfidenceScore)
        {
            return candidate.ConfidenceScore > current.ConfidenceScore;
        }

        return string.Compare(
            candidate.PlatformName,
            current.PlatformName,
            StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static bool ContainsAlias(string normalizedPath, string normalizedAlias)
    {
        return normalizedPath.Contains(
            " " + normalizedAlias + " ",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUseKeyAlias(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        string value = key.Trim();

        return value.Length >= 3
            && !value.Equals("cdi", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("pc", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddCommonAliases(string key, HashSet<string> aliases)
    {
        switch (key.Trim().ToLowerInvariant())
        {
            case "psx":
                AddMany(aliases, "psx", "ps1", "playstation", "playstation 1", "sony ps1", "sony playstation 1");
                break;

            case "ps2":
                AddMany(aliases, "ps2", "playstation 2", "sony ps2", "sony playstation 2");
                break;

            case "ps4":
                AddMany(aliases, "ps4", "playstation 4", "sony ps4", "sony playstation 4");
                break;

            case "ps5":
                AddMany(aliases, "ps5", "playstation 5", "sony ps5", "sony playstation 5");
                break;

            case "psp":
                AddMany(aliases, "psp", "sony psp", "playstation portable");
                break;

            case "mcd":
                AddMany(aliases, "mega cd", "sega mega cd", "sega cd", "mega-cd");
                break;

            case "ss":
                AddMany(aliases, "saturn", "sega saturn");
                break;

            case "dc":
                AddMany(aliases, "dreamcast", "sega dreamcast");
                break;

            case "naomi":
                AddMany(aliases, "naomi", "sega naomi");
                break;

            case "naomi2":
                AddMany(aliases, "naomi 2", "naomi2", "sega naomi 2", "sega naomi2");
                break;

            case "chihiro":
                AddMany(aliases, "chihiro", "sega chihiro");
                break;

            case "trf":
                AddMany(aliases, "triforce", "namco sega nintendo triforce");
                break;

            case "gc":
                AddMany(aliases, "gamecube", "game cube", "gcn", "ngc", "nintendo gamecube");
                break;

            case "wii":
                AddMany(aliases, "wii", "nintendo wii");
                break;

            case "wiiu":
                AddMany(aliases, "wii u", "wiiu", "nintendo wii u", "nintendo wiiu");
                break;

            case "xbox":
                AddMany(aliases, "xbox", "microsoft xbox", "original xbox");
                break;

            case "xbox360":
                AddMany(aliases, "xbox 360", "xbox360", "x360", "microsoft xbox 360");
                break;

            case "xboxone":
                AddMany(aliases, "xbox one", "xboxone", "microsoft xbox one");
                break;

            case "xboxsx":
                AddMany(aliases, "xbox series x", "xbox series", "microsoft xbox series x");
                break;

            case "pc":
                AddMany(aliases, "ibm pc compatible", "pc compatible", "windows pc");
                break;

            case "pce":
                AddMany(aliases, "pc engine", "pc engine cd", "turbografx cd", "turbo grafx cd", "nec pc engine");
                break;

            case "pc-fx":
                AddMany(aliases, "pc fx", "pc-fx", "pc fxga", "pc-fxga", "nec pc fx");
                break;

            case "ngcd":
                AddMany(aliases, "neo geo cd", "neogeo cd");
                break;

            case "3do":
                AddMany(aliases, "3do", "panasonic 3do");
                break;

            case "cdi":
                AddMany(aliases, "philips cd i", "philips cdi", "cd i");
                break;

            case "fmt":
                AddMany(aliases, "fm towns", "fujitsu fm towns");
                break;

            case "x68k":
                AddMany(aliases, "x68000", "sharp x68000");
                break;

            case "bd-video":
                AddMany(aliases, "bd video", "blu ray video", "blu-ray video");
                break;

            case "dvd-video":
                AddMany(aliases, "dvd video");
                break;

            case "audio-cd":
                AddMany(aliases, "audio cd");
                break;

            case "vcd":
                AddMany(aliases, "video cd", "vcd");
                break;
        }
    }

    private static void AddMany(HashSet<string> aliases, params string[] values)
    {
        foreach (string value in values)
        {
            AddAlias(aliases, value);
        }
    }

    private static void AddAlias(HashSet<string> aliases, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        aliases.Add(value.Trim());
    }

    private static string RemoveKnownManufacturerPrefix(string value)
    {
        string result = value.Trim();

        string[] prefixes =
        [
            "Sony ",
            "Sega ",
            "Nintendo ",
            "Microsoft ",
            "NEC ",
            "Panasonic ",
            "Philips ",
            "Fujitsu ",
            "Sharp "
        ];

        foreach (string prefix in prefixes)
        {
            if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return result[prefix.Length..].Trim();
            }
        }

        return result;
    }

    private static string NormalizeAlias(string value)
    {
        return Normalize(value).Trim();
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder((value?.Length ?? 0) + 2);
        builder.Append(' ');

        bool previousWasSpace = true;

        foreach (char character in (value ?? string.Empty).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSpace = false;
                continue;
            }

            if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        if (!previousWasSpace)
        {
            builder.Append(' ');
        }

        return builder.ToString();
    }

    private sealed record ConsoleAliasRule(
        string PlatformName,
        string Reason,
        int ConfidenceScore,
        IReadOnlyList<string> Aliases);

    private sealed record ConsoleAliasMatch(
        string PlatformName,
        string Reason,
        int ConfidenceScore,
        int AliasLength);
}
