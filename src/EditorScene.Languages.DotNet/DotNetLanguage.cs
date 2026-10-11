using System.Collections.Immutable;
using System.Text;
using EditorScene.LanguageServices;
using EditorScene.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using CS = Microsoft.CodeAnalysis.CSharp.Syntax;
using VB = Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Range = EditorScene.LanguageServices.SourceRange;
using Completion = EditorScene.LanguageServices.CompletionItem;

namespace EditorScene.Languages.DotNet;

/// <summary>
/// Compiler-backed C# and Visual Basic services for individual documents. Additional
/// metadata references may be supplied by a host. Project-system/build integration
/// and Roslyn Features refactorings are deliberately separate concerns.
/// </summary>
public sealed partial class DotNetLanguage : ISyntaxLanguage, ISelectionRangeProvider
{
    private readonly bool _visualBasic;
    private readonly ImmutableArray<MetadataReference>? _references;
    public DotNetLanguage(bool visualBasic = false, IEnumerable<MetadataReference>? references = null)
    { _visualBasic = visualBasic; _references = references?.ToImmutableArray(); }
    public string Id => _visualBasic ? "vb" : "csharp";
    public string DisplayName => _visualBasic ? "Visual Basic" : "C#";
    public IReadOnlyList<string> Extensions => _visualBasic ? [".vb"] : [".cs", ".csx"];
    public LanguageCapabilities Capabilities => LanguageCapabilities.Diagnostics | LanguageCapabilities.Classification | LanguageCapabilities.Outlining | LanguageCapabilities.Symbols | LanguageCapabilities.Completion | LanguageCapabilities.QuickInfo | LanguageCapabilities.SignatureHelp | LanguageCapabilities.Formatting | LanguageCapabilities.SelectionRanges;
    public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default) => Task.Run(() => AnalyzeCached(snapshot, cancellationToken), cancellationToken);

    private LanguageAnalysis AnalyzeCore(DocumentSnapshot snapshot, CachedModel model, CancellationToken token)
    {
        var root = model.Tree.GetRoot(token);
        var diagnostics = model.Compilation.GetDiagnostics(token).Where(d => d.Location.IsInSource && d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning or DiagnosticSeverity.Info)
            .Select(d => new EditorDiagnostic(d.Id, d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), d.Severity == DiagnosticSeverity.Error ? IssueSeverity.Error : d.Severity == DiagnosticSeverity.Warning ? IssueSeverity.Warning : IssueSeverity.Information, ToRange(d.Location.SourceSpan))).ToImmutableArray();
        var styles = ImmutableArray.CreateBuilder<ClassificationSpan>();
        foreach (var syntaxToken in root.DescendantTokens(descendIntoTrivia: false))
        {
            token.ThrowIfCancellationRequested();
            var kind = _visualBasic ? ((Microsoft.CodeAnalysis.VisualBasic.SyntaxKind)syntaxToken.RawKind).ToString() : ((Microsoft.CodeAnalysis.CSharp.SyntaxKind)syntaxToken.RawKind).ToString();
            string? classification = kind.EndsWith("Keyword", StringComparison.Ordinal) ? "keyword" : kind.Contains("String", StringComparison.Ordinal) || kind.Contains("CharacterLiteral", StringComparison.Ordinal) ? "string" : kind.Contains("NumericLiteral", StringComparison.Ordinal) || kind.Contains("IntegerLiteral", StringComparison.Ordinal) || kind.Contains("FloatingLiteral", StringComparison.Ordinal) || kind.Contains("DecimalLiteral", StringComparison.Ordinal) ? "number" : null;
            if (kind == "IdentifierToken")
            {
                var parent = syntaxToken.Parent;
                var symbol = parent is null ? null : FindSymbol(model.SemanticModel, parent, token);
                classification = symbol switch { INamedTypeSymbol => "type", IMethodSymbol => "function", IPropertySymbol => "property", IFieldSymbol => "field", IParameterSymbol => "parameter", INamespaceSymbol => "namespace", _ => null };
            }
            if (classification is not null && syntaxToken.Span.Length > 0) styles.Add(new(ToRange(syntaxToken.Span), classification));
        }
        var folds = ImmutableArray.CreateBuilder<FoldingRegion>();
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: false))
        {
            var kind = _visualBasic ? ((Microsoft.CodeAnalysis.VisualBasic.SyntaxKind)trivia.RawKind).ToString() : ((Microsoft.CodeAnalysis.CSharp.SyntaxKind)trivia.RawKind).ToString();
            if (kind.Contains("Comment", StringComparison.Ordinal) || kind.Contains("DisabledText", StringComparison.Ordinal) || kind.Contains("Directive", StringComparison.Ordinal))
            {
                if (trivia.Span.Length > 0) styles.Add(new(ToRange(trivia.Span), kind.Contains("Directive", StringComparison.Ordinal) ? "preprocessor" : "comment"));
                if (IsMultiline(model.Tree, trivia.Span)) folds.Add(new(ToRange(trivia.Span), kind.Contains("Comment", StringComparison.Ordinal) ? "/* … */" : "…"));
            }
        }
        foreach (var node in root.DescendantNodes())
        {
            token.ThrowIfCancellationRequested();
            if (!IsFoldable(node) || !IsMultiline(model.Tree, node.Span)) continue;
            var span = node.Span;
            if (!_visualBasic)
            {
                var open = node.ChildTokens().FirstOrDefault(t => t.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.OpenBraceToken);
                var close = node.ChildTokens().LastOrDefault(t => t.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.CloseBraceToken);
                if (open.RawKind != 0 && close.RawKind != 0) span = TextSpan.FromBounds(open.SpanStart, close.Span.End);
            }
            folds.Add(new(ToRange(span), "…", node is CS.BaseTypeDeclarationSyntax or CS.BaseNamespaceDeclarationSyntax or VB.TypeBlockSyntax));
        }
        var symbols = BuildSymbols(root, model.SemanticModel, token);
        var nonOverlappingStyles = styles.OrderBy(s => s.Range.Offset).ThenByDescending(s => s.Range.Length).ToImmutableArray();
        return new(snapshot.DocumentId, snapshot.Revision, diagnostics, nonOverlappingStyles, folds.DistinctBy(f => f.Range).OrderBy(f => f.Range.Offset).ThenByDescending(f => f.Range.Length).ToImmutableArray(), symbols);
    }
    private static bool IsMultiline(SyntaxTree tree, TextSpan span) => tree.GetLineSpan(span).StartLinePosition.Line != tree.GetLineSpan(span).EndLinePosition.Line;
    private bool IsFoldable(SyntaxNode node) => _visualBasic
        ? node is VB.NamespaceBlockSyntax or VB.TypeBlockSyntax or VB.MethodBlockBaseSyntax or VB.PropertyBlockSyntax or VB.MultiLineIfBlockSyntax or VB.ForOrForEachBlockSyntax or VB.TryBlockSyntax or VB.WhileBlockSyntax or VB.SelectBlockSyntax
        : node is CS.BaseNamespaceDeclarationSyntax or CS.BaseTypeDeclarationSyntax or CS.BlockSyntax or CS.AccessorListSyntax or CS.InitializerExpressionSyntax;
    private static ImmutableArray<NavigationSymbol> BuildSymbols(SyntaxNode root, SemanticModel model, CancellationToken token)
    {
        var result = ImmutableArray.CreateBuilder<NavigationSymbol>();
        foreach (var node in root.ChildNodes())
        {
            token.ThrowIfCancellationRequested();
            ISymbol? symbol = null;
            if (node is CS.BaseTypeDeclarationSyntax or CS.BaseMethodDeclarationSyntax or CS.BasePropertyDeclarationSyntax or CS.BaseNamespaceDeclarationSyntax or CS.DelegateDeclarationSyntax or CS.VariableDeclaratorSyntax or VB.TypeBlockSyntax or VB.NamespaceBlockSyntax or VB.MethodBlockBaseSyntax or VB.PropertyBlockSyntax or VB.ModifiedIdentifierSyntax)
                symbol = model.GetDeclaredSymbol(node, token);
            var children = BuildSymbols(node, model, token);
            if (symbol is not null && !symbol.IsImplicitlyDeclared && symbol.Kind is SymbolKind.NamedType or SymbolKind.Namespace or SymbolKind.Method or SymbolKind.Property or SymbolKind.Field or SymbolKind.Event)
            {
                var location = symbol.Locations.FirstOrDefault(l => l.IsInSource)?.SourceSpan ?? node.Span;
                result.Add(new(symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), symbol.Kind.ToString(), ToRange(node.Span), ToRange(location), children));
            }
            else result.AddRange(children);
        }
        return result.ToImmutable();
    }
    private static ISymbol? FindSymbol(SemanticModel model, SyntaxNode node, CancellationToken token)
    {
        var info = model.GetSymbolInfo(node, token);
        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault() ?? model.GetDeclaredSymbol(node, token);
    }
    private static Range ToRange(TextSpan span) => new(span.Start, span.Length);
    private static void ValidateOffset(DocumentSnapshot snapshot, int offset)
    { if ((uint)offset > (uint)snapshot.Length) throw new ArgumentOutOfRangeException(nameof(offset)); }

    public Task<ImmutableArray<Completion>> CompleteAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        ValidateOffset(snapshot, offset); var model = GetModel(snapshot, cancellationToken);
        var root = model.Tree.GetRoot(cancellationToken);
        var start = offset;
        while (start > 0 && (char.IsLetterOrDigit(snapshot.Text[start - 1]) || snapshot.Text[start - 1] is '_' or '@')) start--;
        var prefix = snapshot.Text[start..offset]; var replace = new Range(start, offset - start);
        var before = start - 1; while (before >= 0 && char.IsWhiteSpace(snapshot.Text[before])) before--;
        var isMember = before >= 0 && snapshot.Text[before] == '.';
        IEnumerable<ISymbol> symbols;
        if (isMember && before > 0)
        {
            var accessToken = root.FindToken(before);
            SyntaxNode? expression = accessToken.Parent?.AncestorsAndSelf().OfType<CS.MemberAccessExpressionSyntax>().FirstOrDefault()?.Expression
                ?? (SyntaxNode?)accessToken.Parent?.AncestorsAndSelf().OfType<VB.MemberAccessExpressionSyntax>().FirstOrDefault()?.Expression;
            expression ??= root.FindToken(before - 1).Parent;
            var info = expression is null ? default : model.SemanticModel.GetSymbolInfo(expression, cancellationToken);
            var symbol = info.Symbol;
            var container = symbol as INamespaceOrTypeSymbol ?? (expression is null ? null : model.SemanticModel.GetTypeInfo(expression, cancellationToken).Type);
            var staticOnly = symbol is INamedTypeSymbol or INamespaceSymbol;
            symbols = container is null ? [] : model.SemanticModel.LookupSymbols(offset, container, includeReducedExtensionMethods: !staticOnly).Where(s => !staticOnly || s.IsStatic || s is INamespaceOrTypeSymbol);
        }
        else symbols = model.SemanticModel.LookupSymbols(offset);
        var comparison = _visualBasic ? StringComparison.OrdinalIgnoreCase : StringComparison.OrdinalIgnoreCase;
        var entries = symbols.Where(s => !s.IsImplicitlyDeclared && s.Name.Length > 0 && !s.Name.StartsWith('<') && s.CanBeReferencedByName && s.Name.StartsWith(prefix.TrimStart('@'), comparison))
            .Select(s => new Completion(s.Name, EscapeIdentifier(s.Name), s.Kind.ToString(), s.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), replace));
        if (!isMember)
        {
            var keywords = _visualBasic
                ? Enum.GetValues<Microsoft.CodeAnalysis.VisualBasic.SyntaxKind>().Where(Microsoft.CodeAnalysis.VisualBasic.SyntaxFacts.IsKeywordKind).Select(Microsoft.CodeAnalysis.VisualBasic.SyntaxFacts.GetText)
                : Enum.GetValues<Microsoft.CodeAnalysis.CSharp.SyntaxKind>().Where(Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsKeywordKind).Select(Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetText);
            entries = entries.Concat(keywords.Where(k => !string.IsNullOrEmpty(k) && k.StartsWith(prefix, comparison)).Select(k => new Completion(k, k, "keyword", "Language keyword", replace)));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return entries.DistinctBy(e => (e.Label, e.Kind)).OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase).Take(1000).ToImmutableArray();
    }, cancellationToken);
    private string EscapeIdentifier(string name) => _visualBasic
        ? Microsoft.CodeAnalysis.VisualBasic.SyntaxFacts.GetKeywordKind(name) != Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.None ? "[" + name + "]" : name
        : Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None ? "@" + name : name;

    public Task<QuickInfo?> GetQuickInfoAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default) => Task.Run<QuickInfo?>(() =>
    {
        ValidateOffset(snapshot, offset); if (snapshot.Length == 0) return null;
        var model = GetModel(snapshot, cancellationToken); var token = model.Tree.GetRoot(cancellationToken).FindToken(Math.Min(offset, snapshot.Length - 1));
        for (var node = token.Parent; node is not null && node.Span.Length < 4096; node = node.Parent)
        {
            var symbol = FindSymbol(model.SemanticModel, node, cancellationToken);
            if (symbol is not null) return new(ToRange(token.Span), symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), Documentation(symbol, cancellationToken));
        }
        return null;
    }, cancellationToken);

    public Task<SignatureHelp?> GetSignatureHelpAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default) => Task.Run<SignatureHelp?>(() =>
    {
        ValidateOffset(snapshot, offset); if (offset == 0) return null;
        var model = GetModel(snapshot, cancellationToken); var root = model.Tree.GetRoot(cancellationToken);
        var token = root.FindToken(Math.Min(offset - 1, snapshot.Length - 1));
        SyntaxNode? expression = null; var activeParameter = 0;
        var cs = token.Parent?.AncestorsAndSelf().OfType<CS.ArgumentListSyntax>().FirstOrDefault();
        var vb = token.Parent?.AncestorsAndSelf().OfType<VB.ArgumentListSyntax>().FirstOrDefault();
        if (cs?.Parent is CS.InvocationExpressionSyntax invocation) { expression = invocation.Expression; activeParameter = cs.Arguments.GetSeparators().Count(s => s.SpanStart < offset); }
        else if (cs?.Parent is CS.ObjectCreationExpressionSyntax creation) { expression = creation; activeParameter = cs.Arguments.GetSeparators().Count(s => s.SpanStart < offset); }
        else if (vb?.Parent is VB.InvocationExpressionSyntax vbInvocation) { expression = vbInvocation.Expression; activeParameter = vb.Arguments.GetSeparators().Count(s => s.SpanStart < offset); }
        if (expression is null) return null;
        IEnumerable<ISymbol> members = model.SemanticModel.GetMemberGroup(expression, cancellationToken);
        var info = model.SemanticModel.GetSymbolInfo(expression, cancellationToken);
        if (info.Symbol is not null) members = members.Prepend(info.Symbol);
        members = members.Concat(info.CandidateSymbols);
        var methods = members.OfType<IMethodSymbol>().Distinct(SymbolEqualityComparer.Default).OfType<IMethodSymbol>().ToArray();
        if (methods.Length == 0) return null;
        var signatures = methods.Select(method => new SignatureItem(method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), method.Parameters.Select(p => p.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)).ToImmutableArray(), Documentation(method, cancellationToken))).ToImmutableArray();
        var selected = Array.FindIndex(methods, m => activeParameter < m.Parameters.Length || m.Parameters.LastOrDefault()?.IsParams == true);
        return new(signatures, Math.Max(0, selected), activeParameter);
    }, cancellationToken);
    private static string Documentation(ISymbol symbol, CancellationToken token)
    {
        var xml = symbol.GetDocumentationCommentXml(cancellationToken: token);
        if (string.IsNullOrWhiteSpace(xml)) return "";
        try
        {
            using var reader = System.Xml.XmlReader.Create(new StringReader(xml), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
            var document = System.Xml.Linq.XDocument.Load(reader);
            return document.Root?.Element("summary")?.Value.Trim() ?? "";
        }
        catch (System.Xml.XmlException) { return ""; }
    }
    public Task<ImmutableArray<TextEdit>> FormatAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var root = GetModel(snapshot, cancellationToken).Tree.GetRoot(cancellationToken);
        var eol = snapshot.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var formatted = root.NormalizeWhitespace("    ", eol, elasticTrivia: false).ToFullString();
        cancellationToken.ThrowIfCancellationRequested();
        return formatted == snapshot.Text ? ImmutableArray<TextEdit>.Empty : [new TextEdit(0, snapshot.Length, formatted)];
    }, cancellationToken);
}
