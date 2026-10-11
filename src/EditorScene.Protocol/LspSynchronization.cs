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
    public static JsonObject Create(DocumentSnapshot before, DocumentSnapshot after, CancellationToken cancellationToken = default)
    {
        var change = SnapshotTextChanges.GetMinimalChange(before, after, cancellationToken);
        return new JsonObject { ["range"] = Range(before.GetPosition(change.Offset), before.GetPosition(change.End)), ["text"] = change.NewText };
    }
    public static JsonObject Position(TextPosition position) => new() { ["line"] = position.Line, ["character"] = position.Character };
    public static JsonObject Range(TextPosition start, TextPosition end) => new() { ["start"] = Position(start), ["end"] = Position(end) };
}
