using System;
using System.IO;
using static HakamiqChdTool.App.Services.ChdConversionMessages;

namespace HakamiqChdTool.App.Services;

internal static class ChdConversionRequestGuard
{
    public static void ValidateToChdInputs(string chdmanPath, string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(chdmanPath))
        {
            throw new ArgumentException(InvalidChdmanPathMessageKey, nameof(chdmanPath));
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException(InvalidInputPathMessageKey, nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException(InvalidOutputPathMessageKey, nameof(outputPath));
        }

        if (!File.Exists(chdmanPath))
        {
            throw new FileNotFoundException(ChdmanNotFoundMessageKey, chdmanPath);
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException(InputFileNotFoundMessageKey, inputPath);
        }

        ConversionPathValidator.ThrowIfUnsafeForChdman(chdmanPath, nameof(chdmanPath));
        ConversionPathValidator.ThrowIfUnsafeForChdman(inputPath, nameof(inputPath));
        ConversionPathValidator.ThrowIfUnsafeForChdman(outputPath, nameof(outputPath));
    }
}
