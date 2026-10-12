using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class AdaptiveTabMapTests
{
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(8)]
    [TestCase(1000)]
    public void BothChunkPathsPreserveEveryOffsetAndTabInterior(int tabSize)
    {
        var dense = string.Concat(Enumerable.Repeat(" \t", 4097));
        var fixtures = new[]
        {
            new string('\t', 8193), dense,
            new string(' ', 4095) + "\t\t x\t",
            new string('x', 4096) + dense,
            dense + new string('x', 4096),
            new string('x', 127) + new string('\t', 8193),
            string.Concat(Enumerable.Repeat("0123456789abcdef\t", 513)),
            "🙂\t" + new string(' ', 8193) + "\te\u0301"
        };
        foreach (var source in fixtures)
        {
            using var document = new EditorDocument("header\r\n" + source + "\r\ntail");
            var snapshot = document.Snapshot(); var map = new TabColumnMap(snapshot, 1, tabSize);
            Assert.That(map.LineOffset, Is.EqualTo(8)); Assert.That(map.Length, Is.EqualTo(source.Length));
            Assert.That(map.TabCount, Is.EqualTo(source.Count(ch => ch == '\t')));
            var leading = source.TakeWhile(ch => ch is ' ' or '\t').Count();
            Assert.That(map.LeadingWhitespaceLength, Is.EqualTo(leading));
            long column = 0; var consecutiveSpaces = 0;
            for (var offset = 0; offset <= source.Length; offset++)
            {
                Assert.That(map.GetColumn(offset), Is.EqualTo(column));
                Assert.That(map.GetOffset(column), Is.EqualTo(offset));
                Assert.That(map.GetOffset(column, true), Is.EqualTo(offset));
                var softLength = offset == 0 || offset > leading || consecutiveSpaces == 0 ? 0 : Math.Min(consecutiveSpaces, (int)((column - 1) % tabSize + 1));
                Assert.That(map.GetSoftTabBackspaceLength(offset), Is.EqualTo(softLength));
                if (offset == source.Length) break;
                var next = column + (source[offset] == '\t' ? tabSize - column % tabSize : 1);
                if (next - column > 1)
                {
                    foreach (var interior in new[] { column + 1, column + (next - column) / 2, next - 1 })
                    {
                        Assert.That(map.GetOffset(interior), Is.EqualTo(offset));
                        Assert.That(map.GetOffset(interior, true), Is.EqualTo(offset + 1));
                    }
                }
                consecutiveSpaces = source[offset] == ' ' ? consecutiveSpaces + 1 : 0;
                column = next;
            }
            Assert.That(map.ColumnLength, Is.EqualTo(column));
            Assert.That(map.GetOffset(column + 1000), Is.EqualTo(source.Length));
        }
    }

    [Test]
    public void CancellationDoesNotPoisonSubsequentMapConstruction()
    {
        using var document = new EditorDocument(new string('\t', 10000));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var snapshot = document.Snapshot();
        Assert.Throws<OperationCanceledException>(() => new TabColumnMap(snapshot, 0, 8, cancel.Token));
        var map = new TabColumnMap(snapshot, 0, 8);
        Assert.That(map.ColumnLength, Is.EqualTo(80000L)); Assert.That(map.TabCount, Is.EqualTo(10000));
    }

    [Test]
    public void OldSnapshotMappingsRemainIndependentAfterDocumentEdits()
    {
        using var document = new EditorDocument(string.Concat(Enumerable.Repeat("x\t", 4096)));
        var snapshot = document.Snapshot(); var original = new TabColumnMap(snapshot, 0, 4);
        document.ApplyEdits([new TextEdit(0, document.Buffer.TextLength, "new")], snapshot.Revision);
        var oldAgain = new TabColumnMap(snapshot, 0, 4);
        var current = new TabColumnMap(document.Snapshot(), 0, 4);
        Assert.That(oldAgain.ColumnLength, Is.EqualTo(original.ColumnLength));
        Assert.That(oldAgain.ColumnLength, Is.EqualTo(16384L)); Assert.That(current.ColumnLength, Is.EqualTo(3L));
        Assert.That(current.TabCount, Is.Zero);
    }
}
