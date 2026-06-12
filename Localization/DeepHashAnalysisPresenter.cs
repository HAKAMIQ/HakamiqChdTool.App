using HakamiqChdTool.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Localization;

public sealed record DeepHashAnalysisView(
    string StatusMessage,
    string DetailTooltip);

public static class DeepHashAnalysisPresenter
{
    private const string TipHashFilesHeaderKey = "LocDeepHash_TipHashFilesHeader";
    private const string TipTotalFilesKey = "LocDeepHash_TipTotalFiles";
    private const string TipTotalSizeKey = "LocDeepHash_TipTotalSize";
    private const string TipFileSizeKey = "LocDeepHash_TipFileSize";
    private const string TipHitResultsHeaderKey = "LocDeepHash_TipHitResultsHeader";
    private const string TipVerifiedHeaderKey = "LocDeepHash_TipVerifiedHeader";
    private const string TipUnmatchedFilesHeaderKey = "LocDeepHash_TipUnmatchedFilesHeader";
    private const string TipPlatformKey = "LocDeepHash_TipPlatform";
    private const string TipStandardNameKey = "LocDeepHash_TipStandardName";
    private const string TipRecordNameKey = "LocDeepHash_TipRecordName";
    private const string TipMatchSourceKey = "LocDeepHash_TipMatchSource";
    private const string TipCrcKey = "LocDeepHash_TipCrc";
    private const string ValueUnavailableKey = "LocValue_Unavailable";
    private const string ByteUnitKey = "LocUnit_Byte";
    private const string ByteCountKey = "LocDeepHash_ByteCount";
    private const string ByteSizeKey = "LocDeepHash_ByteSize";

    public static DeepHashAnalysisView Format(DeepHashAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string status = T(result.StatusMessageKey);
        string detail = BuildDetailTooltip(result);
        return new DeepHashAnalysisView(status, detail);
    }

    private static string BuildDetailTooltip(DeepHashAnalysisResult result)
    {
        List<string> parts = [];

        string hashSummary = BuildHashTooltip(result.HashedFiles);
        if (!string.IsNullOrWhiteSpace(hashSummary))
        {
            parts.Add(hashSummary);
        }

        string detail = FormatDetailMessage(result);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            parts.Add(detail);
        }

        if (result.Matches.Count > 0)
        {
            parts.Add(BuildHitTooltip(result.Matches));
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string FormatDetailMessage(DeepHashAnalysisResult result)
    {
        if (result.DetailTooltipKey == TipVerifiedHeaderKey && result.Matches.Count > 0)
        {
            DeepHashMatch first = result.Matches[0];
            return string.Join(
                Environment.NewLine,
                T(TipVerifiedHeaderKey),
                BidiText.Technical(first.SystemName),
                BidiText.Mixed(first.GameName));
        }

        string message = result.DetailArgs.Count == 0
            ? T(result.DetailTooltipKey)
            : F(result.DetailTooltipKey, [.. result.DetailArgs]);

        if (result.UnmatchedFileNames.Count == 0)
        {
            return message;
        }

        return message
            + Environment.NewLine + Environment.NewLine
            + T(TipUnmatchedFilesHeaderKey)
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                result.UnmatchedFileNames.Select(static name => "• " + BidiText.FileName(name)));
    }

    private static string BuildHashTooltip(IReadOnlyList<DeepHashFileDigest> hashed)
    {
        if (hashed.Count == 0)
        {
            return string.Empty;
        }

        long totalBytes = hashed.Sum(static file => file.SizeBytes);
        List<string> lines =
        [
            T(TipHashFilesHeaderKey),
            F(TipTotalFilesKey, hashed.Count),
            F(TipTotalSizeKey, FormatByteSize(totalBytes)),
            string.Empty
        ];

        foreach (DeepHashFileDigest file in hashed)
        {
            lines.Add("• " + BidiText.FileName(Path.GetFileName(file.Path)));
            lines.Add(F(TipFileSizeKey, FormatByteSize(file.SizeBytes)));
            lines.Add($"  MD5:  {BidiText.Hash(file.Md5)}");
            lines.Add($"  SHA1: {BidiText.Hash(file.Sha1)}");
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    private static string BuildHitTooltip(IReadOnlyList<DeepHashMatch> matches)
    {
        List<string> lines = [T(TipHitResultsHeaderKey), string.Empty];

        foreach (DeepHashMatch match in matches)
        {
            string crc = string.IsNullOrWhiteSpace(match.Crc)
                ? T(ValueUnavailableKey)
                : BidiText.Hash(match.Crc);

            lines.Add("• " + BidiText.FileName(Path.GetFileName(match.FilePath)));
            lines.Add(F(TipPlatformKey, BidiText.Technical(match.SystemName)));
            lines.Add(F(TipStandardNameKey, BidiText.Mixed(match.GameName)));
            lines.Add(F(TipRecordNameKey, BidiText.FileName(match.RomName)));
            lines.Add(F(TipMatchSourceKey, BidiText.Technical(match.MatchSource)));
            lines.Add(F(TipCrcKey, crc));
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    private static string FormatByteSize(long bytes)
    {
        string byteUnit = T(ByteUnitKey);
        string[] units = [byteUnit, "KB", "MB", "GB", "TB"];
        double value = Math.Abs((double)bytes);
        int unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return unitIndex == 0
            ? F(ByteCountKey, bytes, byteUnit)
            : F(ByteSizeKey, value, units[unitIndex], bytes, byteUnit);
    }

    private static string T(string key) => ArabicUi.Get(key);

    private static string F(string key, params object?[] args) =>
        ArabicUi.Format(key, args);
}