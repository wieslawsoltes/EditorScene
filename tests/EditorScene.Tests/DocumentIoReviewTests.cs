using System.Text;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class DocumentIoReviewTests
{
    [Test]
    public void SaveWritesCapturedSnapshotAndDoesNotMarkNewerEditsSaved()
    {
        using var doc = new EditorDocument("original"); doc.Buffer.Insert(0, "saved ");
        using var stream = new CallbackStream(() => doc.Buffer.Insert(doc.Buffer.TextLength, " NEW"));
        doc.Save(stream);
        Assert.That(Encoding.UTF8.GetString(stream.ToArray()), Is.EqualTo("saved original"));
        Assert.That(doc.Buffer.Text, Is.EqualTo("saved original NEW")); Assert.That(doc.IsModified, Is.True);
    }
    [Test]
    public void LoadCannotOverwriteAnEditMadeDuringStreamRead()
    {
        using var doc = new EditorDocument("original");
        using var stream = new CallbackReadStream(Encoding.UTF8.GetBytes("replacement"), () => doc.Buffer.Insert(0, "NEW "));
        Assert.Throws<InvalidOperationException>(() => doc.Load(stream));
        Assert.That(doc.Buffer.Text, Is.EqualTo("NEW original")); Assert.That(doc.IsModified, Is.True);
    }
    [Test]
    public async Task WrongThreadIoFailsBeforeTouchingCallerStreams()
    {
        using var doc = new EditorDocument("original");
        var touched = false; using var stream = new CallbackStream(() => touched = true);
        await Task.Run(() => Assert.Throws<InvalidOperationException>(() => doc.Save(stream)));
        Assert.That(touched, Is.False); Assert.That(stream.Length, Is.Zero);
    }
    private sealed class CallbackStream(Action callback) : MemoryStream
    {
        private bool _called;
        public override void Write(byte[] buffer, int offset, int count) { Invoke(); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Invoke(); base.Write(buffer); }
        private void Invoke() { if (_called) return; _called = true; callback(); }
    }
    private sealed class CallbackReadStream(byte[] data, Action callback) : MemoryStream(data)
    {
        private bool _called;
        public override int Read(byte[] buffer, int offset, int count) { Invoke(); return base.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { Invoke(); return base.Read(buffer); }
        private void Invoke() { if (_called) return; _called = true; callback(); }
    }
}
