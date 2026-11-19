using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HakamiqChdTool.App.Core.Input;

/// <summary>One FILE entry of a CUE and where its data is: a plain file or an .ecm next to it.</summary>
public sealed record EcmCueTrackFile(string FileName, string? PlainPath, string? EcmPath);

/// <summary>A CUE in the same folder whose missing track files are all present as .ecm.</summary>
public sealed record EcmCueSet(string CuePath, IReadOnlyList<EcmCueTrackFile> Files)
{
    /// <summary>The first ECM-compressed file in CUE order; it represents the set in the queue.</summary>
    public string LeaderEcmPath => Files.First(file => file.EcmPath is not null).EcmPath!;
}

/// <summary>
/// Relates ECM-compressed tracks ("game.bin.ecm") to the CUE that describes them, so
/// intake can queue the set once and conversion can decode every compressed track.
/// Only plain file names in the CUE folder are considered; anything else is left to
/// the normal CUE workflow.
/// </summary>
public static class EcmEvidence
{
    public const string EcmExtension = ".ecm";
    private const long MaxCueBytes = 64 * 1024;
    private const int MaxCueCandidates = 256;

    /// <summary>"game.bin.ecm" decodes to "game.bin"; only .bin and .img payloads are accepted.</summary>
    public static bool TryGetDecodedName(string ecmPath, out string decodedName)
    {
        decodedName = string.Empty;
        string name = Path.GetFileName(ecmPath);
        if (!name.EndsWith(EcmExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string inner = name[..^EcmExtension.Length];
        string innerExtension = Path.GetExtension(inner);
        if (Path.GetFileNameWithoutExtension(inner).Length == 0
            || !(string.Equals(innerExtension, ".bin", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(innerExtension, ".img", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        decodedName = inner;
        return true;
    }

    /// <summary>
    /// Returns the ECM set of a CUE: every FILE is a plain name in the CUE folder, it
    /// exists either as-is or as "name.ecm", and at least one is only present as .ecm.
    /// </summary>
    public static EcmCueSet? TryReadCueSet(string cuePath)
    {
        try
        {
            string fullCue = Path.GetFullPath(cuePath);
            var info = new FileInfo(fullCue);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxCueBytes)
            {
                return null;
            }

            string directory = info.DirectoryName!;
            List<string>? names = ReadFileEntries(File.ReadAllText(fullCue, Encoding.UTF8));
            if (names is null || names.Count == 0)
            {
                return null;
            }

            var files = new List<EcmCueTrackFile>(names.Count);
            foreach (string name in names)
            {
                string plain = Path.Combine(directory, name);
                string ecm = plain + EcmExtension;
                if (File.Exists(plain))
                {
                    files.Add(new EcmCueTrackFile(name, plain, null));
                }
                else if (File.Exists(ecm) && TryGetDecodedName(ecm, out _))
                {
                    files.Add(new EcmCueTrackFile(name, null, ecm));
                }
                else
                {
                    return null;
                }
            }

            return files.Any(file => file.EcmPath is not null) ? new EcmCueSet(fullCue, files) : null;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Finds a CUE in the same folder that references the decoded name of this .ecm.</summary>
    public static string? FindReferencingCue(string ecmPath)
    {
        if (!TryGetDecodedName(ecmPath, out string decodedName))
        {
            return null;
        }

        try
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(ecmPath))!;
            foreach (string cue in Directory.EnumerateFiles(directory, "*.cue").Order(StringComparer.OrdinalIgnoreCase).Take(MaxCueCandidates))
            {
                var info = new FileInfo(cue);
                if (info.Length <= 0 || info.Length > MaxCueBytes)
                {
                    continue;
                }

                List<string>? names = ReadFileEntries(File.ReadAllText(cue, Encoding.UTF8));
                if (names is not null && names.Contains(decodedName, StringComparer.OrdinalIgnoreCase))
                {
                    return cue;
                }
            }
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or System.Security.SecurityException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// An .ecm is queued on its own when no CUE references it, or when it is the first
    /// compressed file of the CUE that references it. Otherwise the CUE's set covers it.
    /// </summary>
    public static bool IsQueueEntry(string ecmPath)
    {
        if (!TryGetDecodedName(ecmPath, out _))
        {
            return false;
        }

        string? cue = FindReferencingCue(ecmPath);
        if (cue is null)
        {
            return true;
        }

        EcmCueSet? set = TryReadCueSet(cue);
        return set is not null
            && string.Equals(set.LeaderEcmPath, Path.GetFullPath(ecmPath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A CUE whose tracks are partly or wholly ECM-compressed is converted through its .ecm entry.</summary>
    public static bool IsCueCoveredByEcm(string cuePath) => TryReadCueSet(cuePath) is not null;

    /// <summary>
    /// Reads the FILE entries of a CUE. Returns null when any entry is not a plain file
    /// name, so such CUEs keep their normal handling.
    /// </summary>
    internal static List<string>? ReadFileEntries(string cueText)
    {
        var names = new List<string>();
        foreach (string raw in cueText.Split('\n'))
        {
            string line = raw.Trim().TrimEnd('\r');
            if (!line.StartsWith("FILE", StringComparison.OrdinalIgnoreCase)
                || line.Length < 5
                || !char.IsWhiteSpace(line[4]))
            {
                continue;
            }

            string rest = line[4..].Trim();
            string name;
            if (rest.StartsWith('"'))
            {
                int close = rest.IndexOf('"', 1);
                if (close <= 1)
                {
                    return null;
                }

                name = rest[1..close];
            }
            else
            {
                int space = rest.IndexOfAny([' ', '\t']);
                name = space < 0 ? rest : rest[..space];
            }

            if (name.Length == 0
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
                || name is "." or "..")
            {
                return null;
            }

            names.Add(name);
        }

        return names;
    }
}
