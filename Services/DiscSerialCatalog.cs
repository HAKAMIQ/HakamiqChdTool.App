using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Services;

internal enum DiscSerialScanProfile
{
    Metadata,
    Raw
}

internal readonly record struct DiscSerialCatalogResult(
    bool Success,
    string Prefix,
    string Serial,
    string PlatformName,
    string Region);

internal static class DiscSerialCatalog
{
    private const int SerialPrefixLength = 4;

    private static readonly IReadOnlyDictionary<int, DiscSerialPrefixRule> PrefixRules = BuildPrefixRules();

    public static bool TryExtract(
        string? text,
        DiscSerialScanProfile profile,
        string fallbackPlatform,
        bool includeOptionalTail,
        string serialSeparator,
        out DiscSerialCatalogResult result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> span = text.AsSpan();

        for (int index = 0; index <= span.Length - SerialPrefixLength; index++)
        {
            if (!IsSerialBoundaryBefore(span, index))
            {
                continue;
            }

            if (!TryReadRuleAt(span, index, profile, out DiscSerialPrefixRule rule))
            {
                continue;
            }

            int position = index + SerialPrefixLength;
            SkipSerialSeparators(span, ref position);

            if (!TryReadDigits(span, ref position, 3, 5, out string digits))
            {
                continue;
            }

            int afterDigits = position;
            string tail = string.Empty;

            if (includeOptionalTail)
            {
                SkipSerialSeparators(span, ref position);
                if (TryReadExactDigits(span, ref position, 2, out string resolvedTail))
                {
                    tail = resolvedTail;
                }
                else
                {
                    position = afterDigits;
                }
            }

            if (!IsSerialBoundaryAfter(span, position))
            {
                continue;
            }

            string platform = string.IsNullOrWhiteSpace(rule.PlatformOverride)
                ? fallbackPlatform
                : rule.PlatformOverride;

            if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(rule.Region))
            {
                continue;
            }

            string normalizedSerial = rule.Prefix + serialSeparator + digits + tail;
            result = new DiscSerialCatalogResult(
                true,
                rule.Prefix,
                normalizedSerial,
                platform,
                rule.Region);
            return true;
        }

        return false;
    }

    private static bool TryReadRuleAt(
        ReadOnlySpan<char> text,
        int index,
        DiscSerialScanProfile profile,
        out DiscSerialPrefixRule rule)
    {
        rule = default;

        if (index < 0 || index + SerialPrefixLength > text.Length)
        {
            return false;
        }

        int key = BuildPrefixKey(text.Slice(index, SerialPrefixLength));
        if (!PrefixRules.TryGetValue(key, out DiscSerialPrefixRule candidate))
        {
            return false;
        }

        bool allowed = profile switch
        {
            DiscSerialScanProfile.Metadata => candidate.AllowMetadataProbe,
            DiscSerialScanProfile.Raw => candidate.AllowRawProbe,
            _ => false
        };

        if (!allowed)
        {
            return false;
        }

        rule = candidate;
        return true;
    }

    private static IReadOnlyDictionary<int, DiscSerialPrefixRule> BuildPrefixRules()
    {
        var rules = new Dictionary<int, DiscSerialPrefixRule>();

        Add(rules, "SLUS", "USA", string.Empty, metadata: true, raw: true);
        Add(rules, "SCUS", "USA", string.Empty, metadata: true, raw: true);
        Add(rules, "SLES", "Europe", string.Empty, metadata: true, raw: true);
        Add(rules, "SCES", "Europe", string.Empty, metadata: true, raw: true);
        Add(rules, "SLPM", "Japan", string.Empty, metadata: true, raw: true);
        Add(rules, "SLPS", "Japan", string.Empty, metadata: true, raw: true);
        Add(rules, "SLKA", "Korea", string.Empty, metadata: true, raw: true);
        Add(rules, "SCAJ", "Asia", string.Empty, metadata: true, raw: true);
        Add(rules, "PAPX", "Japan", string.Empty, metadata: true, raw: true);
        Add(rules, "PCPX", "Japan", string.Empty, metadata: true, raw: true);
        Add(rules, "ESPM", "Japan", string.Empty, metadata: true, raw: true);
        Add(rules, "SCPS", "Japan", string.Empty, metadata: false, raw: true);

        Add(rules, "ULUS", "USA", "PlayStation Portable", metadata: true, raw: true);
        Add(rules, "UCUS", "USA", "PlayStation Portable", metadata: true, raw: true);
        Add(rules, "ULES", "Europe", "PlayStation Portable", metadata: true, raw: true);
        Add(rules, "UCES", "Europe", "PlayStation Portable", metadata: true, raw: true);
        Add(rules, "ULJM", "Japan", "PlayStation Portable", metadata: true, raw: true);
        Add(rules, "ULJS", "Japan", "PlayStation Portable", metadata: true, raw: true);
        Add(rules, "UCJS", "Japan", "PlayStation Portable", metadata: true, raw: true);
        Add(rules, "UCAS", "Asia", "PlayStation Portable", metadata: true, raw: false);
        Add(rules, "ULKS", "Korea", "PlayStation Portable", metadata: true, raw: false);
        Add(rules, "UCKS", "Korea", "PlayStation Portable", metadata: true, raw: false);

        return rules;
    }

    private static void Add(
        Dictionary<int, DiscSerialPrefixRule> rules,
        string prefix,
        string region,
        string platformOverride,
        bool metadata,
        bool raw)
    {
        rules[BuildPrefixKey(prefix.AsSpan())] = new DiscSerialPrefixRule(
            prefix,
            region,
            platformOverride,
            metadata,
            raw);
    }

    private static int BuildPrefixKey(ReadOnlySpan<char> prefix)
    {
        if (prefix.Length != SerialPrefixLength)
        {
            return -1;
        }

        int key = 0;
        for (int index = 0; index < SerialPrefixLength; index++)
        {
            char value = char.ToUpperInvariant(prefix[index]);
            if (value is < 'A' or > 'Z')
            {
                return -1;
            }

            key = (key << 8) | value;
        }

        return key;
    }

    private static bool TryReadDigits(
        ReadOnlySpan<char> text,
        ref int position,
        int minimumDigits,
        int maximumDigits,
        out string digits)
    {
        digits = string.Empty;
        int start = position;
        int count = 0;

        while (position < text.Length && count < maximumDigits && char.IsDigit(text[position]))
        {
            position++;
            count++;
        }

        if (count < minimumDigits)
        {
            position = start;
            return false;
        }

        if (position < text.Length && char.IsDigit(text[position]))
        {
            position = start;
            return false;
        }

        digits = text.Slice(start, count).ToString();
        return true;
    }

    private static bool TryReadExactDigits(
        ReadOnlySpan<char> text,
        ref int position,
        int count,
        out string digits)
    {
        digits = string.Empty;
        int start = position;

        for (int index = 0; index < count; index++)
        {
            int current = start + index;
            if (current >= text.Length || !char.IsDigit(text[current]))
            {
                position = start;
                return false;
            }
        }

        int end = start + count;
        if (end < text.Length && char.IsDigit(text[end]))
        {
            position = start;
            return false;
        }

        position = end;
        digits = text.Slice(start, count).ToString();
        return true;
    }

    private static void SkipSerialSeparators(ReadOnlySpan<char> text, ref int position)
    {
        while (position < text.Length && IsSerialSeparator(text[position]))
        {
            position++;
        }
    }

    private static bool IsSerialSeparator(char value) =>
        char.IsWhiteSpace(value) || value is '.' or '_' or '-';

    private static bool IsSerialBoundaryBefore(ReadOnlySpan<char> text, int index) =>
        index == 0 || !char.IsLetterOrDigit(text[index - 1]);

    private static bool IsSerialBoundaryAfter(ReadOnlySpan<char> text, int index) =>
        index >= text.Length || !char.IsLetterOrDigit(text[index]);

    private readonly record struct DiscSerialPrefixRule(
        string Prefix,
        string Region,
        string PlatformOverride,
        bool AllowMetadataProbe,
        bool AllowRawProbe);
}