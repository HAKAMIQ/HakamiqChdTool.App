using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Core.Workflow.Extraction;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HakamiqChdTool.App.Services;

/// <summary>
/// Holds the source files of a conversion open for reading from the start of preparation until
/// chdman finishes. Read sharing lets chdman and the preparation tools read them; write and delete
/// sharing are denied, so no other process can modify, replace, rename or delete them, or rename a
/// folder that contains them, while the job runs. Each opened handle must be the regular file at
/// the validated path (see <see cref="HandleIdentity"/>).
/// </summary>
internal sealed class SourceFileLease : IDisposable
{
    public const string SourceUnstableMessageKey = "LocConversion_SourceNotStable";

    private readonly List<FileStream> _handles;

    private SourceFileLease(List<FileStream> handles) => _handles = handles;

    public IReadOnlyList<string> Paths => [.. _handles.Select(handle => handle.Name)];

    public static bool TryAcquire(IEnumerable<string> paths, out SourceFileLease? lease, out string? failedPath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lease = null;
        failedPath = null;
        var handles = new List<FileStream>();
        try
        {
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                failedPath = path;
                var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.None);
                handles.Add(handle);
                if (!HandleIdentity.IsHandleAtPath(handle.SafeFileHandle, path))
                {
                    DisposeAll(handles);
                    return false;
                }
            }

            failedPath = null;
            lease = new SourceFileLease(handles);
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or System.Security.SecurityException)
        {
            DisposeAll(handles);
            return false;
        }
    }

    public void Dispose() => DisposeAll(_handles);

    private static void DisposeAll(List<FileStream> handles)
    {
        foreach (FileStream handle in handles)
        {
            handle.Dispose();
        }

        handles.Clear();
    }
}

/// <summary>
/// The files a conversion input reads: the input itself and, for descriptors, the existing track
/// and data files it names in its own folder. Missing files are left to the format checks.
/// </summary>
internal static class SourceDependencySet
{
    private const long MaxDescriptorBytes = 64 * 1024;

    public static IReadOnlyList<string> Resolve(string inputPath)
    {
        string fullPath = Path.GetFullPath(inputPath);
        var files = new List<string> { fullPath };
        if (!File.Exists(fullPath))
        {
            return files;
        }

        string directory = Path.GetDirectoryName(fullPath)!;
        try
        {
            switch (Path.GetExtension(fullPath).ToLowerInvariant())
            {
                case ".cue" when new FileInfo(fullPath).Length <= MaxDescriptorBytes:
                    AddNames(files, directory, EcmEvidence.ReadFileEntries(File.ReadAllText(fullPath, Encoding.UTF8)));
                    break;
                case ".gdi" when GdiSheet.TryRead(fullPath, out _, out List<GdiTrackLine> tracks):
                    AddNames(files, directory, tracks.Select(track => track.FileName));
                    break;
                case ".ccd":
                    AddExisting(files, Path.ChangeExtension(fullPath, ".img"));
                    AddExisting(files, Path.ChangeExtension(fullPath, ".sub"));
                    break;
                case ".mds" when AlcoholMdsCueBuilder.Parse(fullPath) is { IsValid: true, Layout: { } layout }:
                    AddExisting(files, layout.ImagePath);
                    break;
                case ".ecm" when EcmEvidence.FindReferencingCue(fullPath) is { } cue && EcmEvidence.TryReadCueSet(cue) is { } set:
                    AddExisting(files, set.CuePath);
                    foreach (EcmCueTrackFile file in set.Files)
                    {
                        AddExisting(files, file.EcmPath ?? file.PlainPath!);
                    }

                    break;
            }
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or System.Security.SecurityException)
        {
            // The input alone is still leased; the format checks report a broken descriptor.
        }

        return files;
    }

    private static void AddNames(List<string> files, string directory, IEnumerable<string>? names)
    {
        foreach (string name in names ?? [])
        {
            AddExisting(files, Path.Combine(directory, name));
        }
    }

    private static void AddExisting(List<string> files, string path)
    {
        string full = Path.GetFullPath(path);
        if (File.Exists(full) && !files.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            files.Add(full);
        }
    }
}
