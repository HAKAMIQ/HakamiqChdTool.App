using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HakamiqChdTool.App.Services.ExternalTools.Igir;

public sealed record IgirReportRow(string DatName, string GameName, string Status, string RomFiles,
    IReadOnlyDictionary<string, string> Flags);

public static class IgirReportParser
{
    private const int MaxBytes = 32 * 1024 * 1024;
    private const int MaxRows = 250_000;

    public static IReadOnlyList<IgirReportRow> Parse(string path)
    {
        FileInfo info = new(path);
        if (!info.Exists || info.Length > MaxBytes) throw new InvalidDataException("igir report is missing or exceeds its size limit.");
        using StreamReader reader = new(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        List<IReadOnlyList<string>> records = ReadCsv(reader);
        if (records.Count == 0) throw new InvalidDataException("igir report has no header.");
        Dictionary<string, int> columns = records[0].Select((name, index) => (name, index))
            .ToDictionary(pair => pair.name, pair => pair.index, StringComparer.OrdinalIgnoreCase);
        string[] required = ["DAT Name", "Game Name", "Status", "ROM Files"];
        if (required.Any(name => !columns.ContainsKey(name))) throw new InvalidDataException("igir report lacks required columns.");
        List<IgirReportRow> rows = [];
        foreach (IReadOnlyList<string> record in records.Skip(1))
        {
            if (record.All(string.IsNullOrEmpty)) continue;
            if (record.Count != records[0].Count) throw new InvalidDataException("igir report row has an invalid column count.");
            string status = record[columns["Status"]];
            if (status is not ("FOUND" or "DUPLICATE" or "UNUSED" or "MISSING" or "DELETED" or "INCOMPLETE"))
                throw new InvalidDataException("igir report contains an unknown status.");
            rows.Add(new(record[columns["DAT Name"]], record[columns["Game Name"]], status,
                record[columns["ROM Files"]], columns.Where(pair => !required.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key, pair => record[pair.Value], StringComparer.OrdinalIgnoreCase)));
            if (rows.Count > MaxRows) throw new InvalidDataException("igir report exceeds its row limit.");
        }
        return rows;
    }

    private static List<IReadOnlyList<string>> ReadCsv(TextReader reader)
    {
        List<IReadOnlyList<string>> rows = [];
        List<string> row = [];
        StringBuilder field = new();
        bool quoted = false;
        int value;
        while ((value = reader.Read()) >= 0)
        {
            char c = (char)value;
            if (quoted)
            {
                if (c == '"' && reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && reader.Peek() == '\n') reader.Read();
                row.Add(field.ToString()); field.Clear(); rows.Add(row.ToArray()); row = [];
            }
            else field.Append(c);
        }
        if (quoted) throw new InvalidDataException("igir report contains an unterminated quoted field.");
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }
}
