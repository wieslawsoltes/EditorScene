using System.Buffers;
using System.Text;

namespace EditorScene.Text;

/// <summary>Prepared, immutable encoded content for a host-owned save. Preparation validates
/// the entire snapshot before a host opens/truncates its destination. Each write uses bounded
/// pooled buffers and an independent encoder. It never marks a live document saved.</summary>
public sealed class SnapshotSaveContent
{
    private const int CharacterBufferSize = 4096;
    private const int ByteBufferSize = 16384;
    private readonly DocumentSnapshot _snapshot;
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;
    public Guid DocumentId => _snapshot.DocumentId;
    public long Revision => _snapshot.Revision;
    public long ByteLength { get; }
    public string EncodingWebName => _encoding.WebName;

    private SnapshotSaveContent(DocumentSnapshot snapshot, Encoding encoding, byte[] preamble, long byteLength)
    { _snapshot = snapshot; _encoding = encoding; _preamble = preamble; ByteLength = byteLength; }

    /// <summary>Two-pass saving deliberately pays an encoding preflight to avoid truncating an
    /// existing file before discovering invalid source characters. The captured encoding is cloned.
    /// BOM bytes are included exactly as provided by that encoding, including for empty content.
    /// The caller must supply a deterministic Encoding/Encoder implementation.</summary>
    public static SnapshotSaveContent Create(DocumentSnapshot snapshot, Encoding encoding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(encoding);
        cancellationToken.ThrowIfCancellationRequested();
        var captured = (Encoding)encoding.Clone(); var preamble = captured.GetPreamble();
        long length = preamble.Length;
        using (var reader = new EncoderReader(snapshot, captured))
            while (reader.ReadNext(cancellationToken, out var count)) length = checked(length + count);
        cancellationToken.ThrowIfCancellationRequested();
        return new(snapshot, captured, preamble, length);
    }

    /// <summary>Write at the destination's current position, then flush. Does not seek, truncate,
    /// close the destination, publish file-provider completion or mark anything saved. Cancellation
    /// or an I/O failure may leave a partial destination: hosts own transactional replacement.
    /// No additional writes/flushes are attempted after an error.</summary>
    public async Task WriteToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();
        if (!destination.CanWrite) throw new ArgumentException("A writable destination is required.", nameof(destination));
        long written = 0;
        for (var start = 0; start < _preamble.Length;)
        {
            var count = Math.Min(ByteBufferSize, _preamble.Length - start);
            cancellationToken.ThrowIfCancellationRequested();
            await destination.WriteAsync(_preamble.AsMemory(start, count), cancellationToken).ConfigureAwait(false);
            start += count; written += count;
        }
        using (var reader = new EncoderReader(_snapshot, _encoding))
        {
            while (reader.ReadNext(cancellationToken, out var count))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await destination.WriteAsync(reader.Bytes.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                written = checked(written + count);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (written != ByteLength) throw new InvalidOperationException("The encoding produced a different length after preflight.");
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private sealed class EncoderReader : IDisposable
    {
        private readonly TextReader _reader;
        private readonly Encoder _encoder;
        private char[] _characters = [];
        internal byte[] Bytes { get; private set; } = [];
        private int _offset, _count;
        private bool _end, _completed, _disposed;
        internal EncoderReader(DocumentSnapshot snapshot, Encoding encoding)
        {
            _encoder = ((Encoding)encoding.Clone()).GetEncoder();
            _reader = snapshot.TextSource.CreateReader(0, snapshot.Length);
            try
            {
                _characters = ArrayPool<char>.Shared.Rent(CharacterBufferSize);
                Bytes = ArrayPool<byte>.Shared.Rent(ByteBufferSize);
            }
            catch { Dispose(); throw; }
        }
        internal bool ReadNext(CancellationToken cancellationToken, out int bytesWritten)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            bytesWritten = 0;
            while (!_completed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_end && _offset == _count)
                {
                    _offset = 0; _count = _reader.Read(_characters, 0, CharacterBufferSize);
                    _end = _count == 0;
                }
                _encoder.Convert(_characters, _offset, _count - _offset, Bytes, 0, ByteBufferSize, _end,
                    out var consumed, out bytesWritten, out var completed);
                _offset += consumed; _completed = _end && completed;
                if (bytesWritten != 0) return true;
                if (consumed == 0 && !_completed)
                    throw new InvalidOperationException("The encoder cannot make progress within the bounded output buffer.");
            }
            return false;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _reader.Dispose(); }
            finally
            {
                if (_characters.Length != 0) ArrayPool<char>.Shared.Return(_characters, clearArray: true);
                if (Bytes.Length != 0) ArrayPool<byte>.Shared.Return(Bytes, clearArray: true);
                _characters = []; Bytes = [];
            }
        }
    }
}
