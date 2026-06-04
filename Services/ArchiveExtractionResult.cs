using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Services;

public sealed class ArchiveExtractionResult
{
    public bool IsSuccess { get; init; }

    public bool WasCancelled { get; init; }

    public bool RequiresPassword { get; init; }

    public int ExitCode { get; init; }

    public string ExtractedPath { get; init; } = string.Empty;

    public IReadOnlyList<string> ExtractedFiles { get; init; } = Array.Empty<string>();

    public string Output { get; init; } = string.Empty;

    public string Error { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}