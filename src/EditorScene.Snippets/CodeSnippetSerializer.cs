using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EditorScene.Snippets;

/// <summary>Visual Studio 2005 snippet XML with bounded input and prohibited DTD/external entities.</summary>
public static class CodeSnippetSerializer
{
    public const string Namespace = "http://schemas.microsoft.com/VisualStudio/2005/CodeSnippet";
    public static ImmutableArray<CodeSnippet> Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false, MaxCharactersInDocument = 8 * 1024 * 1024 });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new FormatException("Missing snippet root element.");
        if (root.Name.NamespaceName != Namespace && root.Name.NamespaceName.Length != 0) throw new FormatException("Unexpected snippet XML namespace.");
        var ns = root.Name.Namespace;
        var nodes = root.Name.LocalName switch
        {
            "CodeSnippet" => new[] { root },
            "CodeSnippets" => root.Elements(ns + "CodeSnippet").ToArray(),
            _ => throw new FormatException("Expected CodeSnippets or CodeSnippet.")
        };
        if (nodes.Length is 0 or > 512) throw new FormatException("A snippet file must contain between one and 512 snippets.");
        var results = ImmutableArray.CreateBuilder<CodeSnippet>();
        foreach (var node in nodes)
        {
            if (node.Attribute("Format")?.Value is { } format && format != "1.0.0") throw new NotSupportedException("Unsupported snippet format: " + format);
            var header = node.Element(ns + "Header") ?? throw new FormatException("Missing Header.");
            var body = node.Element(ns + "Snippet") ?? throw new FormatException("Missing Snippet.");
            var code = body.Element(ns + "Code") ?? throw new FormatException("Missing Code.");
            var delimiterText = code.Attribute("Delimiter")?.Value ?? "$";
            if (delimiterText.Length != 1) throw new FormatException("A snippet delimiter must be one character.");
            var fields = new List<SnippetDeclaration>();
            foreach (var field in body.Element(ns + "Declarations")?.Elements() ?? [])
            {
                if (field.Name.Namespace != ns || field.Name.LocalName is not ("Literal" or "Object")) throw new NotSupportedException("Unsupported snippet declaration: " + field.Name);
                var editable = field.Attribute("Editable")?.Value;
                fields.Add(new(Required(field, ns + "ID"), field.Element(ns + "Default")?.Value ?? "", editable is null || XmlConvert.ToBoolean(editable),
                    Value(field, "ToolTip"), Value(field, "Function"), field.Name.LocalName == "Object", Value(field, "Type")));
            }
            var types = (SnippetTypes)0;
            foreach (var type in header.Element(ns + "SnippetTypes")?.Elements(ns + "SnippetType") ?? [])
                types |= type.Value switch { "Expansion" => SnippetTypes.Expansion, "SurroundsWith" => SnippetTypes.SurroundsWith, "Refactoring" => SnippetTypes.Refactoring, _ => throw new NotSupportedException("Unsupported snippet type: " + type.Value) };
            if (types == 0) types = SnippetTypes.Expansion;
            results.Add(new CodeSnippet(Required(header, ns + "Title"), Value(header, "Shortcut"), code.Attribute("Language")?.Value ?? throw new FormatException("Missing snippet language."), code.Value,
                fields, types, delimiterText[0], Value(header, "Description"), Value(header, "Author"), Value(header, "HelpUrl"), code.Attribute("Kind")?.Value ?? "",
                body.Element(ns + "Imports")?.Elements(ns + "Import").Select(i => Required(i, ns + "Namespace")),
                body.Element(ns + "References")?.Elements(ns + "Reference").Select(r => new SnippetReference(Required(r, ns + "Assembly"), Value(r, "Url"))),
                header.Element(ns + "Keywords")?.Elements(ns + "Keyword").Select(k => k.Value)));
        }
        return results.ToImmutable();
    }
    private static string Value(XElement parent, string name) => parent.Element(parent.Name.Namespace + name)?.Value ?? "";
    private static string Required(XElement parent, XName name) => parent.Element(name)?.Value is { Length: > 0 } value ? value : throw new FormatException("Missing " + name.LocalName);
    public static void Save(Stream stream, IEnumerable<CodeSnippet> snippets)
    {
        ArgumentNullException.ThrowIfNull(stream); ArgumentNullException.ThrowIfNull(snippets);
        var items = snippets.ToArray();
        if (items.Length is 0 or > 512) throw new ArgumentException("Choose between one and 512 snippets.", nameof(snippets));
        XNamespace ns = Namespace; var root = new XElement(ns + "CodeSnippets");
        foreach (var snippet in items)
        {
            ArgumentNullException.ThrowIfNull(snippet);
            XElement Element(string name, object? value) => new(ns + name, value);
            var header = Element("Header", new object[] { Element("Title", snippet.Title), Element("Shortcut", snippet.Shortcut), Element("Description", snippet.Description), Element("Author", snippet.Author), Element("HelpUrl", snippet.HelpUrl),
                Element("SnippetTypes", Enum.GetValues<SnippetTypes>().Where(t => snippet.Types.HasFlag(t)).Select(t => Element("SnippetType", t.ToString()))), Element("Keywords", snippet.Keywords.Select(k => Element("Keyword", k))) });
            var declarations = Element("Declarations", snippet.Declarations.Select(d => new XElement(ns + (d.IsObject ? "Object" : "Literal"), new XAttribute("Editable", d.Editable),
                Element("ID", d.Id), Element("Default", d.DefaultText), Element("ToolTip", d.ToolTip), d.Function.Length > 0 ? Element("Function", d.Function) : null, d.IsObject ? Element("Type", d.Type) : null)));
            var code = new XElement(ns + "Code", new XAttribute("Language", snippet.Language == "csharp" ? "CSharp" : snippet.Language == "vb" ? "VB" : snippet.Language), new XAttribute("Delimiter", snippet.Delimiter),
                snippet.CodeKind.Length > 0 ? new XAttribute("Kind", snippet.CodeKind) : null, snippet.Code);
            var body = Element("Snippet", new object[] { Element("Imports", snippet.Imports.Select(i => Element("Import", Element("Namespace", i)))),
                Element("References", snippet.References.Select(r => Element("Reference", new object[] { Element("Assembly", r.Assembly), Element("Url", r.Url) }))), declarations, code });
            root.Add(new XElement(ns + "CodeSnippet", new XAttribute("Format", "1.0.0"), header, body));
        }
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false });
        new XDocument(root).Save(writer);
    }
}
