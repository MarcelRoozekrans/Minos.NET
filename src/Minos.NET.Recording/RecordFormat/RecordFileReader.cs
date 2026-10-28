using System.Buffers;
using System.Text.Json;

namespace Minos.Recording.RecordFormat;

/// <summary>
/// Reads a record file in one pass, into the decisions it describes: outcomes are attached to their record in file
/// order, an erasure removes the records before it, and a later record of a decision replaces the earlier one. A line
/// that cannot be used is a problem with a fixed message, and the lines around it are still read.
/// </summary>
internal static class RecordFileReader
{
    internal static async Task<List<RecordedDecision>> ReadAsync(
        string path, ICollection<DecisionRecordFileProblem>? problems, CancellationToken cancellationToken)
    {
        var fold = new Fold(problems);
        var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                fold.Feed(chunk.AsSpan(0, read));
            }

            fold.End();
        }

        return fold.Finish();
    }

    private sealed class Fold(ICollection<DecisionRecordFileProblem>? problems)
    {
        private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
        private readonly HashSet<string> _erasedIds = new(StringComparer.Ordinal);
        private readonly List<Entry> _order = [];
        private readonly ArrayBufferWriter<byte> _line = new();
        private int _lineNumber;

        // Takes the next bytes of the file, and reads each line they end.
        internal void Feed(ReadOnlySpan<byte> bytes)
        {
            int end;
            while ((end = bytes.IndexOf((byte)'\n')) >= 0)
            {
                _line.Write(bytes[..end]);
                Add(_line.WrittenSpan);
                _line.Clear();
                bytes = bytes[(end + 1)..];
            }

            _line.Write(bytes);
        }

        // Reads the last line, when the file does not end with a line feed: a cut line is reported, not thrown.
        internal void End()
        {
            if (_line.WrittenCount > 0)
            {
                Add(_line.WrittenSpan);
                _line.Clear();
            }
        }

        private void Add(ReadOnlySpan<byte> line)
        {
            _lineNumber++;
            if (_lineNumber == 1 && line.StartsWith("﻿"u8))
            {
                line = line[3..];
            }

            if (!line.IsEmpty && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            if (line.Trim(" \t\r"u8).IsEmpty)
            {
                return;
            }

            LineDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize(line, RecordJsonContext.Default.LineDto);
            }
            catch (JsonException ex)
            {
                // The parser's message can quote the text that failed, which may be state: say only where.
                Problem(ex.BytePositionInLine is { } position
                    ? $"The line is not a valid record line: it stops making sense at byte {RecordLine.Number((int)Math.Min(position, int.MaxValue))}."
                    : "The line is not a valid record line.");
                return;
            }
            catch (InvalidOperationException)
            {
                // An escape that is not valid text, such as a lone surrogate, in a member read as a string.
                Problem("The line is not a valid record line.");
                return;
            }

            if (dto is null)
            {
                Problem("The line is not a JSON object.");
                return;
            }

            if (dto.Version is not { } version)
            {
                Problem("The line has no 'version'.");
                return;
            }

            if (version is not (RecordLine.Version or RecordLine.VersionWithSource))
            {
                Problem($"The line has record format version {RecordLine.Number(version)}; this reader reads versions {RecordLine.Number(RecordLine.Version)} and {RecordLine.Number(RecordLine.VersionWithSource)}.");
                return;
            }

            switch (dto.Kind)
            {
                case "record":
                    AddRecord(dto);
                    break;
                case "outcome":
                    AddOutcome(dto);
                    break;
                case "erasure":
                    AddErasure(dto);
                    break;
                case null:
                    Problem("The line has no 'kind'.");
                    break;
                default:
                    Problem("The line's 'kind' must be record, outcome or erasure.");
                    break;
            }
        }

        internal List<RecordedDecision> Finish()
        {
            var decisions = new List<RecordedDecision>(_order.Count);
            foreach (var entry in _order)
            {
                if (!entry.Removed)
                {
                    decisions.Add(new RecordedDecision(entry.Record, DecisionRecord.Freeze(entry.Outcomes)));
                }
            }

            return decisions;
        }

        private void AddRecord(LineDto dto)
        {
            if (!RecordLineConverter.TryToRecord(dto, out var record, out var message))
            {
                Problem(message);
                return;
            }

            if (_byId.TryGetValue(record.DecisionId, out var existing))
            {
                // A later record of the same decision replaces the earlier one, and keeps its place and its outcomes.
                existing.Record = record;
                return;
            }

            var entry = new Entry(record);
            _byId[record.DecisionId] = entry;
            _order.Add(entry);
        }

        private void AddOutcome(LineDto dto)
        {
            if (string.IsNullOrEmpty(dto.DecisionId))
            {
                Problem("The outcome line has no 'decisionId'.");
                return;
            }

            if (!RecordLineConverter.TryToOutcome(dto, out var outcome, out var message))
            {
                Problem(message);
                return;
            }

            if (_byId.TryGetValue(dto.DecisionId, out var entry))
            {
                entry.Outcomes.Add(outcome);
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

            if (!string.IsNullOrEmpty(dto.DecisionId))
            {
                if (_byId.TryGetValue(dto.DecisionId, out var byId))
                {
                    Erase(byId);
                }

                _erasedIds.Add(dto.DecisionId);
            }

            if (!string.IsNullOrEmpty(dto.ErasureKey))
            {
                foreach (var entry in _order)
                {
                    if (!entry.Removed && string.Equals(entry.Record.ErasureKey, dto.ErasureKey, StringComparison.Ordinal))
                    {
                        Erase(entry);
                    }
                }
            }
        }

        private void Erase(Entry entry)
        {
            entry.Removed = true;
            _byId.Remove(entry.Record.DecisionId);
            _erasedIds.Add(entry.Record.DecisionId);
        }

        private void Problem(string message) => problems?.Add(new DecisionRecordFileProblem(_lineNumber, message));
    }

    private sealed class Entry(DecisionRecord record)
    {
        internal DecisionRecord Record { get; set; } = record;

        internal List<DecisionOutcome> Outcomes { get; } = [];

        internal bool Removed { get; set; }
    }
}
