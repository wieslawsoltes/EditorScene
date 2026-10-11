using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EditorScene.Text.Macros;

/// <summary>Versioned, bounded, data-only macro XML. Streams remain caller-owned; no type/assembly activation.</summary>
public static class MacroSerializer
{
    public const string Namespace = "urn:editorscene:macro:1";
    public static EditorMacro Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false, MaxCharactersInDocument = 8 * 1024 * 1024 });
        var root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root ?? throw new FormatException("Missing macro root.");
        XNamespace ns = Namespace;
        if (root.Name != ns + "Macro" || (string?)root.Attribute("version") != "1") throw new FormatException("Unsupported macro schema or version.");
        if (root.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != "version")) throw new FormatException("Unknown macro metadata.");
        var steps = new List<MacroStep>();
        foreach (var node in root.Nodes())
        {
            if (node is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value) || node is XComment) continue;
            if (node is not XElement element || element.Name != ns + "Action" || element.HasElements || element.Attributes().Any(a => a.Name != "command"))
                throw new FormatException("Invalid macro action.");
            if (steps.Count == EditorMacro.MaxSteps) throw new FormatException("Too many macro actions.");
            steps.Add(new MacroStep((string?)element.Attribute("command") ?? throw new FormatException("Missing action command."), element.Value));
        }
        return new EditorMacro(steps);
    }
    public static void Save(Stream stream, EditorMacro macro)
    {
        ArgumentNullException.ThrowIfNull(stream); ArgumentNullException.ThrowIfNull(macro);
        XNamespace ns = Namespace;
        var root = new XElement(ns + "Macro", new XAttribute("version", "1"), macro.Steps.Select(step => new XElement(ns + "Action", new XAttribute("command", step.Command), step.Argument)));
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        { Encoding = new UTF8Encoding(false), CloseOutput = false, Indent = true, NewLineHandling = NewLineHandling.Entitize });
        new XDocument(root).Save(writer);
    }
}
