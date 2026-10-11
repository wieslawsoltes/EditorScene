using System.Collections.Immutable;
using System.Text;

namespace EditorScene.Snippets;

[Flags]
public enum SnippetTypes { Expansion = 1, SurroundsWith = 2, Refactoring = 4 }
public enum SnippetPartKind { Text, Field, SelectedText, End }
public readonly record struct SnippetPart(SnippetPartKind Kind, string Text);
public sealed record SnippetDeclaration(string Id, string DefaultText, bool Editable = true, string ToolTip = "", string Function = "", bool IsObject = false, string Type = "");
public sealed record SnippetReference(string Assembly, string Url = "");

/// <summary>Immutable, validated snippet. Parsing never evaluates code, opens URLs or resolves assemblies.</summary>
public sealed class CodeSnippet
{
    public CodeSnippet(string title, string shortcut, string language, string code,
        IEnumerable<SnippetDeclaration>? declarations = null, SnippetTypes types = SnippetTypes.Expansion,
        char delimiter = '$', string description = "", string author = "", string helpUrl = "", string codeKind = "",
        IEnumerable<string>? imports = null, IEnumerable<SnippetReference>? references = null, IEnumerable<string>? keywords = null)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A snippet needs a title.", nameof(title));
        ArgumentNullException.ThrowIfNull(shortcut); ArgumentException.ThrowIfNullOrWhiteSpace(language); ArgumentNullException.ThrowIfNull(code);
        if (code.Length > 1_048_576) throw new ArgumentOutOfRangeException(nameof(code), "A snippet may contain at most one MiB of UTF-16 code units.");
        if (char.IsControl(delimiter) || char.IsWhiteSpace(delimiter) || char.IsSurrogate(delimiter)) throw new ArgumentException("Invalid snippet delimiter.", nameof(delimiter));
        if (types == 0 || (types & ~(SnippetTypes.Expansion | SnippetTypes.SurroundsWith | SnippetTypes.Refactoring)) != 0) throw new ArgumentOutOfRangeException(nameof(types));
        Title = title; Shortcut = shortcut; Language = NormalizeLanguage(language); Code = code; Types = types; Delimiter = delimiter;
        Description = description; Author = author; HelpUrl = helpUrl; CodeKind = codeKind;
        Declarations = (declarations ?? []).ToImmutableArray(); Imports = (imports ?? []).ToImmutableArray();
        References = (references ?? []).ToImmutableArray(); Keywords = (keywords ?? []).ToImmutableArray();
        if (Declarations.Length > 512) throw new ArgumentException("A snippet may declare at most 512 fields.", nameof(declarations));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in Declarations)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Id) || field.Id.Contains(delimiter) || field.Id is "selected" or "end" || field.DefaultText is null)
                throw new ArgumentException("Invalid or reserved snippet field ID.", nameof(declarations));
            if (!names.Add(field.Id)) throw new ArgumentException("Duplicate snippet field declaration: " + field.Id, nameof(declarations));
        }
        Parts = ParseParts(code, delimiter, names);
    }
    public string Title { get; }
    public string Shortcut { get; }
    public string Language { get; }
    public string Code { get; }
    public string Description { get; }
    public string Author { get; }
    public string HelpUrl { get; }
    public string CodeKind { get; }
    public char Delimiter { get; }
    public SnippetTypes Types { get; }
    public ImmutableArray<SnippetDeclaration> Declarations { get; }
    public ImmutableArray<string> Imports { get; }
    public ImmutableArray<SnippetReference> References { get; }
    public ImmutableArray<string> Keywords { get; }
    public ImmutableArray<SnippetPart> Parts { get; }
    public static string NormalizeLanguage(string language) => language.Trim().ToLowerInvariant() switch
    { "c#" or "csharp" => "csharp", "vb" or "vb.net" or "visualbasic" => "vb", "javascript" or "js" => "javascript", var value => value };
    private static ImmutableArray<SnippetPart> ParseParts(string code, char delimiter, HashSet<string> names)
    {
        var result = ImmutableArray.CreateBuilder<SnippetPart>(); var text = new StringBuilder(); var hasEnd = false;
        void Flush() { if (text.Length > 0) { result.Add(new(SnippetPartKind.Text, text.ToString())); text.Clear(); } }
        for (var i = 0; i < code.Length;)
        {
            if (code[i] != delimiter) { text.Append(code[i++]); continue; }
            if (i + 1 < code.Length && code[i + 1] == delimiter) { text.Append(delimiter); i += 2; continue; }
            var end = code.IndexOf(delimiter, i + 1);
            if (end < 0) throw new FormatException("Unclosed snippet field at code offset " + i);
            var id = code[(i + 1)..end]; Flush();
            if (id == "end")
            {
                if (hasEnd) throw new FormatException("A snippet may have only one end marker.");
                hasEnd = true; result.Add(new(SnippetPartKind.End, ""));
            }
            else if (id == "selected") result.Add(new(SnippetPartKind.SelectedText, ""));
            else if (names.Contains(id)) result.Add(new(SnippetPartKind.Field, id));
            else throw new FormatException("Snippet field has no declaration: " + id);
            i = end + 1;
        }
        Flush(); return result.ToImmutable();
    }
}
