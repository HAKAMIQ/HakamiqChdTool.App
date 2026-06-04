using System;

namespace HakamiqChdTool.App.Models;

public sealed class PostConversionArtifactFailure
{
    private string _artifactKind = string.Empty;
    private string _messageCode = string.Empty;
    private string? _targetPath;

    public required string ArtifactKind
    {
        get => _artifactKind;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _artifactKind = value.Trim();
        }
    }

    public required string MessageCode
    {
        get => _messageCode;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _messageCode = value.Trim();
        }
    }

    public string? TargetPath
    {
        get => _targetPath;
        init => _targetPath = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}