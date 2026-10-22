using System.Buffers;
using System.Text.Json;

namespace Minos.Recording.RecordFormat;

/// <summary>
/// Reads a record file line by line and folds it into the decisions it describes: outcomes are attached to their
/// record in file order, an erasure removes records, and a higher attempt replaces a lower one.
/// </summary>
internal static class RecordFileReader
{
    internal sealed class Result
    {
        internal required List<RecordedDecision> Decisions { get; init; }

        internal required List<DecisionRecordFileProblem> Problems { get; init; }

        // True when a line was skipped only because this reader does not know its version: rewriting the file would lose it.
        internal bool SkippedNewerVersion { get; init; }
    }

    private sealed class Entry(DecisionRecord record)
    {
        internal DecisionRecord Record { get; set; } = record;

        internal List<DecisionOutcome> Outcomes { get; } = [];

        internal bool Removed { get; set; }
    }

    internal static async ValueTask<Result> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var state = new Fold();
        var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var _ = stream.ConfigureAwait(false);

        var chunk = new byte[81920];
        var line = new ArrayBufferWriter<byte>();
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            var start = 0;
            while (start < read)
            {
                var length = Array.IndexOf(chunk, (byte)'\n', start, read - start);
                if (length < 0)
                {
                    line.Write(chunk.AsSpan(start, read - start));
                    break;
                }

                length -= start;
                line.Write(chunk.AsSpan(start, length));
                state.Add(line.WrittenSpan);
                line.Clear();
                start += length + 1;
            }
        }

        if (line.WrittenCount > 0)
        {
            state.Add(line.WrittenSpan);
        }

        return state.Finish();
    }

    private sealed class Fold
    {
        private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Entry>> _byKey = new(StringComparer.Ordinal);
        private readonly HashSet<string> _erasedIds = new(StringComparer.Ordinal);
        private readonly List<Entry> _order = [];
        private readonly List<DecisionRecordFileProblem> _problems = [];
        private int _lineNumber;
        private bool _skippedNewerVersion;

        internal void Add(ReadOnlySpan<byte> line)
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

            if (IsBlank(line))
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

            if (version != RecordLine.Version)
            {
                _skippedNewerVersion = true;
                Problem($"This file uses record format version {RecordLine.Number(version)}; this reader reads version {RecordLine.Number(RecordLine.Version)}.");
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
                default:
                    Problem("The line's 'kind' must be record, outcome or erasure.");
                    break;
            }
        }

        internal Result Finish()
        {
            var decisions = new List<RecordedDecision>(_order.Count);
            foreach (var entry in _order)
            {
                if (!entry.Removed)
                {
                    decisions.Add(new RecordedDecision(entry.Record, DecisionRecord.Freeze(entry.Outcomes)));
                }
            }

            return new Result { Decisions = decisions, Problems = _problems, SkippedNewerVersion = _skippedNewerVersion };
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
                // A higher attempt replaces a lower one. A lower one that turns up later, or an equal one, changes nothing.
                if (record.Attempt >= existing.Record.Attempt)
                {
                    Unindex(existing);
                    existing.Record = record;
                    Index(existing);
                }

                return;
            }

            var entry = new Entry(record);
            _byId[record.DecisionId] = entry;
            _order.Add(entry);
            Index(entry);
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
            _byId.Remove(entry.Record.DecisionId);
            _erasedIds.Add(entry.Record.DecisionId);
        }

        private void Index(Entry entry)
        {
            if (entry.Record.ErasureKey is not { } key)
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
            if (entry.Record.ErasureKey is { } key && _byKey.TryGetValue(key, out var list))
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
