using System.Collections.Immutable;
using System.Text.Json.Nodes;
using EditorScene.LanguageServices;
using EditorScene.Text;

namespace EditorScene.Protocol;

/// <summary>Validated LSP selectionRange conversion. Rejects malformed chains before returning any result.</summary>
public static class LspSelectionRanges
{
    public const int MaximumPositions = 1024;
    public const int MaximumParentDepth = 128;
    public static ImmutableArray<ImmutableArray<SourceRange>> Decode(DocumentSnapshot snapshot, IReadOnlyList<int> offsets, JsonNode? response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(offsets);
        if (offsets.Count > MaximumPositions) throw new ArgumentOutOfRangeException(nameof(offsets));
        foreach (var offset in offsets) SelectionExpansion.ValidateBoundary(snapshot, offset);
        cancellationToken.ThrowIfCancellationRequested();
        if (response is not null && (response is not JsonArray array || array.Count != offsets.Count))
            throw new InvalidDataException("A selectionRange response must have one entry per requested position.");
        var result = ImmutableArray.CreateBuilder<ImmutableArray<SourceRange>>(offsets.Count);
        for (var i = 0; i < offsets.Count; i++)
        {
            var chain = ImmutableArray.CreateBuilder<SourceRange>();
            var node = response?[i]; var depth = 0;
            if (response is not null && node is null) throw new InvalidDataException("A selectionRange array entry cannot be null.");
            var previous = new SourceRange(offsets[i], 0);
            while (node is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++depth > MaximumParentDepth || node is not JsonObject value) throw new InvalidDataException("Invalid or oversized selectionRange parent chain.");
                SourceRange range;
                try
                {
                    range = LspResultConverter.Range(snapshot, value["range"]);
                    SelectionExpansion.ValidateRange(snapshot, range);
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
                { throw new InvalidDataException("Invalid selectionRange coordinates.", error); }
                if (!SelectionExpansion.Encloses(range, previous)) throw new InvalidDataException("A selectionRange parent must contain its child and requested position.");
                if (chain.Count == 0 || range != previous) chain.Add(range);
                previous = range; node = value["parent"];
            }
            result.Add(chain.ToImmutable());
        }
        return result.MoveToImmutable();
    }
}

public sealed partial class LanguageServerClient
{
    public Task<JsonNode?> SelectionRangesAsync(Uri uri, IReadOnlyList<TextPosition> positions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positions);
        if (positions.Count is < 1 or > LspSelectionRanges.MaximumPositions) throw new ArgumentOutOfRangeException(nameof(positions));
        var items = new JsonArray();
        foreach (var position in positions)
        {
            if (position.Line < 0 || position.Character < 0) throw new ArgumentOutOfRangeException(nameof(positions));
            items.Add(LspTextChange.Position(position));
        }
        var parameters = DocumentParameters(uri); parameters["positions"] = items;
        return Request("textDocument/selectionRange", parameters, cancellationToken);
    }
}

public sealed partial class LspSyntaxLanguage : ISelectionRangeProvider
{
    public Task<ImmutableArray<SourceRange>> GetSelectionRangesAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default)
    {
        SelectionExpansion.ValidateBoundary(snapshot, offset); cancellationToken.ThrowIfCancellationRequested();
        return WithSnapshotAsync(snapshot, async token =>
        {
            if (!Capabilities.HasFlag(LanguageCapabilities.SelectionRanges)) return ImmutableArray<SourceRange>.Empty;
            var response = await _client.SelectionRangesAsync(DocumentUri, [snapshot.GetPosition(offset)], token).ConfigureAwait(false);
            return LspSelectionRanges.Decode(snapshot, new[] { offset }, response, token)[0];
        }, cancellationToken);
    }
}
