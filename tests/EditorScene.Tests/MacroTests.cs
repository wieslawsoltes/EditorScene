using System.Text;
using System.Xml;
using EditorScene.Text.Macros;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class MacroTests
{
    [Test]
    public void RecordPauseResumeStopAndCurrentSnapshotAreIndependent()
    {
        var recorder = new MacroRecorder(); var states = new List<MacroRecordingState>();
        recorder.StateChanged += (_, _) => states.Add(recorder.State);
        recorder.Record(); recorder.NotifyEditAction(MacroStep.InsertText("one"));
        var first = recorder.CurrentMacroAction!;
        recorder.Pause(); Assert.That(recorder.NotifyEditAction(MacroStep.InsertText("ignored")), Is.False);
        recorder.Resume(); recorder.NotifyEditAction(new("editor.SelectAll")); var result = recorder.Stop()!;
        Assert.That(result.Steps.Length, Is.EqualTo(2)); Assert.That(first.Steps.Length, Is.EqualTo(1));
        Assert.That(recorder.CurrentMacroAction, Is.Null);
        Assert.That(states, Is.EqualTo(new[] { MacroRecordingState.Recording, MacroRecordingState.Paused, MacroRecordingState.Recording, MacroRecordingState.Idle }));
    }
    [Test]
    public void EmptyStopAndCancelledRecordingPreservePreviousMacro()
    {
        var recorder = new MacroRecorder(); var previous = new EditorMacro([MacroStep.InsertText("previous")]); recorder.SetLastMacro(previous);
        recorder.Record(); recorder.Stop(); Assert.That(recorder.LastMacroAction, Is.SameAs(previous));
        recorder.Record(); recorder.NotifyEditAction(MacroStep.InsertText("discard")); recorder.Pause(); recorder.Cancel();
        Assert.That(recorder.LastMacroAction, Is.SameAs(previous)); Assert.That(recorder.RecordedStepCount, Is.Zero);
    }
    [Test]
    public async Task PlaybackIsOrderedRepeatableAndDoesNotRecordItself()
    {
        var recorder = new MacroRecorder(); recorder.SetLastMacro(new([new("first"), new("second")])); var seen = new List<string>();
        var count = await recorder.PlayAsync(async (step, _) =>
        {
            await Task.Yield(); seen.Add(step.Command);
            Assert.That(recorder.NotifyEditAction(new("not-recorded")), Is.False);
        }, 2);
        Assert.That(seen, Is.EqualTo(new[] { "first", "second", "first", "second" })); Assert.That(count, Is.EqualTo(4));
        Assert.That(recorder.State, Is.EqualTo(MacroRecordingState.Idle)); Assert.That(recorder.LastMacroAction!.Steps.Length, Is.EqualTo(2));
    }
    [Test]
    public void IllegalStateTransitionsAreRejected()
    {
        var recorder = new MacroRecorder(); Assert.Throws<InvalidOperationException>(recorder.Pause); Assert.Throws<InvalidOperationException>(recorder.Cancel);
        recorder.Record(); Assert.Throws<InvalidOperationException>(recorder.Record); Assert.Throws<InvalidOperationException>(recorder.Resume);
        Assert.Throws<InvalidOperationException>(() => recorder.SetLastMacro(new([]))); recorder.Cancel();
        Assert.ThrowsAsync<InvalidOperationException>(() => recorder.PlayAsync((_, _) => ValueTask.CompletedTask));
    }
    [Test]
    public async Task RecursivePlaybackAndRecordingDuringPlaybackAreRejected()
    {
        var recorder = new MacroRecorder(); recorder.SetLastMacro(new([new("action")]));
        await recorder.PlayAsync((_, _) =>
        {
            Assert.Throws<InvalidOperationException>(recorder.Record);
            Assert.Throws<InvalidOperationException>(recorder.Cancel);
            Assert.ThrowsAsync<InvalidOperationException>(() => recorder.PlayAsync((_, _) => ValueTask.CompletedTask));
            return ValueTask.CompletedTask;
        });
    }
    [Test]
    public void PlaybackExceptionAlwaysRestoresIdleAndKeepsSavedMacro()
    {
        var recorder = new MacroRecorder(); var macro = new EditorMacro([new("fail")]); recorder.SetLastMacro(macro);
        Assert.ThrowsAsync<IOException>(() => recorder.PlayAsync((_, _) => throw new IOException("test")));
        Assert.That(recorder.State, Is.EqualTo(MacroRecordingState.Idle)); Assert.That(recorder.LastMacroAction, Is.SameAs(macro));
    }
    [Test]
    public void CancellationStopsBeforeTheNextActionAndKeepsCompletedWork()
    {
        using var token = new CancellationTokenSource(); var recorder = new MacroRecorder(); recorder.SetLastMacro(new([new("one"), new("two")])); var count = 0;
        Assert.ThrowsAsync<OperationCanceledException>(() => recorder.PlayAsync((_, _) => { count++; token.Cancel(); return ValueTask.CompletedTask; }, cancellationToken: token.Token));
        Assert.That(count, Is.EqualTo(1)); Assert.That(recorder.State, Is.EqualTo(MacroRecordingState.Idle));
    }
    [Test]
    public void ActionAndTextBudgetsAreCheckedBeforeAppend()
    {
        var recorder = new MacroRecorder(); recorder.Record(); recorder.NotifyEditAction(MacroStep.InsertText(new('x', EditorMacro.MaxTextLength)));
        Assert.Throws<InvalidOperationException>(() => recorder.NotifyEditAction(MacroStep.InsertText("x"))); Assert.That(recorder.RecordedStepCount, Is.EqualTo(1));
        Assert.Throws<ArgumentException>(() => new EditorMacro(Enumerable.Repeat(new MacroStep("a"), EditorMacro.MaxSteps + 1)));
        Assert.Throws<ArgumentException>(() => new MacroStep("System.Type, assembly"));
        recorder.Stop(); Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => recorder.PlayAsync((_, _) => ValueTask.CompletedTask, 1001));
    }
    [Test]
    public void XmlPreservesUnicodeTabsCarriageReturnsAndCallerStreamOwnership()
    {
        var macro = new EditorMacro([MacroStep.InsertText("  🙂 e\u0301\r\n\t日本語 <&>  "), new("editor.Uppercase"), new("custom.command", "line\rnext")]);
        using var stream = new MemoryStream(); MacroSerializer.Save(stream, macro); Assert.That(stream.CanWrite, Is.True); stream.Position = 0;
        var copy = MacroSerializer.Load(stream); Assert.That(copy.Steps, Is.EqualTo(macro.Steps)); Assert.That(stream.CanRead, Is.True);
    }
    [TestCase("<!DOCTYPE x SYSTEM 'file:///etc/passwd'><Macro/>")]
    [TestCase("<!DOCTYPE x [<!ENTITY a 'expand'>]><Macro>&a;</Macro>")]
    public void XmlProhibitsDtdAndExternalEntities(string xml)
    { using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)); Assert.Throws<XmlException>(() => MacroSerializer.Load(stream)); }
    [TestCase("<Macro xmlns='urn:editorscene:macro:1' version='2'/>")]
    [TestCase("<Macro xmlns='urn:editorscene:macro:1' version='1'><Action command='text'><Nested/></Action></Macro>")]
    [TestCase("<Macro xmlns='urn:editorscene:macro:1' version='1'><Execute assembly='evil'/></Macro>")]
    [TestCase("<Macro xmlns='urn:editorscene:macro:1' version='1' assembly='evil'/>")]
    public void XmlRejectsUnsupportedSchemaAndExecutableMetadata(string xml)
    { using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)); Assert.Throws<FormatException>(() => MacroSerializer.Load(stream)); }
}
