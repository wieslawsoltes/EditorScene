using System.Collections.Immutable;
using EditorScene.Text;

namespace EditorScene.LanguageServices;

[Flags]
public enum LanguageCapabilities { None = 0, Diagnostics = 1, Classification = 2, Outlining = 4, Symbols = 8, Completion = 16, QuickInfo = 32, SignatureHelp = 64, Formatting = 128, SelectionRanges = 256 }
public enum IssueSeverity { Information, Warning, Error }
public readonly record struct SourceRange(int Offset, int Length) { public int End => checked(Offset + Length); public bool Contains(int offset) => offset >= Offset && offset <= End; }
public sealed record EditorDiagnostic(string Code, string Message, IssueSeverity Severity, SourceRange Range);
public sealed record ClassificationSpan(SourceRange Range, string Kind);
public sealed record FoldingRegion(SourceRange Range, string Caption, bool IsDefinition = false);
public sealed record NavigationSymbol(string Name, string Kind, SourceRange Range, SourceRange SelectionRange, ImmutableArray<NavigationSymbol> Children);
public sealed record CompletionItem(string Label, string InsertText, string Kind = "symbol", string Description = "", SourceRange? ReplaceRange = null, string? SortText = null)
{
    public ImmutableArray<TextEdit> AdditionalTextEdits { get; init; } = [];
}
public sealed record QuickInfo(SourceRange Range, string Signature, string Documentation = "");
public sealed record SignatureItem(string Label, ImmutableArray<string> Parameters, string Documentation = "");
public sealed record SignatureHelp(ImmutableArray<SignatureItem> Signatures, int ActiveSignature, int ActiveParameter);
public sealed record LanguageAnalysis(Guid DocumentId, long Revision, ImmutableArray<EditorDiagnostic> Diagnostics, ImmutableArray<ClassificationSpan> Classifications, ImmutableArray<FoldingRegion> Foldings, ImmutableArray<NavigationSymbol> Symbols)
{
    public static LanguageAnalysis Empty(DocumentSnapshot snapshot) => new(snapshot.DocumentId, snapshot.Revision, [], [], [], []);
}

/// <summary>UI-independent language contract. Providers consume immutable snapshots, never live buffers.</summary>
public interface ISyntaxLanguage
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyList<string> Extensions { get; }
    LanguageCapabilities Capabilities { get; }
    Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<ImmutableArray<CompletionItem>> CompleteAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default) => Task.FromResult(ImmutableArray<CompletionItem>.Empty);
    Task<QuickInfo?> GetQuickInfoAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default) => Task.FromResult<QuickInfo?>(null);
    Task<SignatureHelp?> GetSignatureHelpAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default) => Task.FromResult<SignatureHelp?>(null);
    Task<ImmutableArray<TextEdit>> FormatAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default) => Task.FromResult(ImmutableArray<TextEdit>.Empty);
}

public sealed class LanguageRegistry
{
    private readonly Dictionary<string, ISyntaxLanguage> _languages = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<ISyntaxLanguage> Languages => _languages.Values;
    public void Register(ISyntaxLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (string.IsNullOrWhiteSpace(language.Id)) throw new ArgumentException("A language requires an ID.", nameof(language));
        if (!_languages.TryAdd(language.Id, language)) throw new ArgumentException("Language '" + language.Id + "' is already registered.", nameof(language));
    }
    public ISyntaxLanguage? Get(string id) => _languages.GetValueOrDefault(id);
    public ISyntaxLanguage? ForFile(string fileName) => _languages.Values.FirstOrDefault(l => l.Extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase));
}

public sealed class PlainTextLanguage : ISyntaxLanguage
{
    public string Id => "plaintext";
    public string DisplayName => "Plain text";
    public IReadOnlyList<string> Extensions => [".txt", ".log"];
    public LanguageCapabilities Capabilities => LanguageCapabilities.None;
    public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(LanguageAnalysis.Empty(snapshot)); }
}
