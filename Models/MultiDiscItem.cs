using System;

namespace HakamiqChdTool.App.Models;

public sealed record MultiDiscItem
{
    public MultiDiscItem(
        string filePath,
        string fileName,
        int discNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (discNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(discNumber), discNumber, "Disc number must be greater than zero.");
        }

        FilePath = filePath.Trim();
        FileName = fileName.Trim();
        DiscNumber = discNumber;
    }

    public string FilePath { get; init; }

    public string FileName { get; init; }

    public int DiscNumber { get; init; }
}