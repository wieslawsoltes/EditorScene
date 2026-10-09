using System.Collections.Immutable;
using System.Text;
using UnoEdit.Document;

namespace EditorScene.Text;

/// <summary>A versioned UTF-16 text edit against a captured document revision.</summary>
public readonly record struct TextEdit(int Offset, int Length, string NewText)
{
    public int End => checked(Offset + Length);
}

public readonly record struct TextPosition(int Line, int Character);

/// <summary>Immutable rope snapshot. Safe to read on a language-service worker.</summary>
public sealed class DocumentSnapshot
{
    private readonly ITextSource _source;
    private readonly Lazy<string> _text;
    private readonly Lazy<ImmutableArray<int>> _lineStarts;
    internal DocumentSnapshot(Guid id, long revision, string fileName, ITextSource source)
    {
        DocumentId = id; Revision = revision; FileName = fileName; _source = source;
        _text = new Lazy<string>(() => _source.Text);
        _lineStarts = new Lazy<ImmutableArray<int>>(() =>
        {
            var starts = ImmutableArray.CreateBuilder<int>(); starts.Add(0);
            for (var i = 0; i < Text.Length; i++)
            {
                if (Text[i] == '\r') { if (i + 1 < Text.Length && Text[i + 1] == '\n') i++; starts.Add(i + 1); }
                else if (Text[i] == '\n') starts.Add(i + 1);
            }
            return starts.ToImmutable();
        });
    }
    public Guid DocumentId { get; }
    public long Revision { get; }
    public string FileName { get; }
    public int Length => _source.TextLength;
    public string Text => _text.Value;
    public int LineCount => _lineStarts.Value.Length;
    public ITextSource TextSource => _source;
    public string GetText(int offset, int length) => _source.GetText(offset, length);
    public int GetOffset(TextPosition position)
    {
        var starts = _lineStarts.Value;
        if (position.Line < 0 || position.Line >= starts.Length || position.Character < 0)
            throw new ArgumentOutOfRangeException(nameof(position));
        var end = position.Line + 1 < starts.Length ? starts[position.Line + 1] : Length;
        while (end > starts[position.Line] && Text[end - 1] is '\r' or '\n') end--;
        return starts[position.Line] + Math.Min(position.Character, end - starts[position.Line]);
    }
    public TextPosition GetPosition(int offset)
    {
        if ((uint)offset > (uint)Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var starts = _lineStarts.Value; var index = starts.BinarySearch(offset);
        if (index < 0) index = ~index - 1;
        return new TextPosition(index, offset - starts[index]);
    }
}

/// <summary>
/// A reusable document with persistent snapshots, rope-backed edits, undo and
/// optimistic revision checks. Mutations retain the core's owning-thread rule.
/// </summary>
public sealed class EditorDocument : IDisposable
{
    private bool _disposed;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    public EditorDocument(string text = "", string fileName = "Untitled.cs")
    {
        Buffer = new TextDocument(text ?? throw new ArgumentNullException(nameof(text)));
        FileName = fileName; Buffer.Changed += OnBufferChanged; Buffer.TextChanged += OnTextChanged;
        Buffer.UndoStack.MarkAsOriginalFile();
        Indicators = new IndicatorCollection(this);
    }
    public Guid Id { get; } = Guid.NewGuid();
    public TextDocument Buffer { get; }
    public IndicatorCollection Indicators { get; }
    public string FileName { get; set; }
    public string LanguageId { get; set; } = "csharp";
    public Encoding Encoding { get; set; } = new UTF8Encoding(false, true);
    public long Revision { get; private set; }
    public bool IsModified => !Buffer.UndoStack.IsOriginalFile;
    public event EventHandler? Changed;
    public bool CheckAccess() => Environment.CurrentManagedThreadId == _ownerThreadId;
    public void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CheckAccess()) throw new InvalidOperationException("Document mutations and snapshot capture must run on its owning thread.");
    }
    public DocumentSnapshot Snapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        return new DocumentSnapshot(Id, Revision, FileName, Buffer.CreateSnapshot());
    }
    private void OnBufferChanged(object? sender, DocumentChangeEventArgs e) => Revision++;
    private void OnTextChanged(object? sender, EventArgs e) => Changed?.Invoke(this, e);
    public void MarkSaved() => Buffer.UndoStack.MarkAsOriginalFile();

    /// <summary>
    /// Validates all offsets and rejects overlapping edits before mutation.
    /// All coordinates refer to the same expected revision; one undo group is used.
    /// </summary>
    public void ApplyEdits(IEnumerable<TextEdit> changes, long expectedRevision)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(changes);
        VerifyAccess();
        if (Revision != expectedRevision) throw new InvalidOperationException("The document changed; discard stale edits and recompute them.");
        var edits = changes.OrderBy(x => x.Offset).ThenBy(x => x.Length).ToArray();
        for (var i = 0; i < edits.Length; i++)
        {
            var e = edits[i];
            if (e.NewText is null || e.Offset < 0 || e.Length < 0 || e.Offset > Buffer.TextLength || e.Length > Buffer.TextLength - e.Offset)
                throw new ArgumentOutOfRangeException(nameof(changes), "An edit is outside the captured document.");
            if (i > 0 && (edits[i - 1].End > e.Offset || edits[i - 1].Offset == e.Offset))
                throw new ArgumentException("Edits must not overlap or share insertion offsets.", nameof(changes));
        }
        if (Revision != expectedRevision) throw new InvalidOperationException("The edit source mutated the document during enumeration.");
        using (Buffer.RunUpdate())
            for (var i = edits.Length - 1; i >= 0; i--)
                if (edits[i].Length != 0 || edits[i].NewText.Length != 0)
                    Buffer.Replace(edits[i].Offset, edits[i].Length, edits[i].NewText);
    }

    /// <summary>Load only after the stream decodes successfully; the caller owns the stream.</summary>
    public void Load(Stream stream, Encoding? fallback = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, fallback ?? new UTF8Encoding(false, true), true, 8192, true);
        var text = reader.ReadToEnd(); var encoding = reader.CurrentEncoding;
        Buffer.Text = text; Encoding = encoding; Buffer.UndoStack.ClearAll(); MarkSaved();
    }
    /// <summary>Do not mark saved until writing and flushing both succeed.</summary>
    public void Save(Stream stream)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(stream);
        using (var writer = new StreamWriter(stream, Encoding, 8192, true)) { Buffer.WriteTextTo(writer); writer.Flush(); }
        MarkSaved();
    }
    public void ConvertLineEndings(string delimiter)
    {
        if (delimiter is not ("\n" or "\r\n" or "\r")) throw new ArgumentException("Choose LF, CRLF or CR.", nameof(delimiter));
        var changes = Buffer.Lines.Where(x => x.DelimiterLength != 0)
            .Select(x => new TextEdit(x.EndOffset, x.DelimiterLength, delimiter)).ToArray();
        ApplyEdits(changes, Revision);
    }
    public string PreferredLineEnding
    {
        get
        {
            foreach (var line in Buffer.Lines)
                if (line.DelimiterLength != 0) return Buffer.GetText(line.EndOffset, line.DelimiterLength);
            return "\n";
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Buffer.Changed -= OnBufferChanged; Buffer.TextChanged -= OnTextChanged; Changed = null;
    }
}
