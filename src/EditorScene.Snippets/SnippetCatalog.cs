namespace EditorScene.Snippets;

public sealed record SnippetEntry(string Folder, CodeSnippet Snippet);

/// <summary>Host-owned catalog. Folder names are logical names, never filesystem paths.</summary>
public sealed class SnippetCatalog
{
    private readonly List<SnippetEntry> _entries = [];
    public IReadOnlyList<SnippetEntry> Entries => _entries.AsReadOnly();
    public void Register(CodeSnippet snippet, string folder = "")
    {
        ArgumentNullException.ThrowIfNull(snippet); folder = NormalizeFolder(folder);
        if (_entries.Any(e => e.Folder == folder && e.Snippet.Language == snippet.Language && e.Snippet.Title == snippet.Title)) throw new ArgumentException("Duplicate snippet title in the same folder.", nameof(snippet));
        _entries.Add(new(folder, snippet));
    }
    public CodeSnippet? FindShortcut(string shortcut, string language)
    {
        var id = CodeSnippet.NormalizeLanguage(language); var comparison = id == "vb" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return _entries.FirstOrDefault(e => e.Snippet.Language == id && e.Snippet.Types.HasFlag(SnippetTypes.Expansion) && e.Snippet.Shortcut.Equals(shortcut, comparison))?.Snippet;
    }
    public IEnumerable<SnippetEntry> Find(string language, SnippetTypes type, string filter = "", string folder = "")
    {
        var id = CodeSnippet.NormalizeLanguage(language); folder = NormalizeFolder(folder);
        return _entries.Where(e => e.Snippet.Language == id && (e.Snippet.Types & type) != 0 && (folder.Length == 0 || e.Folder == folder || e.Folder.StartsWith(folder + "/", StringComparison.Ordinal)))
            .Where(e => filter.Length == 0 || e.Snippet.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) || e.Snippet.Shortcut.Contains(filter, StringComparison.OrdinalIgnoreCase) || e.Snippet.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Folder, StringComparer.Ordinal).ThenBy(e => e.Snippet.Title, StringComparer.OrdinalIgnoreCase);
    }
    private static string NormalizeFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var segments = folder.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".." || s.Any(char.IsControl))) throw new ArgumentException("Invalid logical snippet folder.", nameof(folder));
        return string.Join('/', segments);
    }
    public static SnippetCatalog CreateDefault()
    {
        var result = new SnippetCatalog();
        void Add(string title, string shortcut, string language, string code, SnippetDeclaration[] fields, bool surround = false, string folder = "General") =>
            result.Register(new(title, shortcut, language, code, fields, SnippetTypes.Expansion | (surround ? SnippetTypes.SurroundsWith : 0)), folder);
        Add("For loop", "for", "csharp", "for (int $index$ = 0; $index$ < $length$; $index$++)\n{\n\t$selected$$end$\n}", [new("index", "i"), new("length", "length")], true, "Control flow");
        Add("For each", "foreach", "csharp", "foreach (var $item$ in $collection$)\n{\n\t$selected$$end$\n}", [new("item", "item"), new("collection", "collection")], true, "Control flow");
        Add("If statement", "if", "csharp", "if ($condition$)\n{\n\t$selected$$end$\n}", [new("condition", "true")], true, "Control flow");
        Add("While loop", "while", "csharp", "while ($condition$)\n{\n\t$selected$$end$\n}", [new("condition", "true")], true, "Control flow");
        Add("Try catch", "try", "csharp", "try\n{\n\t$selected$$end$\n}\ncatch ($exception$ $name$)\n{\n\tthrow;\n}", [new("exception", "System.Exception"), new("name", "ex")], true, "Control flow");
        Add("Class", "class", "csharp", "public class $name$\n{\n\t$end$\n}", [new("name", "MyClass")], folder: "Types and members");
        Add("Auto property", "prop", "csharp", "public $type$ $name$ { get; set; }$end$", [new("type", "int"), new("name", "MyProperty")], folder: "Types and members");
        Add("Constructor", "ctor", "csharp", "public $name$()\n{\n\t$end$\n}", [new("name", "MyClass")], folder: "Types and members");
        Add("Console write line", "cw", "csharp", "System.Console.WriteLine($value$);$end$", [new("value", "\"message\"")]);
        Add("Region", "region", "csharp", "#region $name$\n$selected$$end$\n#endregion", [new("name", "Region")], true);
        Add("For loop", "for", "vb", "For $index$ As Integer = 0 To $length$ - 1\n\t$selected$$end$\nNext $index$", [new("index", "i"), new("length", "length")], true, "Control flow");
        Add("If statement", "if", "vb", "If $condition$ Then\n\t$selected$$end$\nEnd If", [new("condition", "True")], true, "Control flow");
        Add("Property", "prop", "vb", "Public Property $name$ As $type$$end$", [new("name", "MyProperty"), new("type", "Integer")], folder: "Types and members");
        Add("Class", "class", "vb", "Public Class $name$\n\t$end$\nEnd Class", [new("name", "MyClass")], folder: "Types and members");
        Add("Element", "element", "xml", "<$name$>\n\t$selected$$end$\n</$name$>", [new("name", "element")], true);
        Add("Grid", "grid", "xaml", "<Grid>\n\t$selected$$end$\n</Grid>", [], true);
        return result;
    }
}
