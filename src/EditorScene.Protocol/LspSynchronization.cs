using System.Text.Json.Nodes;
using EditorScene.Text;

namespace EditorScene.Protocol;

public enum LspTextSyncKind { None, Full, Incremental }
public sealed record LspSynchronization(LspTextSyncKind Change, bool OpenClose, bool Save, bool IncludeTextOnSave)
{
    public static LspSynchronization Read(JsonNode? capability)
    {
        if (capability is null) return new(LspTextSyncKind.None, false, false, false);
        var value = capability is JsonObject options ? options["change"]?.GetValue<int>() ?? 0 : capability.GetValue<int>();
        if (value is < 0 or > 2) throw new InvalidDataException("Invalid LSP textDocumentSync kind.");
        if (capability is not JsonObject settings) return new((LspTextSyncKind)value, value != 0, false, false);
        var save = settings["save"];
        return new((LspTextSyncKind)value, settings["openClose"]?.GetValue<bool>() ?? false,
            save is JsonObject || save is JsonValue enabled && enabled.TryGetValue<bool>(out var yes) && yes,
            save is JsonObject saveOptions && (saveOptions["includeText"]?.GetValue<bool>() ?? false));
    }
}

/// <summary>One minimal UTF-16 change, with boundaries outside surrogate pairs and CRLF delimiters.</summary>
public static class LspTextChange
{
    public static JsonObject Create(DocumentSnapshot before, DocumentSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after);
        if (before.DocumentId != after.DocumentId || after.Revision < before.Revision)
            throw new ArgumentException("Changes require forward snapshots of the same document.", nameof(after));
        var prefix = 0; var limit = Math.Min(before.Length, after.Length);
        while (prefix < limit && before.TextSource.GetCharAt(prefix) == after.TextSource.GetCharAt(prefix)) prefix++;
        while (prefix > 0 && (SplitsSequence(before, prefix) || SplitsSequence(after, prefix))) prefix--;
        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix &&
            before.TextSource.GetCharAt(before.Length - suffix - 1) == after.TextSource.GetCharAt(after.Length - suffix - 1)) suffix++;
        while (suffix > 0 && (SplitsSequence(before, before.Length - suffix) || SplitsSequence(after, after.Length - suffix))) suffix--;
        return new JsonObject
        {
            ["range"] = Range(before.GetPosition(prefix), before.GetPosition(before.Length - suffix)),
            ["text"] = after.GetText(prefix, after.Length - prefix - suffix)
        };
    }
    private static bool SplitsSequence(DocumentSnapshot snapshot, int offset) => offset > 0 && offset < snapshot.Length &&
        ((char.IsHighSurrogate(snapshot.TextSource.GetCharAt(offset - 1)) && char.IsLowSurrogate(snapshot.TextSource.GetCharAt(offset))) ||
         (snapshot.TextSource.GetCharAt(offset - 1) == '\r' && snapshot.TextSource.GetCharAt(offset) == '\n'));
    public static JsonObject Position(TextPosition position) => new() { ["line"] = position.Line, ["character"] = position.Character };
    public static JsonObject Range(TextPosition start, TextPosition end) => new() { ["start"] = Position(start), ["end"] = Position(end) };
}
