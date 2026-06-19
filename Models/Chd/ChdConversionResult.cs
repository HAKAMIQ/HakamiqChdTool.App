using System;
using HakamiqChdTool.App.Core.Chd.Commands;

namespace HakamiqChdTool.App.Models.Chd;

public sealed class ChdConversionResult
{
    public bool IsSuccess { get; init; }

    public bool WasCancelled { get; init; }

    public int ExitCode { get; init; }

    public ChdConversionStatus Status { get; init; } = ChdConversionStatus.Failed;

    public string InputPath { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public string CommandLine { get; init; } = string.Empty;

    public string Output { get; init; } = string.Empty;

    public string Error { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string LogPath { get; init; } = string.Empty;

    public TimeSpan ChdmanDuration { get; init; }

    public int NumProcessors { get; init; }

    public string CompressionCodecs { get; init; } = string.Empty;

    public string RequestedCompressionPreset { get; init; } = string.Empty;

    public string ResolvedCompressionCodecs { get; init; } = string.Empty;

    public string EffectiveCompressionCodecs { get; init; } = string.Empty;

    public bool EffectiveCompressionSameAsMameDefault { get; init; }

    public string? CompressionTruthNoteKey { get; init; }

    public int? HunkSizeBytes { get; init; }

    public long LogicalInputBytes { get; init; }

    public string RequestedProfile { get; init; } = string.Empty;

    public string ResolvedCommand { get; init; } = string.Empty;

    public string ResolvedCompression { get; init; } = string.Empty;

    public int? ResolvedHunkSize { get; init; }

    public string EffectiveCompression { get; init; } = string.Empty;

    public int? EffectiveHunkSize { get; init; }

    public bool SameAsMameDefault { get; init; }

    public string CompatibilityNotes { get; init; } = string.Empty;

    public string ChdmanVersion { get; init; } = string.Empty;
}
