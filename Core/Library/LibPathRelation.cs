using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HakamiqChdTool.App.Core.Library;

/// <summary>
/// Compares two directory paths by whole path components, never by string prefix, so
/// <c>C:\Games2</c> is not "inside" <c>C:\Games</c>. Existing parts of a path are expanded
/// to their long form first, so 8.3 short names cannot hide an overlap. Read-only: it only
/// queries the file system and creates nothing. Like every path check here, the answer
/// can change between the check and a later use (TOCTOU), which callers already accept.
/// </summary>
internal static class LibPathRelation
{
    private const int MaxLongPath = 32768;

    /// <summary>True when <paramref name="candidate"/> is the same folder as, or lies inside, <paramref name="parent"/>.</summary>
    public static bool IsSameOrInside(string parent, string candidate)
    {
        string[] parentParts = Components(Canonical(parent));
        string[] candidateParts = Components(Canonical(candidate));

        if (parentParts.Length == 0 || candidateParts.Length < parentParts.Length)
        {
            return false;
        }

        for (int i = 0; i < parentParts.Length; i++)
        {
            if (!string.Equals(parentParts[i], candidateParts[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string Canonical(string path)
    {
        string full = Path.GetFullPath(path);
        string trimmed = full.Length > 3 ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;

        // Expand 8.3 names on the longest existing prefix; the not-yet-existing tail is kept as typed.
        string existing = trimmed;
        string tail = string.Empty;

        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            string? parent = Path.GetDirectoryName(existing);

            if (parent is null)
            {
                return trimmed;
            }

            tail = Path.Combine(Path.GetFileName(existing), tail);
            existing = parent;
        }

        StringBuilder buffer = new(1024);
        uint length = GetLongPathNameW(existing, buffer, (uint)buffer.Capacity);

        if (length == 0 || length >= MaxLongPath)
        {
            return trimmed;
        }

        if (length >= buffer.Capacity)
        {
            buffer.EnsureCapacity((int)length + 1);
            length = GetLongPathNameW(existing, buffer, (uint)buffer.Capacity);

            if (length == 0 || length >= buffer.Capacity)
            {
                return trimmed;
            }
        }

        string expanded = buffer.ToString();
        return tail.Length == 0 ? expanded : Path.Combine(expanded, tail);
    }

    private static string[] Components(string fullPath) =>
        fullPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, uint bufferLength);
}
