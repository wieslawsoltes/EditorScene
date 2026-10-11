using System.Collections.Immutable;
using System.Text;
using EditorScene.LanguageServices;
using EditorScene.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace EditorScene.Languages.DotNet;

public readonly record struct DotNetCacheStatistics(int CachedDocuments, long ModelBuilds, long CacheHits, long ColdParses, long IncrementalUpdates, long CompilationReuses, long AnalysisBuilds);

public sealed partial class DotNetLanguage
{
    private const int MaximumCachedDocuments = 8;
    private readonly object _cacheGate = new();
    private readonly Dictionary<Guid, ModelSlot> _models = [];
    private long _accessClock, _modelBuilds, _cacheHits, _coldParses, _incrementalUpdates, _compilationReuses, _analysisBuilds;
    private sealed class ModelSlot
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal int Users;
        internal long LastAccess;
        internal CachedModel? Model;
    }
    private sealed record CachedModel(DocumentSnapshot Snapshot, SyntaxTree Tree, Compilation Compilation, SemanticModel SemanticModel)
    {
        internal readonly SemaphoreSlim AnalysisGate = new(1, 1);
        internal LanguageAnalysis? Analysis;
    }
    /// <summary>Observed cache work, not timing estimates. At most eight idle document models are retained.</summary>
    public DotNetCacheStatistics CacheStatistics
    {
        get
        {
            lock (_cacheGate) return new(_models.Count, Interlocked.Read(ref _modelBuilds), Interlocked.Read(ref _cacheHits), Interlocked.Read(ref _coldParses),
                Interlocked.Read(ref _incrementalUpdates), Interlocked.Read(ref _compilationReuses), Interlocked.Read(ref _analysisBuilds));
        }
    }
    private ModelSlot Lease(Guid id)
    {
        lock (_cacheGate)
        {
            if (!_models.TryGetValue(id, out var slot))
            {
                if (_models.Count == MaximumCachedDocuments)
                {
                    var idle = _models.Where(e => e.Value.Users == 0).OrderBy(e => e.Value.LastAccess).FirstOrDefault();
                    if (idle.Value is not null) _models.Remove(idle.Key);
                }
                slot = new ModelSlot();
                // Do not evict an active gate and start a second build for the same
                // document. Overflow work is transient when all eight slots are busy.
                if (_models.Count < MaximumCachedDocuments) _models.Add(id, slot);
            }
            slot.Users++; slot.LastAccess = ++_accessClock; return slot;
        }
    }
    private CachedModel GetModel(DocumentSnapshot snapshot, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot); token.ThrowIfCancellationRequested();
        var slot = Lease(snapshot.DocumentId); var entered = false;
        try
        {
            // All provider APIs schedule onto a worker. The gate is per document,
            // cancellable, and shared by analysis, completion and selection requests.
            slot.Gate.Wait(token); entered = true;
            var previous = slot.Model;
            if (previous is not null && previous.Snapshot.Revision == snapshot.Revision && previous.Snapshot.FileName == snapshot.FileName)
            { Interlocked.Increment(ref _cacheHits); return previous; }
            SourceText source;
            if (previous is not null && previous.Snapshot.Revision <= snapshot.Revision)
            {
                var edit = SnapshotTextChanges.GetMinimalChange(previous.Snapshot, snapshot, token);
                source = previous.Tree.GetText(token);
                if (edit.Length != 0 || edit.NewText.Length != 0)
                    source = source.WithChanges(new TextChange(new TextSpan(edit.Offset, edit.Length), edit.NewText));
            }
            else
            {
                using var reader = snapshot.TextSource.CreateReader();
                source = SourceText.From(reader, snapshot.Length, Encoding.UTF8);
            }
            token.ThrowIfCancellationRequested();
            SyntaxTree tree;
            if (previous is not null && previous.Snapshot.Revision <= snapshot.Revision && previous.Snapshot.FileName == snapshot.FileName)
            { tree = previous.Tree.WithChangedText(source); Interlocked.Increment(ref _incrementalUpdates); }
            else
            {
                tree = _visualBasic
                    ? VisualBasicSyntaxTree.ParseText(source, new VisualBasicParseOptions(Microsoft.CodeAnalysis.VisualBasic.LanguageVersion.Latest), snapshot.FileName, token)
                    : CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview, DocumentationMode.Parse), snapshot.FileName, token);
                Interlocked.Increment(ref _coldParses);
            }
            var references = _references ?? ReferenceAssemblyCatalog.Default;
            Compilation compilation;
            if (previous is not null && previous.Snapshot.Revision <= snapshot.Revision)
            { compilation = previous.Compilation.ReplaceSyntaxTree(previous.Tree, tree); Interlocked.Increment(ref _compilationReuses); }
            else compilation = _visualBasic
                ? VisualBasicCompilation.Create("EditorScene_" + snapshot.DocumentId.ToString("N"), [tree], references, new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On, globalImports: [GlobalImport.Parse("System"), GlobalImport.Parse("System.Collections.Generic"), GlobalImport.Parse("System.Linq")]))
                : CSharpCompilation.Create("EditorScene_" + snapshot.DocumentId.ToString("N"), [tree], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
            var model = new CachedModel(snapshot, tree, compilation, compilation.GetSemanticModel(tree, ignoreAccessibility: false));
            token.ThrowIfCancellationRequested(); Interlocked.Increment(ref _modelBuilds);
            // Older immutable snapshots can still be queried, but cannot evict the
            // latest revision from a cache slot shared by live views.
            if (previous is null || previous.Snapshot.Revision <= snapshot.Revision) slot.Model = model;
            return model;
        }
        finally
        {
            if (entered) slot.Gate.Release();
            lock (_cacheGate) slot.Users--;
        }
    }
    private LanguageAnalysis AnalyzeCached(DocumentSnapshot snapshot, CancellationToken token)
    {
        var model = GetModel(snapshot, token); model.AnalysisGate.Wait(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (model.Analysis is not null) return model.Analysis;
            var analysis = AnalyzeCore(snapshot, model, token); token.ThrowIfCancellationRequested();
            model.Analysis = analysis; Interlocked.Increment(ref _analysisBuilds); return analysis;
        }
        finally { model.AnalysisGate.Release(); }
    }
    /// <summary>Syntax-token/trivia and enclosing AST ranges for C# and Visual Basic. No source text is materialized here.</summary>
    public Task<ImmutableArray<SourceRange>> GetSelectionRangesAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        SelectionExpansion.ValidateBoundary(snapshot, offset); cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.Length == 0) return ImmutableArray<SourceRange>.Empty;
        var model = GetModel(snapshot, cancellationToken); var root = model.Tree.GetRoot(cancellationToken);
        var position = Math.Min(offset, snapshot.Length - 1);
        var result = ImmutableArray.CreateBuilder<SourceRange>();
        void Add(TextSpan span)
        {
            if (span.Length == 0 || span.Start > offset || span.End < offset) return;
            if (result.Count == SelectionExpansion.MaximumCandidates) throw new InvalidOperationException("The syntax selection depth limit was exceeded.");
            var range = new SourceRange(span.Start, span.Length); SelectionExpansion.ValidateRange(snapshot, range); result.Add(range);
        }
        var trivia = root.FindTrivia(position, findInsideTrivia: true); Add(trivia.Span);
        var syntaxToken = root.FindToken(position, findInsideTrivia: true); Add(syntaxToken.Span);
        for (var node = syntaxToken.Parent; node is not null; node = node.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = node.GetFirstToken(); var last = node.GetLastToken();
            if ((first.Text, last.Text) is ("(", ")") or ("[", "]") or ("{", "}"))
            {
                // Include content between paired delimiters before the containing
                // node itself, provided the caret is inside that content.
                if (last.SpanStart >= first.Span.End) Add(TextSpan.FromBounds(first.Span.End, last.SpanStart));
            }
            Add(node.Span);
        }
        Add(root.FullSpan);
        return result.Distinct().OrderBy(r => r.Length).ThenBy(r => r.Offset).ToImmutableArray();
    }, cancellationToken);
}
