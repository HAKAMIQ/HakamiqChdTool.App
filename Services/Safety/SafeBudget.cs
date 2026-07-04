using HakamiqChdTool.App.Models;
using System;

namespace HakamiqChdTool.App.Services.Safety;

internal sealed class SafetyScanBudget
{
    private readonly int _maxFiles;
    private readonly int _maxDirectories;

    private int _acceptedFiles;
    private int _acceptedDirectories;

    public SafetyScanBudget(InputSafetyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        _maxFiles = policy.MaxFilesToScan;
        _maxDirectories = policy.MaxDirectoriesToScan;
    }

    public int AcceptedFiles => _acceptedFiles;

    public int AcceptedDirectories => _acceptedDirectories;

    public bool WasLimitReached { get; private set; }

    public bool TryAcceptFile()
    {
        if (_acceptedFiles >= _maxFiles)
        {
            WasLimitReached = true;
            return false;
        }

        _acceptedFiles++;
        return true;
    }

    public bool TryAcceptDirectory()
    {
        if (_acceptedDirectories >= _maxDirectories)
        {
            WasLimitReached = true;
            return false;
        }

        _acceptedDirectories++;
        return true;
    }
}
