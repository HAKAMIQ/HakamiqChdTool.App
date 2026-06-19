using HakamiqChdTool.App.Core.Verification;
using HakamiqChdTool.App.Services;
using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace HakamiqChdTool.App.Services.Verification;

public sealed record VerificationDatabaseManifest(
    string SourceName,
    string ImportedUtc,
    int EntryCount,
    IReadOnlyList<GameHashAlgorithm> SupportedAlgorithms,
    string SystemName);

public sealed class DatFileImporter
{
    private const long MaxDatBytes = 256L * 1024L * 1024L;

    public async Task<GameHashSet> ImportAsync(
        string datPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datPath);

        string fullPath = Path.GetFullPath(datPath.Trim());
        FileInfo info = new(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("LocFileHash_FileNotFound", fullPath);
        }

        if (info.Length <= 0 || info.Length > MaxDatBytes)
        {
            throw new InvalidDataException("LocVerify_UnsupportedDatabaseFormat");
        }

        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        XDocument document = await XDocument
            .LoadAsync(stream, LoadOptions.None, cancellationToken)
            .ConfigureAwait(false);

        XElement root = document.Root ?? throw new InvalidDataException("LocVerify_UnsupportedDatabaseFormat");
        if (!string.Equals(root.Name.LocalName, "datafile", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("LocVerify_UnsupportedDatabaseFormat");
        }

        XElement? header = root.Element("header");
        string sourceName = Value(header, "name");
        string sourceDate = Value(header, "date");
        string systemName = Value(header, "description");
        var entries = new List<GameHashEntry>();

        foreach (XElement game in root.Elements().Where(static element =>
                     string.Equals(element.Name.LocalName, "game", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(element.Name.LocalName, "machine", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string gameName = Attribute(game, "name");
            string description = Value(game, "description");
            string displayGameName = string.IsNullOrWhiteSpace(description) ? gameName : description;

            foreach (XElement rom in game.Elements().Where(static element =>
                         string.Equals(element.Name.LocalName, "rom", StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(new GameHashEntry
                {
                    SystemName = systemName,
                    GameName = displayGameName,
                    DumpStatus = Attribute(rom, "status"),
                    FileName = Attribute(rom, "name"),
                    SizeBytes = TryParseLong(Attribute(rom, "size")),
                    CRC32 = Attribute(rom, "crc"),
                    MD5 = Attribute(rom, "md5"),
                    SHA1 = Attribute(rom, "sha1"),
                    SHA256 = Attribute(rom, "sha256"),
                    SourceDatabase = sourceName,
                    SourceDate = sourceDate
                });
            }
        }

        var hashSet = new GameHashSet(sourceName, sourceDate, systemName, entries);
        await WriteManifestAsync(hashSet, cancellationToken).ConfigureAwait(false);
        return hashSet;
    }

    private static async Task WriteManifestAsync(GameHashSet hashSet, CancellationToken cancellationToken)
    {
        string root = Path.Combine(AppPaths.LocalAppRoot, "VerificationDatabases");
        Directory.CreateDirectory(root);

        var algorithms = new HashSet<GameHashAlgorithm>();
        foreach (GameHashEntry entry in hashSet.Entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.CRC32)) algorithms.Add(GameHashAlgorithm.CRC32);
            if (!string.IsNullOrWhiteSpace(entry.MD5)) algorithms.Add(GameHashAlgorithm.MD5);
            if (!string.IsNullOrWhiteSpace(entry.SHA1)) algorithms.Add(GameHashAlgorithm.SHA1);
            if (!string.IsNullOrWhiteSpace(entry.SHA256)) algorithms.Add(GameHashAlgorithm.SHA256);
        }

        var manifest = new VerificationDatabaseManifest(
            hashSet.SourceName,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            hashSet.Entries.Count,
            [.. algorithms.OrderBy(static algorithm => algorithm)],
            hashSet.SystemName);

        string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(hashSet.SourceName)
            ? "imported-dat"
            : hashSet.SourceName);

        string manifestPath = Path.Combine(root, safeName + ".manifest.json");
        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, json, cancellationToken).ConfigureAwait(false);
    }

    private static string Value(XElement? element, string name) =>
        element?.Element(name)?.Value.Trim() ?? string.Empty;

    private static string Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value.Trim() ?? string.Empty;

    private static long? TryParseLong(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result)
            ? result
            : null;

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "imported-dat" : value.Trim();
    }
}
