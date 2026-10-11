using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using EditorScene.LanguageServices;
using EditorScene.Text;
using Range = EditorScene.LanguageServices.SourceRange;

namespace EditorScene.Languages.Structured;

/// <summary>XML/XAML syntax services. DTDs, external resources and entity expansion are never enabled.</summary>
public sealed class XmlLanguage(bool xaml = false) : ISyntaxLanguage
{
    public string Id => xaml ? "xaml" : "xml";
    public string DisplayName => xaml ? "XAML" : "XML";
    public IReadOnlyList<string> Extensions => xaml ? [".xaml"] : [".xml", ".config", ".csproj", ".props", ".targets", ".svg"];
    public LanguageCapabilities Capabilities => LanguageCapabilities.Diagnostics | LanguageCapabilities.Classification | LanguageCapabilities.Outlining | LanguageCapabilities.Symbols | LanguageCapabilities.Formatting;
    private static XmlReaderSettings Settings => new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0, MaxCharactersInDocument = 64 * 1024 * 1024, IgnoreComments = false };
    private sealed record Frame(string Name, int Start, int NameStart, List<NavigationSymbol> Children);
    public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default) => Task.Run(() => Analyze(snapshot, cancellationToken), cancellationToken);
    private static LanguageAnalysis Analyze(DocumentSnapshot snapshot, CancellationToken cancellationToken)
    {
        var diagnostics = ImmutableArray.CreateBuilder<EditorDiagnostic>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(snapshot.Text), Settings);
            while (reader.Read()) cancellationToken.ThrowIfCancellationRequested();
        }
        catch (XmlException exception)
        {
            var position = new TextPosition(Math.Clamp(exception.LineNumber - 1, 0, snapshot.LineCount - 1), Math.Max(0, exception.LinePosition - 1));
            var offset = snapshot.GetOffset(position);
            diagnostics.Add(new("XML001", exception.Message, IssueSeverity.Error, new(offset, Math.Min(1, snapshot.Length - offset))));
        }
        var styles = ImmutableArray.CreateBuilder<ClassificationSpan>();
        var folds = ImmutableArray.CreateBuilder<FoldingRegion>();
        var roots = new List<NavigationSymbol>(); var stack = new Stack<Frame>();
        var text = snapshot.Text;
        for (var i = 0; i < text.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (text[i] != '<') { i++; continue; }
            var start = i;
            if (text.AsSpan(i).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal); i = end < 0 ? text.Length : end + 3;
                styles.Add(new(new(start, i - start), "comment"));
                if (snapshot.GetPosition(start).Line < snapshot.GetPosition(i).Line) folds.Add(new(new(start, i - start), "<!-- … -->"));
                continue;
            }
            if (text.AsSpan(i).StartsWith("<![CDATA[", StringComparison.Ordinal))
            {
                var end = text.IndexOf("]]>", i + 9, StringComparison.Ordinal); i = end < 0 ? text.Length : end + 3;
                styles.Add(new(new(start, i - start), "string")); continue;
            }
            if (i + 1 < text.Length && text[i + 1] is '?' or '!')
            {
                var processing = text[i + 1] == '?';
                var end = processing ? text.IndexOf("?>", i + 2, StringComparison.Ordinal) : text.IndexOf('>', i + 1);
                i = end < 0 ? text.Length : end + (processing ? 2 : 1);
                styles.Add(new(new(start, i - start), "preprocessor")); continue;
            }
            i++; var closing = i < text.Length && text[i] == '/'; if (closing) i++;
            var nameStart = i; while (i < text.Length && IsName(text[i])) i++;
            var name = text[nameStart..i];
            if (name.Length == 0) continue;
            styles.Add(new(new(nameStart, i - nameStart), "type"));
            var selfClosing = false;
            while (i < text.Length && text[i] != '>')
            {
                if (text[i] is '\'' or '"')
                {
                    var quote = text[i]; var valueStart = i++;
                    while (i < text.Length && text[i] != quote) i++;
                    if (i < text.Length) i++;
                    styles.Add(new(new(valueStart, i - valueStart), "string"));
                }
                else if (IsName(text[i]))
                {
                    var attributeStart = i++; while (i < text.Length && IsName(text[i])) i++;
                    styles.Add(new(new(attributeStart, i - attributeStart), "property"));
                }
                else { selfClosing = text[i] == '/'; i++; }
            }
            if (i < text.Length) i++;
            if (closing)
            {
                if (stack.TryPeek(out var frame) && frame.Name == name)
                {
                    stack.Pop(); var range = new Range(frame.Start, i - frame.Start);
                    var symbol = new NavigationSymbol(frame.Name, "element", range, new(frame.NameStart, frame.Name.Length), frame.Children.ToImmutableArray());
                    if (stack.TryPeek(out var parent)) parent.Children.Add(symbol); else roots.Add(symbol);
                    if (snapshot.GetPosition(frame.Start).Line < snapshot.GetPosition(i).Line) folds.Add(new(range, $"<{name}> … </{name}>"));
                }
            }
            else if (selfClosing)
            {
                var symbol = new NavigationSymbol(name, "element", new(start, i - start), new(nameStart, name.Length), []);
                if (stack.TryPeek(out var parent)) parent.Children.Add(symbol); else roots.Add(symbol);
            }
            else stack.Push(new(name, start, nameStart, []));
        }
        return new(snapshot.DocumentId, snapshot.Revision, diagnostics.ToImmutable(), styles.ToImmutable(), folds.OrderBy(f => f.Range.Offset).ToImmutableArray(), roots.ToImmutableArray());
    }
    private static bool IsName(char c) => char.IsLetterOrDigit(c) || c is '_' or ':' or '-' or '.';
    public Task<ImmutableArray<TextEdit>> FormatAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var reader = XmlReader.Create(new StringReader(snapshot.Text), Settings);
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var declaration = document.Declaration?.ToString();
        var newline = snapshot.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        XmlWhitespaceFormatter.Apply(document, newline, cancellationToken);
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings
        {
            // Whitespace was inserted only where safe. Automatic writer indentation
            // cannot know about mixed content before its first text node is written.
            Indent = false, OmitXmlDeclaration = declaration is null,
            NewLineChars = newline, NewLineHandling = NewLineHandling.Entitize
        })) document.Save(writer);
        var formatted = builder.ToString();
        if (declaration is not null)
        {
            var end = formatted.IndexOf("?>", StringComparison.Ordinal);
            if (!formatted.StartsWith("<?xml", StringComparison.Ordinal) || end < 0) throw new InvalidDataException("The XML writer did not emit the expected declaration.");
            formatted = declaration + formatted[(end + 2)..];
        }
        cancellationToken.ThrowIfCancellationRequested();
        return formatted == snapshot.Text ? ImmutableArray<TextEdit>.Empty : [new TextEdit(0, snapshot.Length, formatted)];
    }, cancellationToken);
}
