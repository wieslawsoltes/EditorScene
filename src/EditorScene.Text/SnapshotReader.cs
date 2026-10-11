using System.Text;

namespace EditorScene.Text;

/// <summary>A position in an immutable snapshot, for speculative parser reads.</summary>
public readonly record struct SnapshotReaderPosition(DocumentSnapshot Snapshot, int Offset);

/// <summary>
/// Seekable, bounded UTF-16 TextReader over an immutable rope snapshot. Sequential
/// reads use one 4 KiB-character window, without flattening the document. Each
/// reader has its own cursor; the underlying snapshot can be shared across workers.
/// </summary>
public sealed class SnapshotReader : TextReader
{
    private const int WindowSize = 4096;
    private readonly DocumentSnapshot _snapshot;
    private readonly int _start, _end;
    private int _position, _windowStart = -1;
    private string _window = "";
    private bool _disposed;
    public SnapshotReader(DocumentSnapshot snapshot) : this(new SnapshotSpan(snapshot, 0, snapshot?.Length ?? 0)) { }
    public SnapshotReader(SnapshotSpan span)
    {
        ArgumentNullException.ThrowIfNull(span); _snapshot = span.Snapshot;
        _start = _position = span.Offset; _end = span.End;
    }
    public DocumentSnapshot Snapshot => _snapshot;
    public int StartOffset => _start;
    public int EndOffset => _end;
    public int Position { get { Check(); return _position; } set => Seek(value, SeekOrigin.Begin); }
    public int Remaining { get { Check(); return _end - _position; } }
    public bool IsAtEnd => Remaining == 0;
    public bool IsAtStart { get { Check(); return _position == _start; } }
    /// <summary>Begin is the absolute document origin, Current is the cursor and End is this reader's upper bound.</summary>
    public int Seek(long offset, SeekOrigin origin)
    {
        Check(); long target;
        try { target = checked(offset + (origin switch { SeekOrigin.Begin => 0L, SeekOrigin.Current => _position, SeekOrigin.End => _end, _ => throw new ArgumentOutOfRangeException(nameof(origin)) })); }
        catch (OverflowException) { throw new ArgumentOutOfRangeException(nameof(offset)); }
        if (target < _start || target > _end) throw new ArgumentOutOfRangeException(nameof(offset));
        return _position = (int)target;
    }
    public SnapshotReaderPosition CapturePosition() { Check(); return new(_snapshot, _position); }
    public void RestorePosition(SnapshotReaderPosition position)
    {
        Check(); if (!ReferenceEquals(position.Snapshot, _snapshot)) throw new ArgumentException("The checkpoint belongs to another snapshot.", nameof(position));
        Position = position.Offset;
    }
    public SnapshotReader Clone()
    {
        Check(); return new(new SnapshotSpan(_snapshot, _start, _end - _start)) { _position = _position, _windowStart = _windowStart, _window = _window };
    }
    public override int Peek() { Check(); return _position < _end ? CharacterAt(_position) : -1; }
    public int PeekPrevious() { Check(); return _position > _start ? CharacterAt(_position - 1) : -1; }
    public override int Read() { Check(); return _position < _end ? CharacterAt(_position++) : -1; }
    /// <summary>Read one UTF-16 code unit backwards. NUL is 0; only the lower boundary returns -1.</summary>
    public int ReadPrevious() { Check(); return _position > _start ? CharacterAt(--_position) : -1; }
    public override int Read(char[] buffer, int index, int count)
    {
        Check(); ArgumentNullException.ThrowIfNull(buffer);
        if (index < 0 || count < 0 || index > buffer.Length || count > buffer.Length - index) throw new ArgumentOutOfRangeException(nameof(index));
        return Read(buffer.AsSpan(index, count));
    }
    public override int Read(Span<char> buffer)
    {
        Check(); var requested = Math.Min(buffer.Length, _end - _position); var written = 0;
        while (written < requested)
        {
            EnsureWindow(_position);
            var offset = _position - _windowStart; var count = Math.Min(requested - written, _window.Length - offset);
            _window.AsSpan(offset, count).CopyTo(buffer[written..]); _position += count; written += count;
        }
        return written;
    }
    public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(Read(buffer.Span)); }
    /// <summary>Read exactly count code units or fail without advancing the cursor.</summary>
    public string ReadText(int count)
    { var value = PeekText(count); _position += count; return value; }
    public string PeekText(int count)
    {
        Check(); if (count < 0 || count > _end - _position) throw new ArgumentOutOfRangeException(nameof(count));
        return count == 0 ? "" : _snapshot.GetText(_position, count);
    }
    public bool Matches(string text, StringComparison comparison = StringComparison.Ordinal)
    {
        Check(); ArgumentNullException.ThrowIfNull(text);
        if (!Enum.IsDefined(comparison)) throw new ArgumentOutOfRangeException(nameof(comparison));
        return text.Length <= _end - _position && PeekText(text.Length).Equals(text, comparison);
    }
    public int SkipWhitespace()
    {
        Check(); var start = _position;
        while (_position < _end && char.IsWhiteSpace(CharacterAt(_position))) _position++;
        return _position - start;
    }
    /// <summary>Read Unicode scalars; malformed or range-split surrogate units produce one replacement scalar.</summary>
    public bool TryReadRune(out Rune rune)
    {
        var first = Read(); if (first < 0) { rune = default; return false; }
        var c = (char)first;
        if (char.IsHighSurrogate(c) && Peek() is var next && next >= 0 && char.IsLowSurrogate((char)next))
        { Read(); rune = new Rune(c, (char)next); }
        else rune = char.IsSurrogate(c) ? Rune.ReplacementChar : new Rune(c);
        return true;
    }
    public bool TryReadPreviousRune(out Rune rune)
    {
        var last = ReadPrevious(); if (last < 0) { rune = default; return false; }
        var c = (char)last;
        if (char.IsLowSurrogate(c) && PeekPrevious() is var previous && previous >= 0 && char.IsHighSurrogate((char)previous))
        { ReadPrevious(); rune = new Rune((char)previous, c); }
        else rune = char.IsSurrogate(c) ? Rune.ReplacementChar : new Rune(c);
        return true;
    }
    public override string? ReadLine()
    {
        Check(); if (_position == _end) return null;
        var start = _position;
        while (_position < _end && CharacterAt(_position) is not ('\r' or '\n')) _position++;
        var line = _snapshot.GetText(start, _position - start);
        if (_position < _end && Read() == '\r' && Peek() == '\n') Read();
        return line;
    }
    public override string ReadToEnd() => ReadText(Remaining);
    private char CharacterAt(int offset) { EnsureWindow(offset); return _window[offset - _windowStart]; }
    private void EnsureWindow(int offset)
    {
        if (_windowStart >= 0 && offset >= _windowStart && offset - _windowStart < _window.Length) return;
        _windowStart = _start + (offset - _start) / WindowSize * WindowSize;
        _window = _snapshot.GetText(_windowStart, Math.Min(WindowSize, _end - _windowStart));
    }
    private void Check() => ObjectDisposedException.ThrowIf(_disposed, this);
    protected override void Dispose(bool disposing) { _disposed = true; _window = ""; base.Dispose(disposing); }
}

public static class SnapshotReaderExtensions
{
    public static SnapshotReader CreateReader(this DocumentSnapshot snapshot, int offset = 0) => new(snapshot) { Position = offset };
    public static SnapshotReader CreateReader(this SnapshotSpan span) => new(span);
}
