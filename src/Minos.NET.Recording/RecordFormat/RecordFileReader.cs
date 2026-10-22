using System.Buffers;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Minos.Recording.RecordFormat;

/// <summary>Where a line sits in the file: its first byte, its length without the line ending, whether this reader models it, and its 1-based line number.</summary>
internal readonly record struct LineSpan(long Offset, int Length, bool Known, int Line);

/// <summary>
/// Reads a record file in two passes. The scan pass folds the lines into the decisions the file describes and keeps only
/// where each surviving line sits: outcomes are attached to their record in file order, an erasure removes the records
/// before it, and a higher attempt replaces a lower one. The second pass reads the bytes of the lines that survived, so
/// compaction can copy them as they are and a read can parse one decision at a time.
/// </summary>
internal static class RecordFileReader
{
    internal sealed class Entry(string decisionId, string? erasureKey, int attempt, LineSpan record)
    {
        internal string DecisionId { get; } = decisionId;

        internal string? ErasureKey { get; set; } = erasureKey;

        internal int Attempt { get; set; } = attempt;

        internal LineSpan Record { get; set; } = record;

        internal List<LineSpan> Outcomes { get; } = [];

        internal bool Removed { get; set; }
    }

    internal sealed class Scan
    {
        internal required List<Entry> Decisions { get; init; }

        // Lines of this version that this reader does not model: a kind it does not know, or an outcome of a type it does not
        // know that belongs to no record. A compaction keeps them as they are.
        internal required List<LineSpan> Loose { get; init; }

        internal required List<DecisionRecordFileProblem> Problems { get; init; }

        // True when a line was skipped only because this reader does not know its version: rewriting the file would lose it.
        internal bool SkippedUnknownVersion { get; init; }
    }

    internal static SafeFileHandle Open(string path)
        => File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous | FileOptions.SequentialScan);

    internal static async ValueTask<byte[]> ReadSpanAsync(SafeFileHandle handle, LineSpan span, CancellationToken cancellationToken)
    {
        var bytes = new byte[span.Length];
        var filled = 0;
        while (filled < bytes.Length)
        {
            var read = await RandomAccess.ReadAsync(handle, bytes.AsMemory(filled), span.Offset + filled, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("The file changed while it was read.");
            }

            filled += read;
        }

        return bytes;
    }

    // The scan parsed these lines once already, so a failure here means the file changed between the two passes.
    internal static DecisionRecord ParseRecord(byte[] line)
        => ParseLine(line) is { } dto && RecordLineConverter.TryToRecord(dto, out var record, out _)
            ? record
            : throw new IOException("The file changed while it was read.");

    internal static DecisionOutcome ParseOutcome(byte[] line)
        => ParseLine(line) is { } dto && RecordLineConverter.TryToOutcome(dto, out var outcome, out _)
            ? outcome
            : throw new IOException("The file changed while it was read.");

    private static LineDto? ParseLine(byte[] line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, RecordJsonContext.Default.LineDto);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static async ValueTask<Scan> ScanAsync(SafeFileHandle handle, CancellationToken cancellationToken)
    {
        var fold = new Fold();
        var chunk = new byte[81920];
        var line = new ArrayBufferWriter<byte>();
        long position = 0;
        long lineStart = 0;
        while (true)
        {
            var read = await RandomAccess.ReadAsync(handle, chunk, position, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            var start = 0;
            while (start < read)
            {
                var end = Array.IndexOf(chunk, (byte)'\n', start, read - start);
                if (end < 0)
                {
                    line.Write(chunk.AsSpan(start, read - start));
                    break;
                }

                line.Write(chunk.AsSpan(start, end - start));
                fold.Add(line.WrittenSpan, lineStart);
                line.Clear();
                start = end + 1;
                lineStart = position + start;
            }

            position += read;
        }

        if (line.WrittenCount > 0)
        {
            fold.Add(line.WrittenSpan, lineStart);
        }

        return fold.Finish();
    }

    private sealed class Fold
    {
        // A version that is not a whole number that fits an int; no real version is this.
        private const int UnreadableVersion = int.MinValue;

        private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Entry>> _byKey = new(StringComparer.Ordinal);
        private readonly HashSet<string> _erasedIds = new(StringComparer.Ordinal);
        private readonly List<Entry> _order = [];
        private readonly List<LineSpan> _loose = [];
        private readonly List<DecisionRecordFileProblem> _problems = [];
        private int _lineNumber;
        private bool _skippedUnknownVersion;

        internal void Add(ReadOnlySpan<byte> line, long offset)
        {
            _lineNumber++;
            if (_lineNumber == 1 && line.StartsWith("﻿"u8))
            {
                line = line[3..];
                offset += 3;
            }

            if (!line.IsEmpty && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            if (IsBlank(line))
            {
                return;
            }

            if (!TryPeek(line, out var kind, out var version))
            {
                return;
            }

            if (version is null)
            {
                Problem("The line has no 'version'.");
                return;
            }

            if (version != RecordLine.Version)
            {
                // Whatever else the line holds, it is not in a format this reader knows, so a rewrite would lose it.
                _skippedUnknownVersion = true;
                Problem(version == UnreadableVersion
                    ? "This file uses a record format version this reader does not know."
                    : $"This file uses record format version {RecordLine.Number(version.Value)}; this reader reads version {RecordLine.Number(RecordLine.Version)}.");
                return;
            }

            LineDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize(line, RecordJsonContext.Default.LineDto);
            }
            catch (JsonException ex)
            {
                Problem(Syntax(ex));
                return;
            }
            catch (InvalidOperationException)
            {
                Problem("The line is not a valid record line.");
                return;
            }

            if (dto is null)
            {
                Problem("The line is not a JSON object.");
                return;
            }

            var span = new LineSpan(offset, line.Length, Known: true, _lineNumber);
            switch (kind)
            {
                case "record":
                    AddRecord(dto, span);
                    break;
                case "outcome":
                    AddOutcome(dto, span);
                    break;
                case "erasure":
                    AddErasure(dto);
                    break;
                case null:
                    Problem("The line has no 'kind'.");
                    break;
                default:
                    // A kind a later addition to version 1 may have made: reported, and kept as it is by a compaction.
                    Problem("The line's 'kind' must be record, outcome or erasure.");
                    _loose.Add(span with { Known = false });
                    break;
            }
        }

        internal Scan Finish()
        {
            var decisions = new List<Entry>(_order.Count);
            foreach (var entry in _order)
            {
                if (!entry.Removed)
                {
                    decisions.Add(entry);
                }
            }

            return new Scan { Decisions = decisions, Loose = _loose, Problems = _problems, SkippedUnknownVersion = _skippedUnknownVersion };
        }

        // Reads just 'kind' and 'version', so that a line of another version is recognised whatever else it holds.
        // Returns false, with the problem added, when the line is not a JSON object or its syntax is broken.
        private bool TryPeek(ReadOnlySpan<byte> line, out string? kind, out int? version)
        {
            kind = null;
            version = null;
            try
            {
                var reader = new Utf8JsonReader(line);
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                {
                    Problem("The line is not a JSON object.");
                    return false;
                }

                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    var isKind = reader.ValueTextEquals("kind"u8);
                    var isVersion = reader.ValueTextEquals("version"u8);
                    reader.Read();
                    if (isKind && reader.TokenType == JsonTokenType.String)
                    {
                        kind = reader.GetString();
                    }
                    else if (isVersion)
                    {
                        version = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) ? number : UnreadableVersion;
                    }

                    reader.Skip();
                }

                return true;
            }
            catch (JsonException ex)
            {
                Problem(Syntax(ex));
                return false;
            }
            catch (InvalidOperationException)
            {
                // An escape that is not valid text, such as a lone surrogate, in 'kind'.
                Problem("The line is not a valid record line.");
                return false;
            }
        }

        // The parser's message can quote the text that failed, which may be state: say only where.
        private static string Syntax(JsonException ex)
            => ex.BytePositionInLine is { } position
                ? $"The line is not a valid record line: it stops making sense at byte {RecordLine.Number((int)Math.Min(position, int.MaxValue))}."
                : "The line is not a valid record line.";

        private void AddRecord(LineDto dto, LineSpan span)
        {
            if (!RecordLineConverter.TryToRecord(dto, out var record, out var message))
            {
                Problem(message);
                return;
            }

            if (_byId.TryGetValue(record.DecisionId, out var existing))
            {
                // A higher attempt replaces a lower one, and an equal one replaces it too: the last write wins.
                // A lower one that turns up later changes nothing.
                if (record.Attempt >= existing.Attempt)
                {
                    Unindex(existing);
                    existing.Attempt = record.Attempt;
                    existing.ErasureKey = record.ErasureKey;
                    existing.Record = span;
                    Index(existing);
                }

                return;
            }

            var entry = new Entry(record.DecisionId, record.ErasureKey, record.Attempt, span);
            _byId[record.DecisionId] = entry;
            _order.Add(entry);
            Index(entry);
        }

        private void AddOutcome(LineDto dto, LineSpan span)
        {
            if (string.IsNullOrEmpty(dto.DecisionId))
            {
                Problem("The outcome line has no 'decisionId'.");
                return;
            }

            if (dto.Type is not null and not ("confirmed" or "signal"))
            {
                // A type a later addition to version 1 may have made: reported, and kept with its decision by a compaction.
                Problem("The outcome line's 'type' must be confirmed or signal.");
                if (_byId.TryGetValue(dto.DecisionId, out var owner))
                {
                    owner.Outcomes.Add(span with { Known = false });
                }
                else if (!_erasedIds.Contains(dto.DecisionId))
                {
                    _loose.Add(span with { Known = false });
                }

                return;
            }

            if (!RecordLineConverter.TryToOutcome(dto, out _, out var message))
            {
                Problem(message);
                return;
            }

            if (_byId.TryGetValue(dto.DecisionId, out var entry))
            {
                entry.Outcomes.Add(span);
            }
            else if (!_erasedIds.Contains(dto.DecisionId))
            {
                // The line is ignored. The id is not in the message: ids are not logged.
                Problem("The outcome line belongs to a decision with no record before it, so it is ignored.");
            }
        }

        private void AddErasure(LineDto dto)
        {
            if (string.IsNullOrEmpty(dto.ErasureKey) && string.IsNullOrEmpty(dto.DecisionId))
            {
                Problem("The erasure line has neither an 'erasureKey' nor a 'decisionId'.");
                return;
            }

            if (!string.IsNullOrEmpty(dto.DecisionId) && _byId.TryGetValue(dto.DecisionId, out var byId))
            {
                Erase(byId);
            }
            else if (!string.IsNullOrEmpty(dto.DecisionId))
            {
                _erasedIds.Add(dto.DecisionId);
            }

            if (!string.IsNullOrEmpty(dto.ErasureKey) && _byKey.TryGetValue(dto.ErasureKey, out var entries))
            {
                foreach (var entry in entries.ToArray())
                {
                    Erase(entry);
                }
            }
        }

        private void Erase(Entry entry)
        {
            if (entry.Removed)
            {
                return;
            }

            entry.Removed = true;
            Unindex(entry);
            _byId.Remove(entry.DecisionId);
            _erasedIds.Add(entry.DecisionId);
        }

        private void Index(Entry entry)
        {
            if (entry.ErasureKey is not { } key)
            {
                return;
            }

            if (!_byKey.TryGetValue(key, out var list))
            {
                _byKey[key] = list = [];
            }

            list.Add(entry);
        }

        private void Unindex(Entry entry)
        {
            if (entry.ErasureKey is { } key && _byKey.TryGetValue(key, out var list))
            {
                list.Remove(entry);
            }
        }

        private void Problem(string message) => _problems.Add(new DecisionRecordFileProblem(_lineNumber, message));

        private static bool IsBlank(ReadOnlySpan<byte> line)
        {
            foreach (var b in line)
            {
                if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
