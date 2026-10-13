using System.Reflection;
using System.Text;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SnapshotSaveContentTests
{
    private static Encoding EncodingFor(string kind) => kind switch
    {
        "utf8" => new UTF8Encoding(false, true),
        "utf8-bom" => new UTF8Encoding(true, true),
        "utf16-le" => new UnicodeEncoding(false, true, true),
        "utf16-be" => new UnicodeEncoding(true, true, true),
        "utf32" => new UTF32Encoding(false, true, true),
        _ => Encoding.Latin1
    };
    [TestCase("utf8")]
    [TestCase("utf8-bom")]
    [TestCase("utf16-le")]
    [TestCase("utf16-be")]
    [TestCase("utf32")]
    [TestCase("latin1")]
    public async Task StreamingMatchesWholeStringEncodingAcrossSurrogateAndReaderBoundaries(string kind)
    {
        var text = new string('a', 4095) + "🙂" + new string('z', 4093) + "e\u0301\r\n日本語\ré\n\t";
        using var document = new EditorDocument(text); var snapshot = document.Snapshot(); var encoding = EncodingFor(kind);
        var expected = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        var content = SnapshotSaveContent.Create(snapshot, encoding);
        Assert.That(IsFlattened(snapshot), Is.False);
        using var output = new ObservedStream(); await content.WriteToAsync(output);
        Assert.That(output.ToArray(), Is.EqualTo(expected)); Assert.That(content.ByteLength, Is.EqualTo(expected.LongLength));
        Assert.That(output.MaximumWrite, Is.LessThanOrEqualTo(16384)); Assert.That(output.Flushes, Is.EqualTo(1));
        Assert.That(output.WasDisposed, Is.False); Assert.That(content.DocumentId, Is.EqualTo(snapshot.DocumentId));
        Assert.That(content.Revision, Is.EqualTo(snapshot.Revision)); Assert.That(IsFlattened(snapshot), Is.False);
    }
    [TestCase("utf8")]
    [TestCase("utf8-bom")]
    [TestCase("utf16-le")]
    public async Task EmptySnapshotsWriteOnlyTheConfiguredPreamble(string kind)
    {
        using var document = new EditorDocument(); var encoding = EncodingFor(kind);
        var content = SnapshotSaveContent.Create(document.Snapshot(), encoding);
        using var stream = new ObservedStream(); await content.WriteToAsync(stream);
        Assert.That(stream.ToArray(), Is.EqualTo(encoding.GetPreamble())); Assert.That(stream.Flushes, Is.EqualTo(1));
    }
    [Test]
    public async Task CapturedEncodingAndSnapshotAreIndependentOfLaterHostChanges()
    {
        var encoding = (Encoding)Encoding.ASCII.Clone(); encoding.EncoderFallback = new EncoderReplacementFallback("original");
        var document = new EditorDocument("é"); var snapshot = document.Snapshot();
        var content = SnapshotSaveContent.Create(snapshot, encoding);
        encoding.EncoderFallback = new EncoderReplacementFallback("changed");
        document.Buffer.Text = "later"; document.Dispose();
        using var stream = new ObservedStream(); await content.WriteToAsync(stream);
        Assert.That(Encoding.ASCII.GetString(stream.ToArray()), Is.EqualTo("original")); Assert.That(IsFlattened(snapshot), Is.False);
    }
    [Test]
    public void StrictEncodingFailuresAreDetectedDuringPreparationBeforeOpeningAnyDestination()
    {
        foreach (var text in new[] { "\uD800", new string('a', 100000) + "\uD800", "\uDC00", "a\uD800x" })
        {
            using var document = new EditorDocument(text); var snapshot = document.Snapshot();
            Assert.Throws<EncoderFallbackException>(() => SnapshotSaveContent.Create(snapshot, new UTF8Encoding(false, true)));
            Assert.That(IsFlattened(snapshot), Is.False);
        }
    }
    [Test]
    public async Task NonthrowingFallbackAtTheEndIsFlushedExactlyOnce()
    {
        var text = new string('x', 4095) + "\uD800";
        using var document = new EditorDocument(text); var encoding = new UTF8Encoding(false, false);
        var content = SnapshotSaveContent.Create(document.Snapshot(), encoding);
        using var stream = new ObservedStream(); await content.WriteToAsync(stream);
        Assert.That(stream.ToArray(), Is.EqualTo(encoding.GetBytes(text)));
    }
    [Test]
    public async Task ConcurrentWritesUseIndependentEncoderStateAndAwaitEachBuffer()
    {
        var document = new EditorDocument(string.Concat(Enumerable.Repeat("日本語🙂\r\n", 5000))); var snapshot = document.Snapshot();
        var expected = new UTF8Encoding(false, true).GetBytes(snapshot.GetText(0, snapshot.Length));
        var content = SnapshotSaveContent.Create(snapshot, new UTF8Encoding(false, true)); document.Dispose();
        using var first = new ObservedStream { YieldWrites = true }; using var second = new ObservedStream { YieldWrites = true };
        await Task.WhenAll(content.WriteToAsync(first), content.WriteToAsync(second));
        Assert.That(first.ToArray(), Is.EqualTo(expected)); Assert.That(second.ToArray(), Is.EqualTo(expected));
        Assert.That(first.Writes, Is.GreaterThan(1)); Assert.That(IsFlattened(snapshot), Is.False);
    }
    [Test]
    public async Task CancelledWriteDoesNotContinueFlushingOrCloseTheCallerStream()
    {
        using var document = new EditorDocument(new string('x', 100000));
        var content = SnapshotSaveContent.Create(document.Snapshot(), Encoding.UTF8); using var cancel = new CancellationTokenSource();
        using var stream = new ObservedStream { AfterWrite = () => cancel.Cancel() };
        Assert.ThrowsAsync<OperationCanceledException>(async () => await content.WriteToAsync(stream, cancel.Token));
        Assert.That(stream.Writes, Is.EqualTo(1)); Assert.That(stream.Flushes, Is.Zero); Assert.That(stream.WasDisposed, Is.False);
        using var recovery = new ObservedStream(); await content.WriteToAsync(recovery);
        Assert.That(recovery.ToArray().LongLength, Is.EqualTo(content.ByteLength));
    }
    [Test]
    public void FailedWriteOrFlushIsPropagatedWithoutASecondAttempt()
    {
        using var document = new EditorDocument(new string('x', 20000));
        var content = SnapshotSaveContent.Create(document.Snapshot(), new UTF8Encoding(false, true));
        using var writeFailure = new ObservedStream { FailWriteAt = 2 };
        Assert.ThrowsAsync<IOException>(async () => await content.WriteToAsync(writeFailure));
        Assert.That(writeFailure.Writes, Is.EqualTo(2)); Assert.That(writeFailure.Flushes, Is.Zero); Assert.That(writeFailure.WasDisposed, Is.False);
        using var flushFailure = new ObservedStream { FailFlush = true };
        Assert.ThrowsAsync<IOException>(async () => await content.WriteToAsync(flushFailure));
        Assert.That(flushFailure.Flushes, Is.EqualTo(1)); Assert.That(flushFailure.WasDisposed, Is.False);
    }
    [Test]
    public void PreCancelledAndInvalidRequestsLeaveDestinationsUntouched()
    {
        using var document = new EditorDocument("text"); var snapshot = document.Snapshot(); var cancelled = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => SnapshotSaveContent.Create(snapshot, Encoding.UTF8, cancelled));
        Assert.Throws<ArgumentNullException>(() => SnapshotSaveContent.Create(null!, Encoding.UTF8));
        Assert.Throws<ArgumentNullException>(() => SnapshotSaveContent.Create(snapshot, null!));
        var content = SnapshotSaveContent.Create(snapshot, Encoding.UTF8); using var stream = new ObservedStream();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await content.WriteToAsync(stream, cancelled));
        Assert.ThrowsAsync<ArgumentNullException>(async () => await content.WriteToAsync(null!));
        using var readOnly = new MemoryStream([1, 2, 3], writable: false);
        Assert.ThrowsAsync<ArgumentException>(async () => await content.WriteToAsync(readOnly));
        Assert.That(stream.Writes, Is.Zero); Assert.That(stream.Flushes, Is.Zero);
    }
    [Test]
    public async Task WritingNeverSeeksTruncatesOrMarksLiveTextSaved()
    {
        using var document = new EditorDocument("before"); document.Buffer.Text = "after"; var snapshot = document.Snapshot();
        var content = SnapshotSaveContent.Create(snapshot, new UTF8Encoding(false, true));
        using var stream = new ObservedStream(); await stream.WriteAsync(new byte[] { 1, 2 });
        await content.WriteToAsync(stream);
        Assert.That(stream.ToArray(), Is.EqualTo(new byte[] { 1, 2 }.Concat(Encoding.UTF8.GetBytes("after"))));
        Assert.That(document.IsModified, Is.True); Assert.That(IsFlattened(snapshot), Is.False);
    }
    private static bool IsFlattened(DocumentSnapshot snapshot) => ((Lazy<string>)typeof(DocumentSnapshot)
        .GetField("_text", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(snapshot)!).IsValueCreated;

    private sealed class ObservedStream : Stream
    {
        private readonly MemoryStream _inner = new();
        internal int Writes, Flushes, MaximumWrite, FailWriteAt;
        internal bool YieldWrites, FailFlush, WasDisposed;
        internal Action? AfterWrite;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !WasDisposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        internal byte[] ToArray() => _inner.ToArray();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Writes++; MaximumWrite = Math.Max(MaximumWrite, buffer.Length);
            if (Writes == FailWriteAt) throw new IOException("Expected write failure");
            if (YieldWrites) await Task.Yield();
            await _inner.WriteAsync(buffer, cancellationToken); AfterWrite?.Invoke();
        }
        public override Task FlushAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Flushes++; return FailFlush ? Task.FromException(new IOException("Expected flush failure")) : Task.CompletedTask; }
        public override void Flush() => throw new NotSupportedException("Synchronous flush was not requested");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use the asynchronous path");
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { WasDisposed = true; if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
