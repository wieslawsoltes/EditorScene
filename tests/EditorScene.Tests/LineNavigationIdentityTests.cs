using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LineNavigationIdentityTests
{
    [Test]
    public void AFileRenameDoesNotInvalidateAnUnchangedTextPosition()
    {
        using var document = new EditorDocument("first\nsecond", "old.cs"); var snapshot = document.Snapshot();
        Assert.That(LineNavigationTarget.TryParse(snapshot, "2:3", out var target, out _), Is.True);
        document.FileName = "renamed.vb"; var renamed = document.Snapshot();
        Assert.That(renamed, Is.Not.SameAs(snapshot)); Assert.That(target.IsCurrent(renamed), Is.True);
        Assert.That(LineNavigationTarget.TryParse(renamed, "2:3", out var again, out _), Is.True);
        Assert.That(again, Is.EqualTo(target)); Assert.That(document.Revision, Is.Zero);
    }
    [TestCase("\n")]
    [TestCase("\r")]
    [TestCase("\r\n")]
    public void TheLineEndNeverIncludesAnyPartOfItsDelimiter(string delimiter)
    {
        using var document = new EditorDocument("x" + delimiter + "y"); var snapshot = document.Snapshot();
        Assert.That(LineNavigationTarget.TryParse(snapshot, "1:2", out var before, out _), Is.True);
        Assert.That(before.Offset, Is.EqualTo(1));
        Assert.That(LineNavigationTarget.TryParse(snapshot, "1:3", out _, out var error), Is.False);
        Assert.That(error, Is.EqualTo(LineNavigationError.CharacterOutOfRange));
        Assert.That(LineNavigationTarget.TryParse(snapshot, "2:2", out var end, out _), Is.True);
        Assert.That(end.Offset, Is.EqualTo(snapshot.Length));
    }
}
