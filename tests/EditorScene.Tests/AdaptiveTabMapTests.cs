using EditorScene.Text;
using Xunit;

namespace EditorScene.Tests;

public sealed class AdaptiveTabMapTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(1000)]
    public void BothChunkPathsPreserveEveryOffsetAndTabInterior(int tabSize)
    {
        var dense = string.Concat(Enumerable.Repeat(" \t", 4097));
        var fixtures = new[]
        {
            new string('\t', 8193), dense,
            new string(' ', 4095) + "\t\t x\t", // reader boundary and indentation termination
            new string('x', 4096) + dense,
            dense + new string('x', 4096),
            new string('x', 127) + new string('\t', 8193), // sparse sample before dense remainder
            string.Concat(Enumerable.Repeat("0123456789abcdef\t", 513)),
            "🙂\t" + new string(' ', 8193) + "\te\u0301"
        };
        foreach (var source in fixtures)
        {
            using var document = new EditorDocument("header\r\n" + source + "\r\ntail");
            var snapshot = document.Snapshot(); var map = new TabColumnMap(snapshot, 1, tabSize);
            Assert.Equal(8, map.LineOffset); Assert.Equal(source.Length, map.Length);
            Assert.Equal(source.Count(ch => ch == '\t'), map.TabCount);
            var leading = source.TakeWhile(ch => ch is ' ' or '\t').Count();
            Assert.Equal(leading, map.LeadingWhitespaceLength);
            long column = 0; var consecutiveSpaces = 0;
            for (var offset = 0; offset <= source.Length; offset++)
            {
                Assert.Equal(column, map.GetColumn(offset));
                Assert.Equal(offset, map.GetOffset(column));
                Assert.Equal(offset, map.GetOffset(column, true));
                var softLength = offset == 0 || offset > leading || consecutiveSpaces == 0 ? 0 : Math.Min(consecutiveSpaces, (int)((column - 1) % tabSize + 1));
                Assert.Equal(softLength, map.GetSoftTabBackspaceLength(offset));
                if (offset == source.Length) break;
                var next = column + (source[offset] == '\t' ? tabSize - column % tabSize : 1);
                if (next - column > 1)
                {
                    foreach (var interior in new[] { column + 1, column + (next - column) / 2, next - 1 })
                    {
                        Assert.Equal(offset, map.GetOffset(interior));
                        Assert.Equal(offset + 1, map.GetOffset(interior, true));
                    }
                }
                consecutiveSpaces = source[offset] == ' ' ? consecutiveSpaces + 1 : 0;
                column = next;
            }
            Assert.Equal(column, map.ColumnLength);
            Assert.Equal(source.Length, map.GetOffset(column + 1000));
        }
    }

    [Fact]
    public void CancellationDoesNotPoisonSubsequentMapConstruction()
    {
        using var document = new EditorDocument(new string('\t', 10000));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var snapshot = document.Snapshot();
        Assert.Throws<OperationCanceledException>(() => new TabColumnMap(snapshot, 0, 8, cancel.Token));
        var map = new TabColumnMap(snapshot, 0, 8);
        Assert.Equal(80000L, map.ColumnLength); Assert.Equal(10000, map.TabCount);
    }

    [Fact]
    public void OldSnapshotMappingsRemainIndependentAfterDocumentEdits()
    {
        using var document = new EditorDocument(string.Concat(Enumerable.Repeat("x\t", 4096)));
        var snapshot = document.Snapshot(); var original = new TabColumnMap(snapshot, 0, 4);
        document.ApplyEdits([new TextEdit(0, document.Buffer.TextLength, "new")], snapshot.Revision);
        var oldAgain = new TabColumnMap(snapshot, 0, 4);
        var current = new TabColumnMap(document.Snapshot(), 0, 4);
        Assert.Equal(original.ColumnLength, oldAgain.ColumnLength);
        Assert.Equal(16384L, oldAgain.ColumnLength); Assert.Equal(3L, current.ColumnLength);
        Assert.Equal(0, current.TabCount);
    }
}
