using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SearchEnumerationReviewTests
{
    [Test]
    public void CancellationAfterYieldDoesNotAffectAnotherEnumerationOrTheDocument()
    {
        using var document = new EditorDocument("match " + new string('x', 8192) + " match");
        var snapshot = document.Snapshot(); var scope = snapshot.GetSpan(0, snapshot.Length);
        var search = new SnapshotLiteralSearch("match"); using var cancellation = new CancellationTokenSource();
        using var cancelled = search.FindAll(scope, cancellationToken: cancellation.Token).GetEnumerator();
        using var independent = search.FindAll(scope).GetEnumerator();
        Assert.That(cancelled.MoveNext(), Is.True); Assert.That(cancelled.Current.Offset, Is.Zero);
        Assert.That(independent.MoveNext(), Is.True);
        cancellation.Cancel(); Assert.Throws<OperationCanceledException>(() => cancelled.MoveNext());
        Assert.That(independent.MoveNext(), Is.True); Assert.That(independent.Current.End, Is.EqualTo(snapshot.Length));
        Assert.That(independent.MoveNext(), Is.False); Assert.That(document.Revision, Is.Zero);
    }
    [Test]
    public void EarlyDisposedSearchDoesNotInvalidateReturnedSnapshotRanges()
    {
        using var document = new EditorDocument("needle " + new string('x', 9000) + " needle");
        var snapshot = document.Snapshot(); var search = new SnapshotLiteralSearch("needle"); SnapshotSpan match;
        using (var iterator = search.FindAll(snapshot.GetSpan(0, snapshot.Length)).GetEnumerator())
        { Assert.That(iterator.MoveNext(), Is.True); match = iterator.Current; }
        document.Buffer.Replace(0, 6, "edited");
        for (var i = 0; i < 30; i++)
            Assert.That(search.FindPrevious(snapshot, snapshot.Length)?.Offset, Is.EqualTo(snapshot.Length - 6));
        Assert.That(match.Text, Is.EqualTo("needle"));
        Assert.That(document.Buffer.GetText(0, 6), Is.EqualTo("edited"));
    }
}
