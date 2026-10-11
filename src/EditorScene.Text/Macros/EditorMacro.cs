using System.Collections.Immutable;

namespace EditorScene.Text.Macros;

/// <summary>A data-only edit action. Command identifiers are resolved by an explicit host, never by reflection.</summary>
public sealed record MacroStep
{
    public MacroStep(string command, string argument = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command); ArgumentNullException.ThrowIfNull(argument);
        if (command.Length > 128 || command.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-'))
            throw new ArgumentException("Command IDs must contain only ASCII letters, numbers, dots, underscores or hyphens.", nameof(command));
        if (argument.Length > EditorMacro.MaxTextLength) throw new ArgumentOutOfRangeException(nameof(argument));
        Command = command; Argument = argument;
    }
    public string Command { get; }
    public string Argument { get; }
    public static MacroStep InsertText(string text) => new("text", text);
}

/// <summary>Immutable reusable macro. Text is UTF-16; limits apply to the complete action sequence.</summary>
public sealed class EditorMacro
{
    public const int MaxSteps = 10_000;
    public const int MaxTextLength = 1_048_576;
    public EditorMacro(IEnumerable<MacroStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var builder = ImmutableArray.CreateBuilder<MacroStep>(); var textLength = 0;
        foreach (var step in steps)
        {
            ArgumentNullException.ThrowIfNull(step);
            if (builder.Count == MaxSteps || step.Argument.Length > MaxTextLength - textLength)
                throw new ArgumentException("The macro exceeds its action or text limit.", nameof(steps));
            builder.Add(step); textLength += step.Argument.Length;
        }
        Steps = builder.ToImmutable(); TextLength = textLength;
    }
    public ImmutableArray<MacroStep> Steps { get; }
    public int TextLength { get; }
}

public enum MacroRecordingState { Idle, Recording, Paused, Playing }

/// <summary>
/// UI-independent, single-owner recorder. Playback executes sequentially on the caller's
/// synchronization context. Cancellation/failure keeps completed edits and always exits
/// playback; hosts decide their undo policy. Recording never executes an action.
/// </summary>
public sealed class MacroRecorder
{
    private readonly List<MacroStep> _steps = [];
    private int _textLength;
    public MacroRecordingState State { get; private set; }
    public EditorMacro? LastMacroAction { get; private set; }
    public EditorMacro? CurrentMacroAction => State is MacroRecordingState.Recording or MacroRecordingState.Paused ? new(_steps) : null;
    public int RecordedStepCount => _steps.Count;
    public event EventHandler? StateChanged;
    public void Record()
    {
        Require(MacroRecordingState.Idle); _steps.Clear(); _textLength = 0; ChangeState(MacroRecordingState.Recording);
    }
    public void Pause() { Require(MacroRecordingState.Recording); ChangeState(MacroRecordingState.Paused); }
    public void Resume() { Require(MacroRecordingState.Paused); ChangeState(MacroRecordingState.Recording); }
    public EditorMacro? Stop()
    {
        RequireRecording();
        if (_steps.Count > 0) LastMacroAction = new(_steps);
        _steps.Clear(); _textLength = 0; ChangeState(MacroRecordingState.Idle); return LastMacroAction;
    }
    public void Cancel()
    {
        RequireRecording(); _steps.Clear(); _textLength = 0; ChangeState(MacroRecordingState.Idle);
    }
    public void SetLastMacro(EditorMacro macro)
    {
        ArgumentNullException.ThrowIfNull(macro); Require(MacroRecordingState.Idle);
        LastMacroAction = macro; StateChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Called before an edit action runs. Paused/idle/playback calls are ignored.</summary>
    public bool NotifyEditAction(MacroStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (State != MacroRecordingState.Recording) return false;
        if (_steps.Count == EditorMacro.MaxSteps || step.Argument.Length > EditorMacro.MaxTextLength - _textLength)
            throw new InvalidOperationException("Macro recording limit reached. Stop or cancel this recording.");
        _steps.Add(step); _textLength += step.Argument.Length; return true;
    }
    /// <summary>Play the saved macro, without recording playback or permitting recursive playback.</summary>
    public async Task<int> PlayAsync(Func<MacroStep, CancellationToken, ValueTask> execute, int repeat = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execute); Require(MacroRecordingState.Idle);
        if (repeat < 1 || repeat > 1_000) throw new ArgumentOutOfRangeException(nameof(repeat));
        var macro = LastMacroAction ?? throw new InvalidOperationException("No macro has been recorded or loaded.");
        if ((long)repeat * macro.Steps.Length > 100_000) throw new ArgumentOutOfRangeException(nameof(repeat), "Playback may execute at most 100,000 actions.");
        cancellationToken.ThrowIfCancellationRequested();
        var completed = 0;
        try
        {
            ChangeState(MacroRecordingState.Playing);
            for (var iteration = 0; iteration < repeat; iteration++)
                foreach (var step in macro.Steps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await execute(step, cancellationToken); completed++;
                }
            return completed;
        }
        finally { ChangeState(MacroRecordingState.Idle); }
    }
    private void Require(MacroRecordingState expected)
    { if (State != expected) throw new InvalidOperationException($"The recorder must be {expected}, not {State}."); }
    private void RequireRecording()
    { if (State is not (MacroRecordingState.Recording or MacroRecordingState.Paused)) throw new InvalidOperationException("There is no active recording."); }
    private void ChangeState(MacroRecordingState state) { State = state; StateChanged?.Invoke(this, EventArgs.Empty); }
}
