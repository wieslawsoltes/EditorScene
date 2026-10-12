using System.Reflection;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class WhitespaceEditPlanTests
{
    private static string Convert(string text, WhitespaceConversion mode, int tabSize = 4, bool leading = false)
    {
        using var document = new EditorDocument(text); var snapshot = document.Snapshot();
        var plan = WhitespaceEditPlan.Create(snapshot.GetSpan(0, snapshot.Length), mode, tabSize, leading);
        Assert.That(IsFlattened(snapshot), Is.False); plan.Apply(document); return document.Buffer.Text;
    }
    [TestCase("a\tb\t", "a   b   ")]
    [TestCase("\t🙂\t", "    🙂  ")]
    [TestCase("x\t\r\n\ty\r\tz\n", "x   \r\n    y\r    z\n")]
    [TestCase("a\u00a0\t", "a\u00a0  ")]
    public void TabsToSpacesPreservesColumnsUnicodeAndDelimiters(string source, string expected)
        => Assert.That(Convert(source, WhitespaceConversion.Spaces), Is.EqualTo(expected));
    [TestCase("    x    y", "\tx\t y")]
    [TestCase("a   b   ", "a\tb\t")]
    [TestCase("  \t \tx", "\t\tx")]
    [TestCase("  x", "  x")]
    [TestCase("   x ", "   x ")]
    public void SpacesToTabsCanonicalizesOnlyWhitespaceRuns(string source, string expected)
        => Assert.That(Convert(source, WhitespaceConversion.Tabs), Is.EqualTo(expected));
    [Test]
    public void LineTabificationDoesNotChangeInternalWhitespace()
    {
        Assert.That(Convert("    a   b\r\n \tc\td", WhitespaceConversion.Tabs, leading: true), Is.EqualTo("\ta   b\r\n\tc\td"));
        Assert.That(Convert(" \ta\tb\n\tend", WhitespaceConversion.Spaces, leading: true), Is.EqualTo("    a\tb\n    end"));
    }
    [Test]
    public void ScopeStartsInsideIndentationAndUsesTheUnselectedPrefixColumn()
    {
        using var doc = new EditorDocument("ab \tvalue\tend"); var snapshot = doc.Snapshot();
        var plan = WhitespaceEditPlan.Create(snapshot.GetSpan(3, 1), WhitespaceConversion.Spaces);
        Assert.That(plan.Edits.Single(), Is.EqualTo(new TextEdit(3, 1, " "))); plan.Apply(doc);
        Assert.That(doc.Buffer.Text, Is.EqualTo("ab  value\tend"));
        using var indentation = new EditorDocument(" \tvalue"); var s = indentation.Snapshot();
        var part = WhitespaceEditPlan.Create(s.GetSpan(1, 1), WhitespaceConversion.Spaces, leadingOnly: true);
        part.Apply(indentation); Assert.That(indentation.Buffer.Text, Is.EqualTo("    value"));
    }
    [Test]
    public void NonLeadingSelectionCannotBeMistakenForIndentation()
    {
        using var doc = new EditorDocument("a    b"); var s = doc.Snapshot();
        var plan = WhitespaceEditPlan.Create(s.GetSpan(1, 4), WhitespaceConversion.Tabs, leadingOnly: true);
        Assert.That(plan.Edits, Is.Empty); Assert.That(doc.Revision, Is.Zero);
    }
    [TestCase(" a \t\r\n b\t\r\t \nend  ", " a\r\n b\r\nend")]
    [TestCase("a\u00a0 \t", "a\u00a0")]
    [TestCase("a  b", "a  b")]
    [TestCase("    ", "")]
    public void TrailingTrimPreservesEveryNonWhitespaceCharacter(string source, string expected)
        => Assert.That(Convert(source, WhitespaceConversion.TrimTrailing), Is.EqualTo(expected));
    [Test]
    public void PartialTrimCannotDeleteUnselectedOrNonTrailingWhitespace()
    {
        using var doc = new EditorDocument("a    \n"); var s = doc.Snapshot();
        Assert.That(WhitespaceEditPlan.Create(s.GetSpan(1, 2), WhitespaceConversion.TrimTrailing).Edits, Is.Empty);
        var suffix = WhitespaceEditPlan.Create(s.GetSpan(3, 2), WhitespaceConversion.TrimTrailing); suffix.Apply(doc);
        Assert.That(doc.Buffer.Text, Is.EqualTo("a  \n"));
    }
    [Test]
    public void IdempotentCanonicalRunsDoNotAllocateReplacementStringsOrUndoSteps()
    {
        using var doc = new EditorDocument("\t  word\n\t\tend"); var snapshot = doc.Snapshot();
        var plan = WhitespaceEditPlan.Create(snapshot.GetSpan(0, snapshot.Length), WhitespaceConversion.Tabs, leadingOnly: true);
        Assert.That(plan.Edits, Is.Empty); Assert.That(plan.ReplacementCharacters, Is.Zero);
        plan.Apply(doc); Assert.That(doc.Revision, Is.Zero); Assert.That(doc.Buffer.UndoStack.CanUndo, Is.False);
        var empty = WhitespaceEditPlan.Create(snapshot.GetSpan(4, 0), WhitespaceConversion.Spaces);
        Assert.That(empty.CharactersScanned, Is.Zero);
    }
    [Test]
    public void ReaderBoundariesDoNotSplitWhitespaceRunsOrCopyLongCode()
    {
        var text = new string('x', 4095) + "\t" + new string('y', 9000) + " \t\r\n" + new string('z', 12000);
        using var doc = new EditorDocument(text); var s = doc.Snapshot();
        var plan = WhitespaceEditPlan.Create(s.GetSpan(0, s.Length), WhitespaceConversion.Spaces);
        Assert.That(plan.Edits.Length, Is.EqualTo(2)); Assert.That(plan.CharactersScanned, Is.EqualTo(text.Length));
        Assert.That(plan.Edits.All(e => e.Length <= 2 && e.NewText.Length <= 4), Is.True);
        Assert.That(IsFlattened(s), Is.False); plan.Apply(doc);
        Assert.That(doc.Buffer.Text, Is.EqualTo(text.Replace("\t", " ").Replace("y  \r\n", "y    \r\n")));
    }
    [Test]
    public void PlansApplyAtomicallyAndRejectWrongOrStaleDocuments()
    {
        using var doc = new EditorDocument("\ta\n\tb"); using var other = new EditorDocument("\ta\n\tb"); var s = doc.Snapshot();
        var plan = WhitespaceEditPlan.Create(s.GetSpan(0, s.Length), WhitespaceConversion.Spaces);
        Assert.Throws<ArgumentException>(() => plan.Apply(other));
        plan.Apply(doc); Assert.That(doc.Buffer.Text, Is.EqualTo("    a\n    b"));
        doc.Buffer.UndoStack.Undo(); Assert.That(doc.Buffer.Text, Is.EqualTo("\ta\n\tb"));
        Assert.Throws<InvalidOperationException>(() => plan.Apply(doc));
    }
    [Test]
    public void LimitsCancellationAndInvalidInputsNeverReturnPartialPlans()
    {
        using var doc = new EditorDocument("\ta\tb"); var s = doc.Snapshot(); var scope = s.GetSpan(0, s.Length);
        Assert.Throws<InvalidOperationException>(() => WhitespaceEditPlan.Create(scope, WhitespaceConversion.Spaces, maxEdits: 1));
        Assert.Throws<InvalidOperationException>(() => WhitespaceEditPlan.Create(scope, WhitespaceConversion.Spaces, maxReplacementCharacters: 1));
        Assert.Throws<OperationCanceledException>(() => WhitespaceEditPlan.Create(scope, WhitespaceConversion.Spaces, cancellationToken: new CancellationToken(true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => WhitespaceEditPlan.Create(scope, WhitespaceConversion.Spaces, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WhitespaceEditPlan.Create(scope, (WhitespaceConversion)99));
        Assert.Throws<ArgumentException>(() => WhitespaceEditPlan.Create(scope, WhitespaceConversion.TrimTrailing, leadingOnly: true));
        Assert.That(doc.Revision, Is.Zero);
    }
    [Test]
    public async Task PlanningWorksOnWorkersAfterLiveDocumentDisposal()
    {
        var doc = new EditorDocument("\tword"); var s = doc.Snapshot(); doc.Dispose();
        var plan = await Task.Run(() => WhitespaceEditPlan.Create(s.GetSpan(0, s.Length), WhitespaceConversion.Spaces));
        Assert.That(plan.Edits.Single(), Is.EqualTo(new TextEdit(0, 1, "    ")));
    }
    [Test]
    public void RandomCanonicalizationPreservesExpandedTextAndIsIdempotent()
    {
        var random = new Random(672);
        for (var sample = 0; sample < 70; sample++)
        {
            var text = new string(Enumerable.Range(0, 8193).Select(_ => "ab  \t\r\n"[random.Next(7)]).ToArray());
            var size = random.Next(1, 17);
            var tabs = Convert(text, WhitespaceConversion.Tabs, size);
            Assert.That(Convert(tabs, WhitespaceConversion.Spaces, size), Is.EqualTo(Expand(text, size)));
            Assert.That(Convert(tabs, WhitespaceConversion.Tabs, size), Is.EqualTo(tabs));
        }
    }
    private static string Expand(string text, int tabSize)
    {
        var output = new System.Text.StringBuilder(); var column = 0;
        foreach (var c in text)
        {
            if (c == '\t') { var count = tabSize - column % tabSize; output.Append(' ', count); column += count; }
            else { output.Append(c); if (c is '\r' or '\n') column = 0; else column++; }
        }
        return output.ToString();
    }
    private static bool IsFlattened(DocumentSnapshot snapshot) => ((Lazy<string>)typeof(DocumentSnapshot).GetField("_text", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(snapshot)!).IsValueCreated;
}
