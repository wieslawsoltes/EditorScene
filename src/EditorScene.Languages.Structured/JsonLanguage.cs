using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using EditorScene.LanguageServices;
using EditorScene.Text;
using Range = EditorScene.LanguageServices.SourceRange;

namespace EditorScene.Languages.Structured;

/// <summary>Strict JSON syntax, structural navigation, folding and formatting. JSON Schema validation is not implied.</summary>
public sealed class JsonLanguage : ISyntaxLanguage
{
    public string Id => "json";
    public string DisplayName => "JSON";
    public IReadOnlyList<string> Extensions => [".json"];
    public LanguageCapabilities Capabilities => LanguageCapabilities.Diagnostics | LanguageCapabilities.Classification | LanguageCapabilities.Outlining | LanguageCapabilities.Symbols | LanguageCapabilities.Formatting | LanguageCapabilities.Completion;
    private sealed record Frame(string Name, string Kind, int Start, List<NavigationSymbol> Children) { public int NextIndex { get; set; } }
    public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default) => Task.Run(() => Analyze(snapshot, cancellationToken), cancellationToken);
    private static LanguageAnalysis Analyze(DocumentSnapshot snapshot, CancellationToken token)
    {
        var styles = ImmutableArray.CreateBuilder<ClassificationSpan>();
        var folds = ImmutableArray.CreateBuilder<FoldingRegion>();
        var symbols = new List<NavigationSymbol>(); var frames = new Stack<Frame>();
        var diagnostics = ImmutableArray.CreateBuilder<EditorDiagnostic>();
        var bytes = Encoding.UTF8.GetBytes(snapshot.Text); var offsets = BuildOffsetMap(snapshot.Text, bytes.Length);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 256 });
        string? property = null;
        try
        {
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                var start = offsets[checked((int)reader.TokenStartIndex)]; var end = offsets[checked((int)reader.BytesConsumed)];
                var range = new Range(start, end - start);
                if (reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.String or JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null)
                {
                    // Property-name BytesConsumed may include its colon; color the quoted token only.
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var quoteEnd = start + 1; var escaped = false;
                        while (quoteEnd < snapshot.Length)
                        {
                            var c = snapshot.Text[quoteEnd++];
                            if (!escaped && c == '"') break;
                            if (!escaped && c == '\\') escaped = true; else escaped = false;
                        }
                        range = new(start, quoteEnd - start);
                    }
                    styles.Add(new(range, reader.TokenType == JsonTokenType.PropertyName ? "property" : reader.TokenType == JsonTokenType.String ? "string" : reader.TokenType == JsonTokenType.Number ? "number" : "keyword"));
                }
                if (reader.TokenType == JsonTokenType.PropertyName) { property = reader.GetString() ?? ""; continue; }
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    var name = property ?? (frames.TryPeek(out var parent) ? $"[{parent.NextIndex++}]" : "$root"); property = null;
                    frames.Push(new(name, reader.TokenType == JsonTokenType.StartArray ? "array" : "object", start, []));
                }
                else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
                {
                    var frame = frames.Pop();
                    var region = new Range(frame.Start, end - frame.Start);
                    var symbol = new NavigationSymbol(frame.Name, frame.Kind, region, new(frame.Start, 1), frame.Children.ToImmutableArray());
                    if (frames.TryPeek(out var parent)) parent.Children.Add(symbol); else symbols.Add(symbol);
                    if (snapshot.GetPosition(frame.Start).Line < snapshot.GetPosition(end).Line) folds.Add(new(region, frame.Kind == "array" ? "[ … ]" : "{ … }"));
                }
                else
                {
                    var name = property ?? (frames.TryPeek(out var parent) ? $"[{parent.NextIndex++}]" : "$value"); property = null;
                    var item = new NavigationSymbol(name, reader.TokenType.ToString().ToLowerInvariant(), range, range, []);
                    if (frames.TryPeek(out var frame)) frame.Children.Add(item); else symbols.Add(item);
                }
            }
            if (reader.TokenType == JsonTokenType.None) diagnostics.Add(new("JSON001", "A JSON value is required.", IssueSeverity.Error, new(0, 0)));
        }
        catch (JsonException exception)
        {
            var line = (int)Math.Clamp(exception.LineNumber ?? 0, 0, snapshot.LineCount - 1);
            var lineOffset = snapshot.GetOffset(new(line, 0));
            var end = line + 1 < snapshot.LineCount ? snapshot.GetOffset(new(line + 1, 0)) : snapshot.Length;
            var lineBytes = Encoding.UTF8.GetBytes(snapshot.Text[lineOffset..end]);
            var bytePosition = (int)Math.Clamp(exception.BytePositionInLine ?? 0, 0, lineBytes.Length);
            var offset = lineOffset + Encoding.UTF8.GetCharCount(lineBytes.AsSpan(0, bytePosition));
            offset = Math.Min(offset, snapshot.Length);
            diagnostics.Add(new("JSON001", exception.Message, IssueSeverity.Error, new(offset, Math.Min(1, snapshot.Length - offset))));
        }
        return new(snapshot.DocumentId, snapshot.Revision, diagnostics.ToImmutable(), styles.ToImmutable(), folds.OrderBy(f => f.Range.Offset).ToImmutableArray(), symbols.ToImmutableArray());
    }
    private static int[] BuildOffsetMap(string text, int byteLength)
    {
        var result = new int[byteLength + 1]; var byteOffset = 0;
        for (var i = 0; i < text.Length;)
        {
            var length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            var count = Encoding.UTF8.GetByteCount(text.AsSpan(i, length));
            for (var j = 0; j < count; j++) result[byteOffset++] = i;
            i += length; result[byteOffset] = i;
        }
        return result;
    }
    public Task<ImmutableArray<TextEdit>> FormatAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var parsed = JsonDocument.Parse(snapshot.Text, new JsonDocumentOptions { MaxDepth = 256 });
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true })) parsed.WriteTo(writer);
        var formatted = Encoding.UTF8.GetString(stream.ToArray());
        cancellationToken.ThrowIfCancellationRequested();
        return formatted == snapshot.Text ? ImmutableArray<TextEdit>.Empty : [new TextEdit(0, snapshot.Length, formatted)];
    }, cancellationToken);
    public Task<ImmutableArray<CompletionItem>> CompleteAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((uint)offset > (uint)snapshot.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var start = offset; while (start > 0 && char.IsLetter(snapshot.Text[start - 1])) start--;
        var prefix = snapshot.Text[start..offset];
        return Task.FromResult(new[] { "true", "false", "null" }.Where(v => v.StartsWith(prefix, StringComparison.Ordinal)).Select(v => new CompletionItem(v, v, "keyword", "JSON literal", new(start, offset - start))).ToImmutableArray());
    }
}
