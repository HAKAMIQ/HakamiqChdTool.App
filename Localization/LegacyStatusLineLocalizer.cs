using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Localization;

internal static partial class LegacyStatusLineLocalizer
{
    private const int RegexTimeoutMilliseconds = 250;

    private static readonly Dictionary<string, string> KnownStatusLineToResourceKey = new(StringComparer.Ordinal)
    {
        ["Ready for processing."] = "LocUi_ReadyForProcessing",
        ["Extracting first supported file from archive."] = "LocStatus_ExtractingFromArchive",
        ["Extraction complete; re-detected inner file type."] = "LocStatus_ExtractionCompleteRedetected",
        ["File type is not supported at this stage."] = "LocStatus_FileTypeUnsupportedStage",
        ["نوع الملف غير مدعوم في هذه المرحلة."] = "LocStatus_FileTypeUnsupportedStage",
        ["Operation cancelled by user."] = "LocStatus_UserCancelled",
        ["تم إلغاء العملية."] = "LocStatus_UserCancelled",
        ["تم إلغاء عملية التحقق."] = "LocStatus_UserCancelled",
        ["Converting source to CHD."] = "LocStatus_ConvertingSource",
        ["Conversion finished; verifying CHD integrity."] = "LocStatus_ConversionFinishedVerifying",
        ["Conversion finished; verifying the newly created CHD with chdman verify."] = "LocStatus_ConversionFinishedVerifyingNewChd",
        ["Conversion finished; verifying the newly created CHD."] = "LocStatus_ConversionFinishedVerifyingNewChd",
        ["اكتمل التحويل، جاري التحقق من ملف CHD الناتج."] = "LocStatus_ConversionFinishedVerifyingNewChd",
        ["Reading CHD media type before extraction."] = "LocStatus_ReadingChdMediaType",
        ["جاري قراءة نوع وسائط CHD قبل الاستخراج."] = "LocStatus_ReadingChdMediaType",
        ["Reading CHD metadata."] = "LocStatus_ReadingChdMetadata",
        ["جاري قراءة بيانات CHD."] = "LocStatus_ReadingChdMetadata",
        ["Extracting CHD to CUE + BIN."] = "LocStatus_ExtractingChdCueBin",
        ["Extracting CD CHD to CUE + BIN."] = "LocStatus_ExtractingChdCueBin",
        ["Extracting CHD to ISO."] = "LocStatus_ExtractingChdIso",
        ["Extracting DVD CHD to ISO."] = "LocStatus_ExtractingChdIso",
        ["Extracting CHD to raw disk image."] = "LocStatus_ExtractingChdRaw",
        ["Extracting Raw CHD to RAW."] = "LocStatus_ExtractingChdRaw",
        ["Extracting HD CHD to IMG."] = "LocStatus_ExtractingChdImg",
        ["Could not determine CHD media type; use Verify CHD or inspect file information."] = "LocStatus_UnknownChdMediaType",
        ["File type is not supported for the selected operation."] = "LocStatus_UnsupportedForSelectedOperation",
        ["Extraction done; verifying source CHD."] = "LocStatus_ExtractionDoneVerifying",
        ["Extraction done; verifying the source CHD with chdman verify."] = "LocStatus_ExtractionDoneVerifyingSourceChd",
        ["Extraction done; verifying the source CHD."] = "LocStatus_ExtractionDoneVerifyingSourceChd",
        ["اكتمل الاستخراج، جاري التحقق من ملف CHD المصدر."] = "LocStatus_ExtractionDoneVerifyingSourceChd",
        ["Extraction done; verifying produced files."] = "LocStatus_ExtractionDoneVerifyingArtifacts",
        ["Archive is password-protected; processing stopped."] = "LocStatus_ArchivePasswordProtected",
        ["Finalizing CHD output file."] = "LocStatus_FinalizingChdOutput",
        ["جاري إنهاء كتابة ملف CHD الناتج."] = "LocStatus_FinalizingChdOutput",
        ["جاري حفظ ملف CHD الناتج."] = "LocStatus_FinalizingChdOutput",
        ["Finalizing extracted output file."] = "LocStatus_FinalizingExtractedOutput",
        ["جاري إنهاء كتابة ملف الاستخراج."] = "LocStatus_FinalizingExtractedOutput",
        ["Finalizing verified file location."] = "LocStatus_FinalizingVerifiedFile",
        ["Validating output file."] = "LocStatus_ValidatingOutputFile",
        ["Cleaning temporary files."] = "LocStatus_CleaningTempFiles",
        ["Converted to CHD successfully."] = "LocStatus_ConversionCompletedSuccess",
        ["تم التحويل إلى CHD بنجاح."] = "LocStatus_ConversionCompletedSuccess",
        ["Archive converted to CHD successfully."] = "LocStatus_ArchiveConversionCompletedSuccess",
        ["تم تحويل الأرشيف إلى CHD بنجاح."] = "LocStatus_ArchiveConversionCompletedSuccess",
        ["تم التحقق من ملف CHD بنجاح. الحالة: سليم. الأداة: chdman verify. النتيجة: لم يتم اكتشاف أخطاء في الملف."] = "LocStatus_VerifyCompletedSuccess",
        ["تم التحقق من ملف CHD بنجاح. لم يتم اكتشاف أخطاء."] = "LocStatus_VerifyCompletedSuccess",
        ["Extracted CHD to CUE + BIN successfully."] = "LocStatus_ExtractCompletedCueBin",
        ["تم استخراج CHD إلى CUE + BIN بنجاح."] = "LocStatus_ExtractCompletedCueBin",
        ["Extracted CHD to ISO successfully."] = "LocStatus_ExtractCompletedIso",
        ["تم استخراج CHD إلى ISO بنجاح."] = "LocStatus_ExtractCompletedIso",
        ["Extracted CHD to IMG successfully."] = "LocStatus_ExtractCompletedImg",
        ["تم استخراج CHD إلى IMG بنجاح."] = "LocStatus_ExtractCompletedImg",
        ["Extracted CHD to RAW successfully."] = "LocStatus_ExtractCompletedRaw",
        ["تم استخراج CHD إلى RAW بنجاح."] = "LocStatus_ExtractCompletedRaw",
        ["Extraction completed successfully."] = "LocStatus_ExtractionCompletedSuccess",
        ["اكتمل الاستخراج بنجاح."] = "LocStatus_ExtractionCompletedSuccess",
        ["Moving extracted output file to final location."] = "LocStatus_MovingExtractedOutputFile",
        ["جاري نقل ملف الاستخراج إلى الموقع النهائي."] = "LocStatus_MovingExtractedOutputFile",
        ["BIN/CUE rescue preparation failed."] = "LocStatus_BinCueRescuePreparationFailed",
        ["تعذر تجهيز BIN/CUE للتحويل."] = "LocStatus_BinCueRescuePreparationFailed",
        ["Workflow preparation failed."] = "LocStatus_WorkflowPreparationFailed",
        ["فشل تجهيز الملف قبل بدء المعالجة."] = "LocStatus_WorkflowPreparationFailed",
        ["Could not bind queue item to UI row."] = "LocStatus_QueueItemBindFailed",
        ["BIN input does not exist."] = "LocStatus_BinInputDoesNotExist",
        ["Adjacent CUE selected by rescue planner does not exist."] = "LocStatus_AdjacentCueDoesNotExist",
        ["Generated rescue CUE path does not exist."] = "LocStatus_GeneratedCueDoesNotExist",
        ["Rescue CUE preparation failed."] = "LocStatus_RescueCuePreparationFailed",
    };

    public static bool TryResolveToResourceKey(string text, out string resourceKey)
    {
        resourceKey = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text.Trim();

        if (IsLocalizationResourceKey(normalized))
        {
            resourceKey = normalized;
            return true;
        }

        if (KnownStatusLineToResourceKey.TryGetValue(normalized, out string? mappedResourceKey))
        {
            resourceKey = mappedResourceKey;
            return true;
        }

        if (normalized.StartsWith("Verifying CHD container integrity with chdman verify.", StringComparison.OrdinalIgnoreCase))
        {
            resourceKey = "LocStatus_VerifyingChdContainer";
            return true;
        }

        if (IsRegexMatch(MediaTypeExtractingLineRegex(), normalized))
        {
            resourceKey = "LocStatus_MediaTypeExtractingArabic";
            return true;
        }

        if (IsRegexMatch(CreatedLineRegex(), normalized))
        {
            resourceKey = "LocStatus_CreatedFileArabic";
            return true;
        }

        if (IsRegexMatch(DetectedMediaLineRegex(), normalized))
        {
            resourceKey = "LocStatus_DetectedMediaArabic";
            return true;
        }

        return TryMapKnownStatusLineToResourceKey(normalized, out resourceKey);
    }

    public static string ExtractTechnicalTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string normalized = text.Trim();

        if (IsLocalizationResourceKey(normalized))
        {
            return string.Empty;
        }

        if (TryMatch(MediaTypeExtractingLineRegex(), normalized, out Match mediaMatch))
        {
            return $"{mediaMatch.Groups["mt"].Value.Trim()} · chdman";
        }

        if (TryMatch(CreatedLineRegex(), normalized, out Match createdMatch))
        {
            return createdMatch.Groups["f"].Value.Trim();
        }

        if (TryMatch(DetectedMediaLineRegex(), normalized, out Match detectedMatch))
        {
            return detectedMatch.Groups["mt"].Value.Trim();
        }

        const string VerifiedAndMovedPrefix = "Verified and moved to final path:";
        if (normalized.StartsWith(VerifiedAndMovedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalized[VerifiedAndMovedPrefix.Length..].Trim();
        }

        const string VerifiedSuccessfullyPrefix = "Verified successfully:";
        if (normalized.StartsWith(VerifiedSuccessfullyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalized[VerifiedSuccessfullyPrefix.Length..].Trim();
        }

        string technical = ExtractLatinTechnicalFromStatus(normalized);
        if (technical.Length > 0)
        {
            return technical;
        }

        return string.Empty;
    }

    public static string StripEmbeddedTechnicalSuffix(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        try
        {
            string cleaned = ChdmanVerifyEnglishSuffixRegex().Replace(text, string.Empty);
            cleaned = ChdmanVerifyArabicSuffixRegex().Replace(cleaned, string.Empty);

            return CollapsedWhitespaceRegex().Replace(cleaned, " ").Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return text.Trim();
        }
    }

    public static bool IsKnownFinalizingOrCleanupDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return false;
        }

        string normalized = detail.Trim();

        if (normalized is "LocConversion_Finalizing"
            or "LocStatus_FinalizingChdOutput"
            or "LocStatus_FinalizingExtractedOutput"
            or "LocStatus_FinalizingVerifiedFile"
            or "LocStatus_ValidatingOutputFile"
            or "LocStatus_CleaningTempFiles")
        {
            return true;
        }

        return normalized.Contains("Finalizing CHD output file", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Finalizing extracted output file", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Finalizing verified file location", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Validating output file", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Cleaning temporary files", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("جاري إنهاء", StringComparison.Ordinal)
            || normalized.Contains("جاري حفظ", StringComparison.Ordinal)
            || normalized.Contains("جاري نقل ملف الاستخراج", StringComparison.Ordinal);
    }

    public static bool IsKnownVerificationCleanupDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return false;
        }

        string normalized = detail.Trim();
        return normalized is "LocStatus_FinalizingVerifiedFile" or "LocStatus_CleaningTempFiles"
            || normalized.Contains("moved to final", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Cleanup", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryMapKnownStatusLineToResourceKey(string text, out string resourceKey)
    {
        resourceKey = string.Empty;

        if (string.Equals(text, "تم الاستخراج بنجاح.", StringComparison.Ordinal)
            || string.Equals(text, "تم التحويل بنجاح.", StringComparison.Ordinal))
        {
            resourceKey = "LocStatus_ProcessingCompletedSuccess";
            return true;
        }

        if (text.StartsWith("Verified and moved to final path:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Verified successfully:", StringComparison.OrdinalIgnoreCase)
            || (text.StartsWith("Created:", StringComparison.OrdinalIgnoreCase) && text.Length > "Created:".Length))
        {
            resourceKey = "LocStatus_ProcessingCompletedSuccess";
            return true;
        }

        if (text.StartsWith("CHD is valid; final path", StringComparison.OrdinalIgnoreCase))
        {
            resourceKey = "LocStatus_ChdValidSkipReplace";
            return true;
        }

        return false;
    }

    private static string ExtractLatinTechnicalFromStatus(string text)
    {
        MatchCollection matches;
        try
        {
            matches = LatinTechnicalSnippetsRegex().Matches(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return string.Empty;
        }

        if (matches.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (Match match in matches)
        {
            string part = match.Value.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(" · ");
            }

            builder.Append(part);
        }

        return builder.ToString();
    }

    private static bool IsLocalizationResourceKey(string value) =>
        value.StartsWith("Loc", StringComparison.Ordinal);

    private static bool IsRegexMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool TryMatch(Regex regex, string input, out Match match)
    {
        try
        {
            match = regex.Match(input);
            return match.Success;
        }
        catch (RegexMatchTimeoutException)
        {
            match = Match.Empty;
            return false;
        }
    }

    [GeneratedRegex(@"\b(?:Media\s+type|extractcd|extractdvd|extracthd|extractraw|burn(?:ing)?|CHD|ISO|BIN|CUE|GDI|GD-ROM|CD-ROM|DVD-ROM|HD-ROM|RAW|chdman|NTFS|Wii\s*U)\b|\.(?:cue|iso|gdi|bin|img|raw|chd)\b|\d{1,3}\s*%|[A-Za-z]:\\[^\r\n]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex LatinTechnicalSnippetsRegex();

    [GeneratedRegex(@"^Media\s+type:\s*(?<mt>[^—]+)\s*—\s*extracting\s+via\s+chdman\.\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex MediaTypeExtractingLineRegex();

    [GeneratedRegex(@"^Created:\s*(?<f>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex CreatedLineRegex();

    [GeneratedRegex(@"^Detected\s+media\s+type:\s*(?<mt>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex DetectedMediaLineRegex();

    [GeneratedRegex(@"\s+using\s+chdman\s+verify\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex ChdmanVerifyEnglishSuffixRegex();

    [GeneratedRegex(@"\s+باستخدام\s+chdman\s+verify\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex ChdmanVerifyArabicSuffixRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex CollapsedWhitespaceRegex();
}