using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Services;

internal static class ChdConversionMessages
{
    internal const string UserCancelledMessageKey = "LocStatus_UserCancelled";
    internal const string ConversionSuccessMessageKey = "LocConversion_Success";
    internal const string ConversionFailedMessageKey = "LocConversion_Failed";
    internal const string ExtractionSuccessMessageKey = "LocExtraction_Success";
    internal const string ExtractionFailedMessageKey = "LocExtraction_Failed";
    internal const string InvalidChdmanPathMessageKey = "LocConversion_InvalidChdmanPath";
    internal const string InvalidInputPathMessageKey = "LocConversion_InvalidInputPath";
    internal const string InvalidOutputPathMessageKey = "LocConversion_InvalidOutputPath";
    internal const string ChdmanNotFoundMessageKey = "LocConversion_ChdmanNotFound";
    internal const string InputFileNotFoundMessageKey = "LocConversion_InputFileNotFound";
    internal const string OutputDirectoryMissingMessageKey = "LocConversion_OutputDirectoryMissing";
    internal const string InvalidChdPathMessageKey = "LocExtraction_InvalidChdPath";
    internal const string InvalidCueOutputPathMessageKey = "LocExtraction_InvalidCueOutputPath";
    internal const string BinOutputDirectoryMissingMessageKey = "LocExtraction_BinOutputDirectoryMissing";
    internal const string ExtractionKindRequiresChdInputMessageKey = "LocExtraction_KindRequiresChdInput";
    internal const string InvalidCompressionSettingMessageKey = "LocConversion_InvalidCompressionSetting";
    internal const string InvalidDvdHunkSizeMessageKey = "LocConversion_InvalidDvdHunkSize";
    internal const string InvalidCdHunkSizeMessageKey = "LocConversion_InvalidCdHunkSize";
    internal const string InvalidCueBinDependencyMessageKey = "LocChdmanContract_InvalidCueBinDependency";
    internal const string DirectChdRecompressBlockedMessageKey = "LocChdPolicy_DirectChdRecompressBlocked";

    internal static readonly Regex CueFileReferenceRegex = new(
        "^\\s*FILE\\s+(?:\"(?<q>[^\"]+)\"|(?<u>\\S+))",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
}
