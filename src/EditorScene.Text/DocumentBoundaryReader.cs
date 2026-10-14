using System.Text;
using UnoEdit.Document;

namespace EditorScene.Text;

/// <summary>Owner-thread scratch reader for one live-buffer boundary code unit.
/// Uses the public writer traversal instead of building the rope's index cache
/// after each mutation. No source, array or string is retained.</summary>
internal sealed class DocumentBoundaryReader : TextWriter
{
    private bool _reading;
    private int _count;
    private char _value;
    public override Encoding Encoding => Encoding.Unicode;

    internal char Read(TextDocument document, int offset)
    {
        if (_reading) throw new InvalidOperationException("A boundary read cannot be reentered.");
        _reading = true; _count = 0;
        try
        {
            // The pinned UnoEdit CharRope.WriteTo visits its leaf directly. Unlike
            // GetCharAt/GetTextAsMemory, it does not build an ImmutableStack cache.
            document.WriteTextTo(this, offset, 1);
            if (_count != 1) throw new InvalidOperationException("A boundary read must produce exactly one code unit.");
            return _value;
        }
        finally { _reading = false; _count = 0; _value = default; }
    }
    public override void Write(char value)
    {
        if (!_reading || _count != 0) throw new InvalidOperationException("Unexpected boundary writer input.");
        _value = value; _count = 1;
    }
    public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));
    public override void Write(string? value) => Write(value.AsSpan());
    public override void Write(ReadOnlySpan<char> buffer)
    {
        if (buffer.IsEmpty) return;
        if (buffer.Length != 1) throw new InvalidOperationException("A boundary writer accepts one code unit.");
        Write(buffer[0]);
    }
}
