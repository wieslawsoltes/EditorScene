using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace EditorScene.Protocol;

/// <summary>
/// Immutable, bounded LSP relative-token integer data. Delta edits refer to the same
/// previous array, may arrive unordered, and can split five-integer token records.
/// No state is changed until the complete candidate has been validated by the caller.
/// </summary>
public sealed class SemanticTokenData
{
    public const int MaximumIntegers = 500000;
    private SemanticTokenData(ImmutableArray<int> data, string? resultId) { Data = data; ResultId = resultId; }
    public ImmutableArray<int> Data { get; }
    public string? ResultId { get; }
    public static SemanticTokenData FromFull(JsonNode? result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (result is null) return new([], null);
        if (result is not JsonObject obj || obj.ContainsKey("edits")) throw new InvalidDataException("Expected a full semantic-token result.");
        var data = ReadData(obj["data"], cancellationToken);
        CheckLength(data.Length); return new(data, ReadId(obj));
    }
    public SemanticTokenData ApplyDelta(JsonNode? result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The protocol permits a delta request to return a replacement full result or null.
        if (result is null || result is JsonObject full && full.ContainsKey("data")) return FromFull(result, cancellationToken);
        if (ResultId is null) throw new InvalidDataException("A delta requires a previous result ID.");
        if (result is not JsonObject obj || obj["edits"] is not JsonArray edits || edits.Count > 100000)
            throw new InvalidDataException("Missing or oversized semantic-token delta edits.");
        var id = ReadId(obj);
        if (edits.Count == 0) return new(Data, id);
        var changes = new List<Edit>(edits.Count); long length = Data.Length; long insertedTotal = 0;
        for (var i = 0; i < edits.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (edits[i] is not JsonObject edit) throw new InvalidDataException("Invalid semantic-token edit.");
            var start = Integer(edit["start"]); var removed = Integer(edit["deleteCount"]);
            if (start > Data.Length || removed > Data.Length - start) throw new InvalidDataException("Semantic-token edit is outside its previous array.");
            var inserted = edit["data"] is null ? ImmutableArray<int>.Empty : ReadData(edit["data"], cancellationToken);
            length += inserted.Length - removed; insertedTotal += inserted.Length;
            if (insertedTotal > MaximumIntegers) throw new InvalidDataException("Semantic-token delta exceeds the size limit.");
            changes.Add(new(start, removed, inserted, i));
        }
        changes.Sort((a, b) => a.Start == b.Start ? a.Order.CompareTo(b.Order) : a.Start.CompareTo(b.Start));
        var cursor = 0;
        foreach (var change in changes)
        {
            if (change.Start < cursor) throw new InvalidDataException("Overlapping semantic-token delta edits.");
            cursor = change.Start + change.Removed;
        }
        CheckLength(checked((int)length));
        var data = ImmutableArray.CreateBuilder<int>((int)length); cursor = 0;
        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            data.AddRange(Data.AsSpan(cursor, change.Start - cursor)); data.AddRange(change.Inserted);
            cursor = change.Start + change.Removed;
        }
        data.AddRange(Data.AsSpan(cursor, Data.Length - cursor));
        return new(data.MoveToImmutable(), id);
    }
    private static ImmutableArray<int> ReadData(JsonNode? node, CancellationToken token)
    {
        if (node is not JsonArray values || values.Count > MaximumIntegers) throw new InvalidDataException("Missing or oversized semantic-token integers.");
        var data = ImmutableArray.CreateBuilder<int>(values.Count);
        for (var i = 0; i < values.Count; i++) { if ((i & 255) == 0) token.ThrowIfCancellationRequested(); data.Add(Integer(values[i])); }
        return data.MoveToImmutable();
    }
    private static int Integer(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) && number >= 0
        ? number : throw new InvalidDataException("Semantic-token integers must be in the LSP uinteger range.");
    private static string? ReadId(JsonObject value)
    {
        if (value["resultId"] is null) return null;
        if (value["resultId"] is JsonValue id && id.TryGetValue<string>(out var text) && text.Length <= 4096) return text;
        throw new InvalidDataException("Invalid or oversized semantic-token result ID.");
    }
    private static void CheckLength(int length)
    { if (length < 0 || length > MaximumIntegers || length % 5 != 0) throw new InvalidDataException("Invalid semantic-token data length."); }
    private sealed record Edit(int Start, int Removed, ImmutableArray<int> Inserted, int Order);
}
