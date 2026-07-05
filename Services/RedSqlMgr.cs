using Microsoft.Data.Sqlite;
using Serilog;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace HakamiqChdTool.App.Services;

public readonly record struct RedumpRomHit(
    string SystemName,
    string GameName,
    string RomName,
    long? SizeBytes,
    string? Crc,
    string MatchSource);

public readonly record struct RedumpImportProgress(
    int RowsInserted,
    int? TotalRowsEstimate);

public readonly record struct RedumpImportResult(
    bool Success,
    string MessageKey,
    IReadOnlyList<object?> MessageArgs,
    int RowsImported)
{
    public RedumpImportResult(bool success, string messageKey, int rowsImported)
        : this(success, messageKey, [], rowsImported)
    {
    }

    public string Message => MessageKey;
}

public sealed class RedumpSqliteManager
{
    private const string FileNotFoundMessageKey = "LocRedumpImport_FileNotFound";
    private const string SuccessMessageKey = "LocRedumpImport_Success";
    private const string FailedMessageKey = "LocRedumpImport_Failed";

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<RedumpSqliteManager>();

    private static readonly Lazy<RedumpSqliteManager> LazyDefault =
        new(() => new RedumpSqliteManager(GetDefaultDatabasePath()));

    private readonly string _databasePath;
    private readonly object _dbLock = new();

    private RedumpSqliteManager(string databasePath)
    {
        _databasePath = databasePath;
    }

    public static RedumpSqliteManager Default => LazyDefault.Value;

    public string DatabasePath => _databasePath;

    public static string GetDefaultDatabasePath()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HakamiqChdTool");

        Directory.CreateDirectory(root);
        return Path.Combine(root, "RedumpDB.sqlite");
    }

    public void EnsureInitialized()
    {
        lock (_dbLock)
        {
            string? directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using SqliteConnection connection = OpenConnectionCore();
            ApplyPragmas(connection);

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS RomHashes (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SystemName TEXT NOT NULL,
                        GameName TEXT NOT NULL,
                        RomName TEXT NOT NULL,
                        MD5 TEXT,
                        SHA1 TEXT,
                        SizeBytes INTEGER,
                        CRC TEXT
                    );
                    """;
                command.ExecuteNonQuery();
            }

            EnsureColumn(connection, "SizeBytes", "ALTER TABLE RomHashes ADD COLUMN SizeBytes INTEGER;");
            EnsureColumn(connection, "CRC", "ALTER TABLE RomHashes ADD COLUMN CRC TEXT;");

            ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_RomHashes_MD5 ON RomHashes(MD5);");
            ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_RomHashes_SHA1 ON RomHashes(SHA1);");
            ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_RomHashes_MD5_Size ON RomHashes(MD5, SizeBytes);");
            ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_RomHashes_SHA1_Size ON RomHashes(SHA1, SizeBytes);");
            ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_RomHashes_Game ON RomHashes(SystemName, GameName);");
        }
    }

    public bool HasAnyRows()
    {
        EnsureInitialized();

        lock (_dbLock)
        {
            using SqliteConnection connection = OpenConnectionCore();
            ApplyPragmas(connection);

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM RomHashes LIMIT 1;";
            return command.ExecuteScalar() is not null;
        }
    }

    public long GetTotalRowCount()
    {
        EnsureInitialized();

        lock (_dbLock)
        {
            using SqliteConnection connection = OpenConnectionCore();
            ApplyPragmas(connection);

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM RomHashes;";

            object? scalar = command.ExecuteScalar();
            return scalar is long value
                ? value
                : Convert.ToInt64(scalar ?? 0L, CultureInfo.InvariantCulture);
        }
    }

    public bool HasSystemRowsForDetectedPlatform(string detectedPlatformName)
    {
        string[] candidates = BuildDetectedPlatformCandidates(detectedPlatformName);

        if (candidates.Length == 0)
        {
            return false;
        }

        EnsureInitialized();

        lock (_dbLock)
        {
            using SqliteConnection connection = OpenConnectionCore();
            ApplyPragmas(connection);

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT SystemName FROM RomHashes;";

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                string systemName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                string normalizedSystem = NormalizeComparableSystemName(systemName);

                foreach (string candidate in candidates)
                {
                    if (IsComparableSystemMatch(normalizedSystem, candidate))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public bool TryMatchHash(string md5LowerHex, string sha1LowerHex, out RedumpRomHit hit) =>
        TryMatchHash(md5LowerHex, sha1LowerHex, sizeBytes: null, out hit);

    public bool TryMatchHash(string md5LowerHex, string sha1LowerHex, long? sizeBytes, out RedumpRomHit hit)
    {
        hit = default;

        string md5 = NormalizeHex(md5LowerHex) ?? string.Empty;
        string sha1 = NormalizeHex(sha1LowerHex) ?? string.Empty;

        if (md5.Length == 0 && sha1.Length == 0)
        {
            return false;
        }

        EnsureInitialized();

        lock (_dbLock)
        {
            using SqliteConnection connection = OpenConnectionCore();
            ApplyPragmas(connection);

            if (sizeBytes.HasValue)
            {
                if (TryQueryHit(connection, "MD5", md5, sizeBytes.Value, "MD5+Size", out hit))
                {
                    return true;
                }

                if (TryQueryHit(connection, "SHA1", sha1, sizeBytes.Value, "SHA1+Size", out hit))
                {
                    return true;
                }
            }

            if (TryQueryHit(connection, "MD5", md5, sizeBytes: null, "MD5", out hit))
            {
                return true;
            }

            if (TryQueryHit(connection, "SHA1", sha1, sizeBytes: null, "SHA1", out hit))
            {
                return true;
            }
        }

        return false;
    }

    public Task<RedumpImportResult> ImportDatFileAsync(
        string datFilePath,
        string systemName,
        IProgress<RedumpImportProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => ImportDatFileCore(datFilePath, systemName, progress, cancellationToken),
            cancellationToken);
    }

    public Task<RedumpImportResult> CleanRebuildFromDatFilesAsync(
        IReadOnlyList<RedumpLocalLibraryDatEntry> entries,
        IProgress<RedumpImportProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => CleanRebuildFromDatFilesCore(entries, progress, cancellationToken),
            cancellationToken);
    }

    private RedumpImportResult CleanRebuildFromDatFilesCore(
        IReadOnlyList<RedumpLocalLibraryDatEntry> entries,
        IProgress<RedumpImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (entries is null || entries.Count == 0)
        {
            return new RedumpImportResult(false, FileNotFoundMessageKey, 0);
        }

        RedumpLocalLibraryDatEntry[] importableEntries = entries
            .Where(entry => entry.IsSelected)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.FilePath))
            .Where(entry => File.Exists(entry.FilePath))
            .ToArray();

        if (importableEntries.Length == 0)
        {
            return new RedumpImportResult(false, FileNotFoundMessageKey, 0);
        }

        try
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();

            lock (_dbLock)
            {
                using SqliteConnection connection = OpenConnectionCore();
                ApplyPragmas(connection);

                using SqliteTransaction transaction = connection.BeginTransaction();

                try
                {
                    DeleteAllRows(connection, transaction);
                    ResetRomHashesSequence(connection, transaction);

                    using SqliteCommand insert = CreateInsertCommand(connection, transaction);

                    SqliteParameter pSystem = insert.Parameters["$system"];
                    SqliteParameter pGame = insert.Parameters["$game"];
                    SqliteParameter pRom = insert.Parameters["$rom"];
                    SqliteParameter pMd5 = insert.Parameters["$md5"];
                    SqliteParameter pSha1 = insert.Parameters["$sha1"];
                    SqliteParameter pSize = insert.Parameters["$size"];
                    SqliteParameter pCrc = insert.Parameters["$crc"];

                    int totalInserted = 0;

                    foreach (RedumpLocalLibraryDatEntry entry in importableEntries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string systemName = NormalizeRedumpSystemName(FirstNonEmpty(
                            entry.Name,
                            entry.Description,
                            Path.GetFileNameWithoutExtension(entry.FilePath)));

                        int inserted = ImportRows(
                            entry.FilePath,
                            systemName,
                            insert,
                            pSystem,
                            pGame,
                            pRom,
                            pMd5,
                            pSha1,
                            pSize,
                            pCrc,
                            progress: null,
                            cancellationToken);

                        totalInserted += inserted;
                        progress?.Report(new RedumpImportProgress(totalInserted, null));
                    }

                    transaction.Commit();
                    progress?.Report(new RedumpImportProgress(totalInserted, totalInserted));

                    return new RedumpImportResult(
                        true,
                        SuccessMessageKey,
                        totalInserted);
                }
                catch
                {
                    RollbackSafely(transaction, "Redump clean rebuild");
                    throw;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedImportException(ex))
        {
            Logger.Warning(ex, "Redump clean rebuild failed.");
            return new RedumpImportResult(false, FailedMessageKey, 0);
        }
    }

    private RedumpImportResult ImportDatFileCore(
        string datFilePath,
        string systemName,
        IProgress<RedumpImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(datFilePath))
        {
            return new RedumpImportResult(false, FileNotFoundMessageKey, 0);
        }

        string fullDatPath;
        try
        {
            fullDatPath = Path.GetFullPath(datFilePath);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "Redump DAT import rejected an invalid path. Path={Path}", datFilePath);
            return new RedumpImportResult(false, FileNotFoundMessageKey, 0);
        }

        if (!File.Exists(fullDatPath))
        {
            return new RedumpImportResult(false, FileNotFoundMessageKey, 0);
        }

        string resolvedSystemName = ResolveSystemName(fullDatPath, systemName);

        try
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();

            lock (_dbLock)
            {
                using SqliteConnection connection = OpenConnectionCore();
                ApplyPragmas(connection);

                using SqliteTransaction transaction = connection.BeginTransaction();

                try
                {
                    DeleteSystemRows(connection, transaction, resolvedSystemName);

                    using SqliteCommand insert = CreateInsertCommand(connection, transaction);

                    SqliteParameter pSystem = insert.Parameters["$system"];
                    SqliteParameter pGame = insert.Parameters["$game"];
                    SqliteParameter pRom = insert.Parameters["$rom"];
                    SqliteParameter pMd5 = insert.Parameters["$md5"];
                    SqliteParameter pSha1 = insert.Parameters["$sha1"];
                    SqliteParameter pSize = insert.Parameters["$size"];
                    SqliteParameter pCrc = insert.Parameters["$crc"];

                    int inserted = ImportRows(
                        fullDatPath,
                        resolvedSystemName,
                        insert,
                        pSystem,
                        pGame,
                        pRom,
                        pMd5,
                        pSha1,
                        pSize,
                        pCrc,
                        progress,
                        cancellationToken);

                    transaction.Commit();
                    progress?.Report(new RedumpImportProgress(inserted, inserted));

                    return new RedumpImportResult(
                        true,
                        SuccessMessageKey,
                        [inserted, resolvedSystemName],
                        inserted);
                }
                catch
                {
                    RollbackSafely(transaction, resolvedSystemName);
                    throw;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedImportException(ex))
        {
            Logger.Warning(ex, "Redump DAT import failed. Path={Path}, System={System}", fullDatPath, resolvedSystemName);
            return new RedumpImportResult(false, FailedMessageKey, 0);
        }
    }

    private static int ImportRows(
        string datFilePath,
        string systemName,
        SqliteCommand insert,
        SqliteParameter pSystem,
        SqliteParameter pGame,
        SqliteParameter pRom,
        SqliteParameter pMd5,
        SqliteParameter pSha1,
        SqliteParameter pSize,
        SqliteParameter pCrc,
        IProgress<RedumpImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null
        };

        int inserted = 0;
        string? currentGameName = null;
        string? currentDescription = null;
        bool insideGame = false;
        const int progressEvery = 2000;

        using FileStream stream = new(
            datFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        using XmlReader reader = XmlReader.Create(stream, settings);

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.EndElement && IsGameElement(reader.Name))
            {
                insideGame = false;
                currentGameName = null;
                currentDescription = null;
                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            string elementName = reader.Name;

            if (IsGameElement(elementName))
            {
                insideGame = true;
                currentGameName = reader.GetAttribute("name");
                currentDescription = null;
                continue;
            }

            if (insideGame && elementName.Equals("description", StringComparison.OrdinalIgnoreCase))
            {
                currentDescription = reader.ReadElementContentAsString()?.Trim();
                continue;
            }

            if (!elementName.Equals("rom", StringComparison.OrdinalIgnoreCase)
                && !elementName.Equals("disk", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? md5 = NormalizeHex(reader.GetAttribute("md5"));
            string? sha1 = NormalizeHex(reader.GetAttribute("sha1"));

            if (string.IsNullOrEmpty(md5) && string.IsNullOrEmpty(sha1))
            {
                continue;
            }

            string? crc = NormalizeHex(reader.GetAttribute("crc"));
            long? sizeBytes = ParseLong(reader.GetAttribute("size"));
            string? romName = reader.GetAttribute("name");

            string gameName = ResolveGameName(currentDescription, currentGameName, systemName);
            string resolvedRomName = string.IsNullOrWhiteSpace(romName)
                ? elementName
                : romName.Trim();

            pSystem.Value = systemName;
            pGame.Value = gameName;
            pRom.Value = resolvedRomName;
            pMd5.Value = string.IsNullOrEmpty(md5) ? DBNull.Value : md5;
            pSha1.Value = string.IsNullOrEmpty(sha1) ? DBNull.Value : sha1;
            pSize.Value = sizeBytes.HasValue ? sizeBytes.Value : DBNull.Value;
            pCrc.Value = string.IsNullOrEmpty(crc) ? DBNull.Value : crc;

            insert.ExecuteNonQuery();
            inserted++;

            if (inserted % progressEvery == 0)
            {
                progress?.Report(new RedumpImportProgress(inserted, null));
            }
        }

        return inserted;
    }

    private static string ResolveSystemName(string datFilePath, string systemName)
    {
        string resolved = string.IsNullOrWhiteSpace(systemName)
            ? Path.GetFileNameWithoutExtension(datFilePath)
            : systemName.Trim();

        return string.IsNullOrWhiteSpace(resolved)
            ? "Redump"
            : resolved;
    }

    private static string ResolveGameName(string? description, string? gameName, string systemName)
    {
        if (!string.IsNullOrWhiteSpace(description))
        {
            return description.Trim();
        }

        if (!string.IsNullOrWhiteSpace(gameName))
        {
            return gameName.Trim();
        }

        return string.IsNullOrWhiteSpace(systemName) ? "Redump" : systemName;
    }

    private static void DeleteSystemRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string systemName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM RomHashes WHERE SystemName = $sys;";
        command.Parameters.AddWithValue("$sys", systemName);
        command.ExecuteNonQuery();
    }

    private static void DeleteAllRows(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM RomHashes;";
        command.ExecuteNonQuery();
    }

    private static void ResetRomHashesSequence(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM sqlite_sequence WHERE name = 'RomHashes';";
        command.ExecuteNonQuery();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "Redump";
    }

    private static string NormalizeRedumpSystemName(string value)
    {
        string result = value.Trim();

        string[] cuts =
        {
            " - Datfile",
            " - Cuesheets",
            " - GDI",
            " - Subchannels",
            " - Disc Keys",
            " - BIOS Datfile"
        };

        foreach (string cut in cuts)
        {
            int index = result.IndexOf(cut, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                result = result[..index].Trim();
            }
        }

        while (result.Contains("  ", StringComparison.Ordinal))
        {
            result = result.Replace("  ", " ", StringComparison.Ordinal);
        }

        return result;
    }

    private static SqliteCommand CreateInsertCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO RomHashes (SystemName, GameName, RomName, MD5, SHA1, SizeBytes, CRC)
            VALUES ($system, $game, $rom, $md5, $sha1, $size, $crc);
            """;

        insert.Parameters.Add("$system", SqliteType.Text);
        insert.Parameters.Add("$game", SqliteType.Text);
        insert.Parameters.Add("$rom", SqliteType.Text);
        insert.Parameters.Add("$md5", SqliteType.Text);
        insert.Parameters.Add("$sha1", SqliteType.Text);
        insert.Parameters.Add("$size", SqliteType.Integer);
        insert.Parameters.Add("$crc", SqliteType.Text);

        return insert;
    }

    private SqliteConnection OpenConnectionCore()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        var connection = new SqliteConnection(builder.ConnectionString)
        {
            DefaultTimeout = 60
        };

        connection.Open();
        return connection;
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, "PRAGMA journal_mode=WAL;");
        ExecuteNonQuery(connection, "PRAGMA synchronous=NORMAL;");
        ExecuteNonQuery(connection, "PRAGMA temp_store=MEMORY;");
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string commandText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string columnName, string alterSql)
    {
        bool exists = false;

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(RomHashes);";

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string existing = reader.GetString(1);
                if (string.Equals(existing, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (!exists)
        {
            ExecuteNonQuery(connection, alterSql);
        }
    }

    private static bool TryQueryHit(
        SqliteConnection connection,
        string columnName,
        string hash,
        long? sizeBytes,
        string source,
        out RedumpRomHit hit)
    {
        hit = default;

        if (string.IsNullOrEmpty(hash) || !IsAllowedHashColumn(columnName))
        {
            return false;
        }

        using SqliteCommand command = connection.CreateCommand();

        if (sizeBytes.HasValue)
        {
            command.CommandText = $"""
                SELECT SystemName, GameName, RomName, SizeBytes, CRC
                FROM RomHashes
                WHERE {columnName} COLLATE NOCASE = $h AND SizeBytes = $size
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$size", sizeBytes.Value);
        }
        else
        {
            command.CommandText = $"""
                SELECT SystemName, GameName, RomName, SizeBytes, CRC
                FROM RomHashes
                WHERE {columnName} COLLATE NOCASE = $h
                LIMIT 1;
                """;
        }

        command.Parameters.AddWithValue("$h", hash);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return false;
        }

        long? size = reader.IsDBNull(3) ? null : reader.GetInt64(3);
        string? crc = reader.IsDBNull(4) ? null : reader.GetString(4);

        hit = new RedumpRomHit(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            size,
            crc,
            source);

        return true;
    }

    private static string[] BuildDetectedPlatformCandidates(string detectedPlatformName)
    {
        string normalized = NormalizeComparableSystemName(detectedPlatformName);

        if (normalized.Length == 0)
        {
            return [];
        }

        HashSet<string> values = new(StringComparer.OrdinalIgnoreCase)
        {
            normalized
        };

        void Add(string value)
        {
            string candidate = NormalizeComparableSystemName(value);
            if (candidate.Length > 0)
            {
                values.Add(candidate);
            }
        }

        if (normalized.Contains("playstation portable", StringComparison.Ordinal)
            || string.Equals(normalized, "psp", StringComparison.Ordinal))
        {
            Add("Sony - PlayStation Portable");
            Add("Sony PlayStation Portable");
            Add("PlayStation Portable");
            Add("PSP");
        }

        if (normalized.Contains("playstation 5", StringComparison.Ordinal))
        {
            Add("Sony PlayStation 5");
        }

        if (normalized.Contains("playstation 4", StringComparison.Ordinal))
        {
            Add("Sony PlayStation 4");
        }

        if (normalized.Contains("playstation 3", StringComparison.Ordinal)
            || string.Equals(normalized, "ps3", StringComparison.Ordinal))
        {
            Add("Sony - PlayStation 3");
            Add("Sony PlayStation 3");
            Add("PlayStation 3");
            Add("PS3");
        }

        if (normalized.Contains("playstation 2", StringComparison.Ordinal)
            || string.Equals(normalized, "ps2", StringComparison.Ordinal))
        {
            Add("Sony - PlayStation 2");
            Add("Sony PlayStation 2");
            Add("PlayStation 2");
            Add("PS2");
        }

        if (normalized.Contains("playstation 1", StringComparison.Ordinal)
            || string.Equals(normalized, "psx", StringComparison.Ordinal)
            || string.Equals(normalized, "ps1", StringComparison.Ordinal))
        {
            Add("Sony - PlayStation");
            Add("Sony PlayStation");
            Add("Sony PlayStation 1");
            Add("PlayStation");
            Add("PlayStation 1");
            Add("PSX");
            Add("PS1");
        }

        if (normalized.Contains("dreamcast", StringComparison.Ordinal))
        {
            Add("Sega Dreamcast");
        }

        if (normalized.Contains("saturn", StringComparison.Ordinal))
        {
            Add("Sega Saturn");
        }

        if (normalized.Contains("gamecube", StringComparison.Ordinal)
            || normalized.Contains("game cube", StringComparison.Ordinal))
        {
            Add("Nintendo GameCube");
        }

        if (string.Equals(normalized, "wii", StringComparison.Ordinal)
            || normalized.Contains("nintendo wii", StringComparison.Ordinal))
        {
            Add("Nintendo Wii");
        }

        if (normalized.Contains("xbox series", StringComparison.Ordinal))
        {
            Add("Microsoft Xbox Series X");
        }

        if (normalized.Contains("xbox one", StringComparison.Ordinal))
        {
            Add("Microsoft Xbox One");
        }

        if (normalized.Contains("xbox 360", StringComparison.Ordinal))
        {
            Add("Microsoft Xbox 360");
        }

        if (string.Equals(normalized, "xbox", StringComparison.Ordinal)
            || normalized.Contains("original xbox", StringComparison.Ordinal))
        {
            Add("Microsoft Xbox");
        }

        return [.. values];
    }

    private static bool IsComparableSystemMatch(string knownSystem, string candidate)
    {
        if (knownSystem.Length == 0 || candidate.Length == 0)
        {
            return false;
        }

        return string.Equals(knownSystem, candidate, StringComparison.Ordinal)
            || knownSystem.EndsWith(" " + candidate, StringComparison.Ordinal)
            || candidate.EndsWith(" " + knownSystem, StringComparison.Ordinal);
    }

    private static string NormalizeComparableSystemName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] chars = value.Trim().ToLowerInvariant().Select(static character =>
            char.IsLetterOrDigit(character) ? character : ' ').ToArray();

        return string.Join(
            " ",
            new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsAllowedHashColumn(string columnName) =>
        string.Equals(columnName, "MD5", StringComparison.Ordinal)
        || string.Equals(columnName, "SHA1", StringComparison.Ordinal);

    private static bool IsGameElement(string name) =>
        name.Equals("game", StringComparison.OrdinalIgnoreCase)
        || name.Equals("machine", StringComparison.OrdinalIgnoreCase);

    private static long? ParseLong(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;
    }

    private static string? NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return null;
        }

        for (int index = 0; index < normalized.Length; index++)
        {
            char character = normalized[index];
            bool isHex = character is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!isHex)
            {
                return null;
            }
        }

        return normalized;
    }

    private static void RollbackSafely(SqliteTransaction transaction, string systemName)
    {
        try
        {
            transaction.Rollback();
        }
        catch (Exception rollbackEx) when (rollbackEx is InvalidOperationException or SqliteException)
        {
            Logger.Warning(rollbackEx, "Redump transaction rollback failed after import error. System={System}", systemName);
        }
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException;

    private static bool IsExpectedImportException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or InvalidDataException
        or NotSupportedException
        or ArgumentException
        or XmlException
        or SqliteException;
}