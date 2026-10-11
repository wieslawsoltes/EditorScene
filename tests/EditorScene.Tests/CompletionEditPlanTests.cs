using EditorScene.LanguageServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class CompletionEditPlanTests
{
    [Test]
    public void AdditionalImportsAndPrimaryEditAreOneUndoUnit()
    {
        using var document = new EditorDocument("class C { Lis value; }"); var before = document.Snapshot(); var start = before.Text.IndexOf("Lis", StringComparison.Ordinal);
        var plan = new CompletionEditPlan(before, start + 3, new(start, 3), "List<int>", [new(0, 0, "using System.Collections.Generic;\n")]);
        var commit = plan.Prepare(before); document.ApplyEdits(commit.Edits, before.Revision);
        Assert.That(document.Buffer.Text, Is.EqualTo("using System.Collections.Generic;\nclass C { List<int> value; }"));
        Assert.That(document.Buffer.GetText(0, commit.CaretOffset), Does.EndWith("List<int>"));
        document.Buffer.UndoStack.Undo(); Assert.That(document.Buffer.Text, Is.EqualTo(before.Text));
    }
    [Test]
    public void TypingToFilterCompletionTranslatesAllRemainingReplacementText()
    {
        using var document = new EditorDocument("prefix LisSuffix end"); var original = document.Snapshot();
        var plan = new CompletionEditPlan(original, 10, new(7, 9), "List", [new(0, 0, "import\n")]);
        document.ApplyEdits([new(10, 0, "t")], document.Revision); var current = document.Snapshot();
        var commit = plan.Prepare(current); document.ApplyEdits(commit.Edits, current.Revision);
        Assert.That(document.Buffer.Text, Is.EqualTo("import\nprefix List end"));
        Assert.That(commit.CaretOffset, Is.EqualTo(18));
    }
    [Test]
    public void UnrelatedEditsInvalidateCompletionEvenWhenUndone()
    {
        using var document = new EditorDocument("aa Lis"); var original = document.Snapshot(); var plan = new CompletionEditPlan(original, 6, new(3, 3), "List");
        document.ApplyEdits([new(0, 0, "x")], document.Revision); document.Buffer.UndoStack.Undo();
        Assert.Throws<InvalidOperationException>(() => plan.Prepare(document.Snapshot()));
    }
    [Test]
    public void OverlappingOrSurrogateSplittingEditsAreRejectedBeforeMutation()
    {
        using var document = new EditorDocument("🙂 name"); var snapshot = document.Snapshot();
        Assert.Throws<ArgumentException>(() => new CompletionEditPlan(snapshot, 7, new(3, 4), "value", [new(5, 1, "x")]));
        Assert.Throws<ArgumentException>(() => new CompletionEditPlan(snapshot, 1, new(1, 0), "x"));
        Assert.Throws<ArgumentException>(() => new CompletionEditPlan(snapshot, 0, new(3, 4), "x"));
        Assert.That(document.Revision, Is.Zero);
    }
    [Test]
    public void DifferentDocumentAndEarlierSnapshotCannotUsePlan()
    {
        using var document = new EditorDocument("a"); using var other = new EditorDocument("a"); var first = document.Snapshot();
        document.ApplyEdits([new(1, 0, "b")], document.Revision); var plan = new CompletionEditPlan(document.Snapshot(), 2, new(0, 2), "c");
        Assert.Throws<InvalidOperationException>(() => plan.Prepare(first));
        Assert.Throws<InvalidOperationException>(() => plan.Prepare(other.Snapshot()));
    }
}
