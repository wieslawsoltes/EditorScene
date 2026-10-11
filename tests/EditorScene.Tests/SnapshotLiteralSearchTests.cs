using System.Globalization;
using System.Reflection;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SnapshotLiteralSearchTests
{
    [TestCase(4095, "needle")]
    [TestCase(4094, "🙂é")]
    [TestCase(8191, "aba")]
    public void MatchesCrossWindowBoundaries(int prefix, string pattern)
    {
        using var doc = new EditorDocument(new string('x', prefix) + pattern + "end"); var snapshot = doc.Snapshot();
        Assert.That(new SnapshotLiteralSearch(pattern).FindNext(snapshot, 0)?.Offset, Is.EqualTo(prefix));
        Assert.That(IsFlattened(snapshot), Is.False);
    }
    [Test]
    public void PatternMayBeLargerThanReadWindow()
    {
        var needle = "a" + new string('b', 9000) + "c";
        using var doc = new EditorDocument("prefix" + needle + needle); var snapshot = doc.Snapshot();
        Assert.That(new SnapshotLiteralSearch(needle).FindAll(snapshot.GetSpan(0, snapshot.Length)).Select(x => x.Offset), Is.EqualTo(new[] { 6, 6 + needle.Length }));
    }
    [Test]
    public void OverlapReverseAndWrapHaveExplicitBoundaries()
    {
        using var doc = new EditorDocument("ababa"); var s = doc.Snapshot(); var search = new SnapshotLiteralSearch("aba");
        Assert.That(search.FindAll(s.GetSpan(0, s.Length)).Select(m => m.Offset), Is.EqualTo(new[] { 0 }));
        Assert.That(search.FindAll(s.GetSpan(0, s.Length), allowOverlap: true).Select(m => m.Offset), Is.EqualTo(new[] { 0, 2 }));
        Assert.That(search.FindPrevious(s, 5)?.Offset, Is.EqualTo(2)); Assert.That(search.FindNext(s, 5, true)?.Offset, Is.Zero);
        Assert.That(search.FindNext(s, 5), Is.Null); Assert.That(search.FindPrevious(s, 0, true)?.Offset, Is.EqualTo(2));
    }
    [Test]
    public void CaseInsensitiveSearchUsesOrdinalNotTurkishCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            using var doc = new EditorDocument("file FILE fiLe fıle"); var s = doc.Snapshot();
            var expected = new List<int>(); for (var i = 0; i < s.Length; i++) if (s.Text.AsSpan(i).StartsWith("file", StringComparison.OrdinalIgnoreCase)) expected.Add(i);
            Assert.That(new SnapshotLiteralSearch("file", matchCase: false).FindAll(s.GetSpan(0, s.Length)).Select(x => x.Offset), Is.EqualTo(expected));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }
    [Test]
    public void WholeWordUsesUnicodeAndRealSnapshotEdges()
    {
        using var doc = new EditorDocument("xfoo foo foo_ foó 🙂foo 𐐀foo"); var s = doc.Snapshot();
        var search = new SnapshotLiteralSearch("foo", wholeWord: true);
        Assert.That(search.FindAll(s.GetSpan(0, s.Length)).Select(x => x.Offset), Is.EqualTo(new[] { 5, 21 }));
        Assert.That(search.FindAll(s.GetSpan(1, 3)), Is.Empty);
    }
    [Test]
    public void ReplacementPlanningIsBoundedAndDoesNotModifySnapshot()
    {
        using var doc = new EditorDocument("a a a"); var s = doc.Snapshot(); var search = new SnapshotLiteralSearch("a");
        Assert.Throws<InvalidOperationException>(() => search.CreateReplaceAllEdits(s.GetSpan(0, s.Length), "b", maxEdits: 2));
        Assert.That(doc.Revision, Is.Zero);
        doc.ApplyEdits(search.CreateReplaceAllEdits(s.GetSpan(0, s.Length), "long"), s.Revision);
        Assert.That(doc.Buffer.Text, Is.EqualTo("long long long")); doc.Buffer.UndoStack.Undo(); Assert.That(doc.Buffer.Text, Is.EqualTo("a a a"));
    }
    [Test]
    public void CancelledOrInvalidSearchFailsBeforeScanning()
    {
        using var doc = new EditorDocument("abc"); var s = doc.Snapshot(); var search = new SnapshotLiteralSearch("a");
        Assert.Throws<ArgumentException>(() => new SnapshotLiteralSearch(""));
        Assert.Throws<ArgumentOutOfRangeException>(() => search.FindNext(s, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => search.FindAll(s.GetSpan(0, 3), -1));
        Assert.Throws<OperationCanceledException>(() => search.FindAll(s.GetSpan(0, 3), cancellationToken: new CancellationToken(true)));
    }
    [Test]
    public void RandomLiteralResultsMatchDotNetStringSearch()
    {
        var random = new Random(428);
        for (var sample = 0; sample < 40; sample++)
        {
            var text = new string(Enumerable.Range(0, 10_000).Select(_ => "abAB🙂\r\n"[random.Next(8)]).ToArray());
            var pattern = text.Substring(random.Next(text.Length - 12), random.Next(1, 12));
            using var doc = new EditorDocument(text); var s = doc.Snapshot();
            foreach (var matchCase in new[] { true, false })
            {
                var expected = new List<int>(); var at = 0;
                while (at <= text.Length - pattern.Length)
                {
                    var found = text.IndexOf(pattern, at, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                    if (found < 0) break; expected.Add(found); at = found + pattern.Length;
                }
                Assert.That(new SnapshotLiteralSearch(pattern, matchCase).FindAll(s.GetSpan(0, s.Length)).Select(m => m.Offset), Is.EqualTo(expected));
            }
            Assert.That(IsFlattened(s), Is.False);
        }
    }
    [Test]
    public async Task SearchWorksOnWorkerAfterDocumentDisposal()
    {
        var doc = new EditorDocument("abc target"); var s = doc.Snapshot(); doc.Dispose();
        Assert.That(await Task.Run(() => new SnapshotLiteralSearch("target").FindNext(s, 0)?.Offset), Is.EqualTo(4));
    }
    private static bool IsFlattened(DocumentSnapshot snapshot) => ((Lazy<string>)typeof(DocumentSnapshot).GetField("_text", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(snapshot)!).IsValueCreated;
}
