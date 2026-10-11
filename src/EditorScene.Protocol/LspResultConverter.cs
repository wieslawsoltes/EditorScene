using System.Collections.Immutable;
using System.Text.Json.Nodes;
using EditorScene.LanguageServices;
using EditorScene.Text;

namespace EditorScene.Protocol;

/// <summary>Bounded, data-only conversion. Remote strings are text, never executable UI or commands.</summary>
public static class LspResultConverter
{
    private const int MaxItems = 100000;
    public static int Offset(DocumentSnapshot snapshot, JsonNode? position)
    {
        var line = Integer(position, "line"); var character = Integer(position, "character");
        if (line < 0 || line >= snapshot.LineCount || character < 0) throw new InvalidDataException("LSP position is outside the snapshot.");
        var offset = snapshot.GetOffset(new(line, character));
        if (snapshot.GetPosition(offset) != new TextPosition(line, character)) throw new InvalidDataException("LSP character is outside the line.");
        if (offset > 0 && offset < snapshot.Length && char.IsHighSurrogate(snapshot.TextSource.GetCharAt(offset - 1)) && char.IsLowSurrogate(snapshot.TextSource.GetCharAt(offset)))
            throw new InvalidDataException("LSP position splits a surrogate pair.");
        return offset;
    }
    public static SourceRange Range(DocumentSnapshot snapshot, JsonNode? range)
    {
        var start = Offset(snapshot, range?["start"]); var end = Offset(snapshot, range?["end"]);
        if (end < start) throw new InvalidDataException("LSP range has reversed endpoints.");
        return new(start, end - start);
    }
    private static int Integer(JsonNode? node, string name) => node?[name]?.GetValue<int>() ?? throw new InvalidDataException("Missing LSP integer: " + name);
    private static string Text(JsonNode? node, string name, string fallback = "") => node?[name]?.GetValue<string>() ?? fallback;
    private static JsonArray Array(JsonNode? node)
    {
        if (node is null) return new();
        if (node is not JsonArray result || result.Count > MaxItems) throw new InvalidDataException("Invalid or oversized LSP result array.");
        return result;
    }
    private static JsonObject Object(JsonNode? node) => node as JsonObject ?? throw new InvalidDataException("Expected an LSP object.");
    public static string Documentation(JsonNode? node)
    {
        if (node is null) return "";
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        if (node is JsonObject obj) return Text(obj, "value");
        if (node is JsonArray array) return string.Join("\n\n", Array(array).Select(Documentation));
        throw new InvalidDataException("Invalid LSP documentation.");
    }
    public static ImmutableArray<EditorDiagnostic> Diagnostics(DocumentSnapshot snapshot, JsonNode? result)
    {
        var items = ImmutableArray.CreateBuilder<EditorDiagnostic>();
        foreach (var node in Array(result))
        {
            var item = Object(node); var severity = item["severity"]?.GetValue<int>() ?? 1;
            if (severity is < 1 or > 4) throw new InvalidDataException("Invalid diagnostic severity.");
            var code = item["code"] is JsonValue value && value.TryGetValue<string>(out var label) ? label : item["code"]?.ToJsonString() ?? "LSP";
            items.Add(new(code, Text(item, "message"), severity == 1 ? IssueSeverity.Error : severity == 2 ? IssueSeverity.Warning : IssueSeverity.Information, Range(snapshot, item["range"])));
        }
        return items.OrderBy(d => d.Range.Offset).ThenBy(d => d.Code, StringComparer.Ordinal).ToImmutableArray();
    }
    public static ImmutableArray<TextEdit> Edits(DocumentSnapshot snapshot, JsonNode? result)
    {
        var items = Array(result).Select(node =>
        {
            var item = Object(node); var range = Range(snapshot, item["range"]);
            return new TextEdit(range.Offset, range.Length, item["newText"]?.GetValue<string>() ?? throw new InvalidDataException("Missing edit text."));
        }).OrderBy(e => e.Offset).ThenBy(e => e.Length).ToImmutableArray();
        for (var i = 1; i < items.Length; i++)
            if (items[i - 1].End > items[i].Offset || items[i - 1].Offset == items[i].Offset) throw new InvalidDataException("LSP edits overlap.");
        return items;
    }
    public static ImmutableArray<CompletionItem> Completions(DocumentSnapshot snapshot, int caret, JsonNode? result, Action<string>? warning = null)
    {
        if ((uint)caret > (uint)snapshot.Length) throw new ArgumentOutOfRangeException(nameof(caret));
        var start = caret; while (start > 0 && (char.IsLetterOrDigit(snapshot.TextSource.GetCharAt(start - 1)) || snapshot.TextSource.GetCharAt(start - 1) == '_')) start--;
        var fallback = new SourceRange(start, caret - start);
        var list = result is JsonObject obj ? Array(obj["items"]) : Array(result);
        var defaults = (result as JsonObject)?["itemDefaults"] as JsonObject;
        var items = ImmutableArray.CreateBuilder<CompletionItem>();
        foreach (var node in list)
        {
            var item = Object(node); var label = Text(item, "label"); if (label.Length == 0) continue;
            var format = item["insertTextFormat"]?.GetValue<int>() ?? defaults?["insertTextFormat"]?.GetValue<int>() ?? 1;
            var mode = item["insertTextMode"]?.GetValue<int>() ?? defaults?["insertTextMode"]?.GetValue<int>() ?? 1;
            if (format != 1 || mode != 1 || item["command"] is not null)
            { warning?.Invoke("Skipped completion requiring an unadvertised snippet, indentation mode or executable command: " + label); continue; }
            var insertion = Text(item, "insertText", label); var range = fallback;
            if (item["textEdit"] is JsonObject edit)
            {
                insertion = Text(edit, "newText", insertion);
                range = Range(snapshot, edit["range"] ?? edit["replace"]);
                if (edit["insert"] is { } insert)
                {
                    var insertRange = Range(snapshot, insert);
                    if (insertRange.Offset != range.Offset || insertRange.End > range.End || caret > insertRange.End) throw new InvalidDataException("Invalid insert/replace completion edit.");
                }
            }
            else if (defaults?["editRange"] is JsonObject defaultRange)
            {
                range = Range(snapshot, defaultRange["replace"] ?? defaultRange);
                insertion = Text(item, "textEditText", insertion);
            }
            if (snapshot.GetPosition(range.Offset).Line != snapshot.GetPosition(range.End).Line || caret < range.Offset || caret > range.End)
                throw new InvalidDataException("Completion edit must be single-line and contain the request caret.");
            var additional = Edits(snapshot, item["additionalTextEdits"]);
            _ = new CompletionEditPlan(snapshot, caret, range, insertion, additional);
            var detail = Text(item, "detail"); var doc = Documentation(item["documentation"]);
            items.Add(new CompletionItem(label, insertion, Kind(item["kind"]?.GetValue<int>() ?? 1), string.Join("\n", new[] { detail, doc }.Where(s => s.Length != 0)), range, Text(item, "sortText", label)) { AdditionalTextEdits = additional });
        }
        return items.OrderBy(i => i.SortText, StringComparer.Ordinal).ThenBy(i => i.Label, StringComparer.Ordinal).ToImmutableArray();
    }
    public static QuickInfo? Hover(DocumentSnapshot snapshot, int caret, JsonNode? result)
    {
        if (result is null) return null; var obj = Object(result); var text = Documentation(obj["contents"]);
        if (text.Length == 0) return null;
        var range = obj["range"] is { } remoteRange ? Range(snapshot, remoteRange) : new SourceRange(caret, 0);
        return new(range, text);
    }
    public static SignatureHelp? Signatures(JsonNode? result)
    {
        if (result is null) return null; var obj = Object(result); var data = Array(obj["signatures"]);
        if (data.Count == 0) return null;
        var signatures = ImmutableArray.CreateBuilder<SignatureItem>();
        foreach (var node in data)
        {
            var signature = Object(node); var label = Text(signature, "label"); var parameters = ImmutableArray.CreateBuilder<string>();
            foreach (var parameter in Array(signature["parameters"]))
            {
                var value = Object(parameter)["label"];
                if (value is JsonArray offsets && offsets.Count == 2)
                {
                    var start = offsets[0]!.GetValue<int>(); var end = offsets[1]!.GetValue<int>();
                    if (start < 0 || end < start || end > label.Length) throw new InvalidDataException("Invalid signature label offsets.");
                    parameters.Add(label[start..end]);
                }
                else parameters.Add(value?.GetValue<string>() ?? "");
            }
            signatures.Add(new(label, parameters.ToImmutable(), Documentation(signature["documentation"])));
        }
        var active = obj["activeSignature"]?.GetValue<int>() ?? 0; if (active < 0 || active >= signatures.Count) active = 0;
        var parameterIndex = data[active]?["activeParameter"]?.GetValue<int>() ?? obj["activeParameter"]?.GetValue<int>() ?? 0;
        return new(signatures.ToImmutable(), active, Math.Clamp(parameterIndex, 0, Math.Max(0, signatures[active].Parameters.Length - 1)));
    }
    public static ImmutableArray<NavigationSymbol> Symbols(DocumentSnapshot snapshot, Uri documentUri, JsonNode? result)
    {
        var count = 0;
        NavigationSymbol? Read(JsonNode? node, int depth)
        {
            if (depth > 64 || ++count > MaxItems) throw new InvalidDataException("LSP symbol tree exceeds limits.");
            var item = Object(node); SourceRange range, selection;
            if (item["location"] is JsonObject location)
            {
                if (!Uri.TryCreate(Text(location, "uri"), UriKind.Absolute, out var uri) || uri != documentUri) return null;
                range = selection = Range(snapshot, location["range"]);
            }
            else { range = Range(snapshot, item["range"]); selection = Range(snapshot, item["selectionRange"] ?? item["range"]); }
            if (selection.Offset < range.Offset || selection.End > range.End) throw new InvalidDataException("Symbol selection is outside its range.");
            var children = Array(item["children"]).Select(child => Read(child, depth + 1)).OfType<NavigationSymbol>().OrderBy(s => s.Range.Offset).ToImmutableArray();
            return new(Text(item, "name"), SymbolKind(item["kind"]?.GetValue<int>() ?? 13), range, selection, children);
        }
        return Array(result).Select(node => Read(node, 0)).OfType<NavigationSymbol>().OrderBy(s => s.Range.Offset).ToImmutableArray();
    }
    public static ImmutableArray<FoldingRegion> Foldings(DocumentSnapshot snapshot, JsonNode? result)
    {
        var regions = new List<FoldingRegion>();
        foreach (var node in Array(result))
        {
            var item = Object(node); var first = Integer(item, "startLine"); var last = Integer(item, "endLine");
            if (first < 0 || last < first || last >= snapshot.LineCount) throw new InvalidDataException("Invalid folding lines.");
            var start = item["startCharacter"] is { } sc ? Offset(snapshot, LspTextChange.Position(new(first, sc.GetValue<int>()))) : snapshot.GetOffset(new(first, int.MaxValue));
            var end = item["endCharacter"] is { } ec ? Offset(snapshot, LspTextChange.Position(new(last, ec.GetValue<int>()))) : snapshot.GetOffset(new(last, int.MaxValue));
            if (end > start) regions.Add(new(new(start, end - start), Text(item, "collapsedText", Text(item, "kind", "…"))));
        }
        var resultList = ImmutableArray.CreateBuilder<FoldingRegion>(); var ends = new Stack<int>();
        foreach (var region in regions.OrderBy(r => r.Range.Offset).ThenByDescending(r => r.Range.End))
        {
            while (ends.Count > 0 && region.Range.Offset >= ends.Peek()) ends.Pop();
            if (ends.Count > 0 && region.Range.End > ends.Peek()) throw new InvalidDataException("Crossing LSP folding ranges.");
            if (resultList.Count > 0 && resultList[^1].Range == region.Range) continue;
            resultList.Add(region); ends.Push(region.Range.End);
        }
        return resultList.ToImmutable();
    }
    public static ImmutableArray<ClassificationSpan> SemanticTokens(DocumentSnapshot snapshot, IReadOnlyList<string> legend, JsonNode? result)
    {
        if (result is null) return [];
        var values = Object(result)["data"] as JsonArray ?? throw new InvalidDataException("Missing semantic-token data.");
        if (values.Count % 5 != 0 || values.Count / 5 > MaxItems) throw new InvalidDataException("Invalid semantic-token data length.");
        var items = ImmutableArray.CreateBuilder<ClassificationSpan>(); var line = 0; var character = 0; var previousEnd = 0;
        for (var i = 0; i < values.Count; i += 5)
        {
            var delta = values[i]!.GetValue<int>(); var column = values[i + 1]!.GetValue<int>(); var length = values[i + 2]!.GetValue<int>(); var type = values[i + 3]!.GetValue<int>();
            if (delta < 0 || column < 0 || length <= 0 || type < 0 || type >= legend.Count || values[i + 4]!.GetValue<long>() < 0) throw new InvalidDataException("Invalid semantic token.");
            line = checked(line + delta); character = delta == 0 ? checked(character + column) : column;
            var start = Offset(snapshot, LspTextChange.Position(new(line, character))); var end = Offset(snapshot, LspTextChange.Position(new(line, checked(character + length))));
            if (start < previousEnd) throw new InvalidDataException("Overlapping semantic tokens.");
            previousEnd = end; var kind = legend[type] switch
            { "class" or "interface" or "struct" or "enum" or "typeParameter" => "type", "function" => "method", "regexp" => "string", "modifier" => "keyword", var name => name };
            items.Add(new(new(start, end - start), kind));
        }
        return items.ToImmutable();
    }
    private static string Kind(int value) => value switch { 2 or 3 or 4 => "method", 5 or 10 => "property", 7 or 8 or 9 or 13 or 22 or 25 => "type", 14 => "keyword", 21 => "constant", _ => "symbol" };
    private static string SymbolKind(int value) => value switch { 2 or 3 or 4 => "namespace", 5 or 11 or 23 or 26 => "type", 6 or 9 or 12 => "method", 7 or 8 or 22 => "property", 10 => "enum", _ => "variable" };
}
