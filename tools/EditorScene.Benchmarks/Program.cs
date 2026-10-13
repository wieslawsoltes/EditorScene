using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using EditorScene.LanguageServices;
using EditorScene.Protocol;
using EditorScene.Text;

// Descriptive CPU/allocation benchmarks, not hardware-independent timing assertions.
// Baselines reproduce the previous hot paths on valid fixtures, not every validation branch.
var rows = new List<object>();
var text = string.Concat(Enumerable.Repeat("1234567890123456789012345678901234567890\r\n", 100000));
Measure("cold_line_lookup", "previous", () =>
{
    var doc = new EditorDocument(text); var snapshot = new LegacySnapshot(doc.Snapshot());
    return new(() => GC.KeepAlive(snapshot.GetPosition(2_000_000)), doc.Dispose);
});
Measure("cold_line_lookup", "current", () =>
{
    var doc = new EditorDocument(text); var snapshot = doc.Snapshot();
    return new(() => GC.KeepAlive(snapshot.GetPosition(2_000_000)), doc.Dispose);
});
Measure("edit_and_line_lookup", "previous", () =>
{
    var doc = new EditorDocument(text); _ = new LegacySnapshot(doc.Snapshot()).GetPosition(2_000_000);
    return new(() => { doc.Buffer.Insert(2_000_000, "X"); GC.KeepAlive(new LegacySnapshot(doc.Snapshot()).GetPosition(2_000_001)); }, doc.Dispose);
});
Measure("edit_and_line_lookup", "current", () =>
{
    var doc = new EditorDocument(text); _ = doc.Snapshot().GetPosition(2_000_000);
    return new(() => { doc.Buffer.Insert(2_000_000, "X"); GC.KeepAlive(doc.Snapshot().GetPosition(2_000_001)); }, doc.Dispose);
});
Measure("small_lsp_change", "previous", () =>
{
    var doc = new EditorDocument(text); var before = doc.Snapshot(); var index = new LegacySnapshot(before); _ = index.GetPosition(2_000_000);
    doc.Buffer.Insert(2_000_000, "X"); var after = doc.Snapshot();
    return new(() => GC.KeepAlive(LegacyDifference(before, after, index)), doc.Dispose);
});
Measure("small_lsp_change", "current", () =>
{
    var doc = new EditorDocument(text); var before = doc.Snapshot(); _ = before.LineCount; doc.Buffer.Insert(2_000_000, "X"); var after = doc.Snapshot();
    return new(() => GC.KeepAlive(LspTextChange.Create(before, after)), doc.Dispose);
});
var tokenText = string.Concat(Enumerable.Repeat("x ", 10000));
var values = new JsonArray(); for (var i = 0; i < 10000; i++) { values.Add(0); values.Add(i == 0 ? 0 : 2); values.Add(1); values.Add(0); values.Add(0); }
var result = new JsonObject { ["data"] = values };
Measure("decode_10000_semantic_tokens", "previous", () =>
{
    var doc = new EditorDocument(tokenText); var snapshot = new LegacySnapshot(doc.Snapshot()); _ = snapshot.GetPosition(0);
    return new(() => GC.KeepAlive(LegacyTokens(snapshot, values)), doc.Dispose);
});
Measure("decode_10000_semantic_tokens", "current", () =>
{
    var doc = new EditorDocument(tokenText); var snapshot = doc.Snapshot(); _ = snapshot.LineCount;
    return new(() => GC.KeepAlive(LspResultConverter.SemanticTokens(snapshot, new[] { "keyword" }, result)), doc.Dispose);
});
ReviewBenchmarks.Add(rows);
CompilerBenchmarks.Add(rows);
CompletedAnalysisBenchmarks.Add(rows);
WorkspaceBenchmarks.Add(rows);
SnapshotSaveBenchmarks.Add(rows);
WhitespaceBenchmarks.Add(rows);
TabColumnBenchmarks.Add(rows);
var report = new { sourceRevision = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local-working-tree", framework = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), processors = Environment.ProcessorCount,
    documentCharacters = text.Length, documentLines = 100001, samples = 11, warmups = 3, note = "Single-process Stopwatch and current-thread allocation measurements. Baselines reproduce old hot paths on valid fixtures. LSP diff indexes are warm for both versions. Setup/disposal excluded; no physical-GPU claims or absolute timing gate.", rows };
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }); Console.WriteLine(json);
if (args.Length > 0) { var file = Path.GetFullPath(args[0]); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, json + "\n"); }

void Measure(string scenario, string implementation, Func<Probe> setup)
{
    for (var i = 0; i < 3; i++) { var probe = setup(); probe.Run(); probe.Dispose(); }
    var times = new List<double>(11); var bytes = new List<long>(11);
    for (var i = 0; i < 11; i++)
    {
        var probe = setup(); var allocated = GC.GetAllocatedBytesForCurrentThread(); var stamp = Stopwatch.GetTimestamp();
        probe.Run(); times.Add(Stopwatch.GetElapsedTime(stamp).TotalMilliseconds); bytes.Add(GC.GetAllocatedBytesForCurrentThread() - allocated); probe.Dispose();
    }
    times.Sort(); bytes.Sort(); rows.Add(new { scenario, implementation, p50Milliseconds = times[5], p95Milliseconds = times[10], medianAllocatedBytes = bytes[5], sortedElapsedMilliseconds = times.ToArray(), sortedAllocatedBytes = bytes.ToArray() });
}
static JsonObject LegacyDifference(DocumentSnapshot before, DocumentSnapshot after, LegacySnapshot index)
{
    var prefix = 0; var limit = Math.Min(before.Length, after.Length);
    while (prefix < limit && before.TextSource.GetCharAt(prefix) == after.TextSource.GetCharAt(prefix)) prefix++;
    var suffix = 0;
    while (suffix < before.Length - prefix && suffix < after.Length - prefix && before.TextSource.GetCharAt(before.Length - suffix - 1) == after.TextSource.GetCharAt(after.Length - suffix - 1)) suffix++;
    return new JsonObject { ["range"] = LspTextChange.Range(index.GetPosition(prefix), index.GetPosition(before.Length - suffix)), ["text"] = after.GetText(prefix, after.Length - prefix - suffix) };
}
static ImmutableArray<ClassificationSpan> LegacyTokens(LegacySnapshot snapshot, JsonArray values)
{
    var items = ImmutableArray.CreateBuilder<ClassificationSpan>(); var line = 0; var column = 0;
    for (var i = 0; i < values.Count; i += 5)
    {
        var delta = values[i]!.GetValue<int>(); line += delta; column = delta == 0 ? column + values[i + 1]!.GetValue<int>() : values[i + 1]!.GetValue<int>();
        var start = PositionOffset(LspTextChange.Position(new(line, column))); var end = PositionOffset(LspTextChange.Position(new(line, column + values[i + 2]!.GetValue<int>())));
        items.Add(new(new(start, end - start), "keyword"));
    }
    return items.ToImmutable();
    int PositionOffset(JsonNode position) => snapshot.GetOffset(new(position["line"]!.GetValue<int>(), position["character"]!.GetValue<int>()));
}
sealed record Probe(Action Run, Action Dispose);
sealed class LegacySnapshot
{
    private readonly Lazy<string> _text; private readonly Lazy<ImmutableArray<int>> _starts;
    public LegacySnapshot(DocumentSnapshot snapshot)
    {
        _text = new(() => snapshot.TextSource.Text);
        _starts = new(() => { var starts = ImmutableArray.CreateBuilder<int>(); starts.Add(0); for (var i = 0; i < _text.Value.Length; i++)
        { if (_text.Value[i] == '\r') { if (i + 1 < _text.Value.Length && _text.Value[i + 1] == '\n') i++; starts.Add(i + 1); } else if (_text.Value[i] == '\n') starts.Add(i + 1); } return starts.ToImmutable(); });
    }
    public TextPosition GetPosition(int offset) { var index = _starts.Value.BinarySearch(offset); if (index < 0) index = ~index - 1; return new(index, offset - _starts.Value[index]); }
    public int GetOffset(TextPosition position)
    { var starts = _starts.Value; var end = position.Line + 1 < starts.Length ? starts[position.Line + 1] : _text.Value.Length; while (end > starts[position.Line] && _text.Value[end - 1] is '\r' or '\n') end--; return starts[position.Line] + Math.Min(position.Character, end - starts[position.Line]); }
}
