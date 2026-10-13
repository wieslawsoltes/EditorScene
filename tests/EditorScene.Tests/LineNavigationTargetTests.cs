using System.Reflection;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LineNavigationTargetTests
{
    [TestCase("1", 1, 1, 0)]
    [TestCase("1:2", 1, 2, 1)]
    [TestCase("2", 2, 1, 3)]
    [TestCase("2:2", 2, 2, 4)]
    [TestCase("2:4", 2, 4, 6)]
    [TestCase(" 2 : 5 ", 2, 5, 7)]
    [TestCase("3:1", 3, 1, 8)]
    [TestCase("0002:0004", 2, 4, 6)]
    public void ResolvesOneBasedCoordinatesWithoutCopyingSource(string query, int line, int character, int offset)
    {
        using var document = new EditorDocument("a\r\n\t🙂z\n"); var snapshot = document.Snapshot();
        Assert.That(LineNavigationTarget.TryParse(snapshot, query, out var target, out var error), Is.True);
        Assert.That(error, Is.EqualTo(LineNavigationError.None)); Assert.That(target.LineNumber, Is.EqualTo(line));
        Assert.That(target.Character, Is.EqualTo(character)); Assert.That(target.Offset, Is.EqualTo(offset));
        Assert.That(target.IsCurrent(snapshot), Is.True); Assert.That(IsFlattened(snapshot), Is.False);
    }
    [TestCase("", LineNavigationError.InvalidFormat)]
    [TestCase(" ", LineNavigationError.InvalidFormat)]
    [TestCase("-1", LineNavigationError.InvalidFormat)]
    [TestCase("+1", LineNavigationError.InvalidFormat)]
    [TestCase("1:", LineNavigationError.InvalidFormat)]
    [TestCase(":1", LineNavigationError.InvalidFormat)]
    [TestCase("1:2:3", LineNavigationError.InvalidFormat)]
    [TestCase("1:1.5", LineNavigationError.InvalidFormat)]
    [TestCase("1:-2", LineNavigationError.InvalidFormat)]
    [TestCase("1,000", LineNavigationError.InvalidFormat)]
    [TestCase("2147483648", LineNavigationError.InvalidFormat)]
    [TestCase("1:2147483648", LineNavigationError.InvalidFormat)]
    [TestCase("0", LineNavigationError.LineOutOfRange)]
    [TestCase("4", LineNavigationError.LineOutOfRange)]
    [TestCase("1:0", LineNavigationError.CharacterOutOfRange)]
    [TestCase("1:3", LineNavigationError.CharacterOutOfRange)]
    [TestCase("3:2", LineNavigationError.CharacterOutOfRange)]
    [TestCase("2:3", LineNavigationError.SplitSurrogate)]
    public void InvalidTargetsAreExplicitAndNeverSilentlyClamped(string query, LineNavigationError expected)
    {
        using var document = new EditorDocument("a\r\n\t🙂z\n"); var snapshot = document.Snapshot();
        Assert.That(LineNavigationTarget.TryParse(snapshot, query, out var target, out var error), Is.False);
        Assert.That(error, Is.EqualTo(expected)); Assert.That(target, Is.EqualTo(default(LineNavigationTarget)));
        Assert.That(target.IsCurrent(snapshot), Is.False); Assert.That(IsFlattened(snapshot), Is.False);
    }
    [Test]
    public void EmptyDocumentHasExactlyOneValidNavigationPosition()
    {
        using var document = new EditorDocument(); var snapshot = document.Snapshot();
        Assert.That(LineNavigationTarget.TryParse(snapshot, "1:1", out var target, out _), Is.True);
        Assert.That(target.Offset, Is.Zero);
        Assert.That(LineNavigationTarget.TryParse(snapshot, "1:2", out _, out _), Is.False);
        Assert.That(LineNavigationTarget.TryParse(snapshot, "2", out _, out _), Is.False);
    }
    [Test]
    public void OldAndForeignTargetsCannotMatchAChangedDocumentWithTheSameText()
    {
        using var document = new EditorDocument("x"); using var other = new EditorDocument("x"); var snapshot = document.Snapshot();
        LineNavigationTarget.TryParse(snapshot, "1", out var target, out _);
        Assert.That(target.IsCurrent(other.Snapshot()), Is.False);
        document.Buffer.Insert(0, "a"); document.Buffer.UndoStack.Undo();
        Assert.That(target.IsCurrent(document.Snapshot()), Is.False); Assert.That(target.IsCurrent(snapshot), Is.True);
        document.FileName = "renamed.cs";
        Assert.That(target.DocumentId, Is.EqualTo(document.Id));
    }
    [Test]
    public void TooLongAndNullSnapshotInputsHaveDefinedFailures()
    {
        using var document = new EditorDocument(); var snapshot = document.Snapshot();
        Assert.That(LineNavigationTarget.TryParse(snapshot, new string(' ', 64) + "1", out _, out var error), Is.False);
        Assert.That(error, Is.EqualTo(LineNavigationError.InvalidFormat));
        Assert.Throws<ArgumentNullException>(() => LineNavigationTarget.TryParse(null!, "1", out _, out _));
        Assert.Throws<ArgumentNullException>(() => default(LineNavigationTarget).IsCurrent(null!));
    }
    [Test]
    public async Task DetachedSnapshotsCanBeQueriedOnAWorker()
    {
        var document = new EditorDocument("one\ntwo"); var snapshot = document.Snapshot(); document.Dispose();
        var target = await Task.Run(() =>
        {
            if (!LineNavigationTarget.TryParse(snapshot, "2:4", out var result, out _)) throw new InvalidOperationException();
            return result;
        });
        Assert.That(target.Offset, Is.EqualTo(7)); Assert.That(IsFlattened(snapshot), Is.False);
    }
    [Test]
    public void RepeatedWarmQueriesAllocateNothingAndDoNotFlattenLargeDocuments()
    {
        using var document = new EditorDocument(string.Concat(Enumerable.Repeat("content\n", 100000)));
        var snapshot = document.Snapshot(); LineNavigationTarget.TryParse(snapshot, "100000:8", out var expected, out _);
        for (var i = 0; i < 100; i++) LineNavigationTarget.TryParse(snapshot, "100000:8", out _, out _);
        var before = GC.GetAllocatedBytesForCurrentThread(); var correct = true;
        for (var i = 0; i < 10000; i++)
            correct &= LineNavigationTarget.TryParse(snapshot, "100000:8", out var target, out _) && target == expected;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(correct, Is.True); Assert.That(allocated, Is.Zero); Assert.That(IsFlattened(snapshot), Is.False);
    }
    private static bool IsFlattened(DocumentSnapshot snapshot) => ((Lazy<string>)typeof(DocumentSnapshot)
        .GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(snapshot)!).IsValueCreated;
}
