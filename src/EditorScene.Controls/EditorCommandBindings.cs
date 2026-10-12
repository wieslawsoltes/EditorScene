using EditorScene.LanguageServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace EditorScene.Controls;

/// <summary>UI-thread-owned bindings for caller-owned buttons/menu items. The collection
/// follows one active editor, queries each distinct command once per refresh, and only
/// writes changed native properties. Removing a binding does not dispose its control.</summary>
public sealed class EditorCommandBindings : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private Registration[] _registrations = [];
    private Group[] _groups = [];
    private SyntaxEditor? _editor;
    private long _generation;
    private bool _refreshing, _pending, _disposed;
    public int Count => _registrations.Length;
    public int DistinctCommandCount => _groups.Length;
    public long RefreshCount { get; private set; }
    public long QueryCount { get; private set; }
    public long PropertyWriteCount { get; private set; }
    public event EventHandler<Exception>? ExecutionFailed;
    public SyntaxEditor? Editor
    {
        get => _editor;
        set
        {
            Verify();
            if (value?.IsDisposed == true) throw new ObjectDisposedException(nameof(value));
            value?.Document.VerifyAccess();
            if (ReferenceEquals(value, _editor)) return;
            DetachEditor(); _editor = value; _generation++;
            if (value is not null) { value.StateChanged += OnStateChanged; value.AnalysisUpdated += OnAnalysisUpdated; }
            Refresh();
        }
    }
    /// <summary>Bind a native Button/ToggleButton or MenuFlyoutItem/ToggleMenuFlyoutItem.
    /// executeOnClick=false updates state only and leaves execution to the host.
    /// An optional original-vector child is dimmed when disabled; its initial opacity is retained.
    /// Dispose the returned registration to detach one binding.</summary>
    public IDisposable Bind(Control target, EditorCommand command, UIElement? glyph = null, bool executeOnClick = true)
    {
        Verify(); ArgumentNullException.ThrowIfNull(target);
        if (target is not ButtonBase and not MenuFlyoutItem) throw new ArgumentException("Use a ButtonBase or MenuFlyoutItem.", nameof(target));
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        if (_registrations.Any(r => ReferenceEquals(r.Target, target))) throw new ArgumentException("This control already has a binding in the collection.", nameof(target));
        var registration = new Registration(this, target, command, glyph, executeOnClick);
        _registrations = [.. _registrations, registration]; RebuildGroups();
        try { Refresh(); return registration; }
        catch { registration.Dispose(); throw; }
    }
    /// <summary>Synchronously refresh for host changes not represented by StateChanged.
    /// Property callbacks may rebind/dispose the collection; stale remaining writes stop.
    /// Feedback that cannot settle within eight passes is explicitly rejected.</summary>
    public void Refresh()
    {
        Verify();
        if (_refreshing) { _pending = true; return; }
        _refreshing = true; RefreshCount++;
        try
        {
            for (var pass = 0; pass < 8; pass++)
            {
                _pending = false;
                if (_editor?.IsDisposed == true) { DetachEditor(); _editor = null; _generation++; }
                var generation = _generation; var editor = _editor; var groups = _groups;
                foreach (var group in groups)
                {
                    var state = editor?.GetCommandState(group.Command, EditorCommandQueryMode.CommandSurface) ?? default;
                    if (editor is not null) QueryCount++;
                    if (_disposed) return;
                    if (generation != _generation) { _pending = true; break; }
                    foreach (var binding in group.Registrations)
                    {
                        if (binding.Target.IsEnabled != state.IsEnabled)
                        { PropertyWriteCount++; binding.Target.IsEnabled = state.IsEnabled; }
                        if (_disposed) return;
                        if (generation != _generation) { _pending = true; break; }
                        if (binding.Target is ToggleButton toggle && toggle.IsChecked != state.IsChecked)
                        { PropertyWriteCount++; toggle.IsChecked = state.IsChecked; }
                        else if (binding.Target is ToggleMenuFlyoutItem item && item.IsChecked != state.IsChecked)
                        { PropertyWriteCount++; item.IsChecked = state.IsChecked; }
                        if (_disposed) return;
                        if (generation != _generation) { _pending = true; break; }
                        if (binding.Glyph is { } glyph)
                        {
                            var opacity = state.IsEnabled ? binding.EnabledOpacity : binding.EnabledOpacity * .35;
                            if (glyph.Opacity != opacity) { PropertyWriteCount++; glyph.Opacity = opacity; }
                        }
                        if (_disposed) return;
                        if (generation != _generation) { _pending = true; break; }
                    }
                    if (_pending) break;
                }
                if (!_pending) return;
            }
            throw new InvalidOperationException("Command binding callbacks did not settle after eight refresh passes.");
        }
        finally { _refreshing = false; }
    }
    /// <summary>Recheck command advice at invocation, then use the editor's normal execution
    /// and protection path. False means unavailable or changed before invocation, not an edit failure.</summary>
    public async Task<bool> TryExecuteAsync(EditorCommand command)
    {
        Verify();
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        var editor = _editor;
        if (editor is null || editor.IsDisposed) return false;
        var generation = _generation; var document = editor.Document; var revision = document.Revision; var language = editor.SyntaxLanguage;
        var state = editor.GetCommandState(command, EditorCommandQueryMode.CommandSurface);
        if (!state.IsEnabled || _disposed || editor.IsDisposed || generation != _generation || !ReferenceEquals(_editor, editor)
            || !ReferenceEquals(editor.Document, document) || document.Revision != revision || !ReferenceEquals(editor.SyntaxLanguage, language)) return false;
        await editor.ExecuteAsync(command);
        if (!_disposed) Refresh();
        return true;
    }
    private void OnStateChanged(object? sender, EventArgs args) { if (!_disposed) { _generation++; Refresh(); } }
    private void OnAnalysisUpdated(object? sender, LanguageAnalysis analysis) => OnStateChanged(sender, EventArgs.Empty);
    private void DetachEditor()
    { if (_editor is { } editor) { editor.StateChanged -= OnStateChanged; editor.AnalysisUpdated -= OnAnalysisUpdated; } }
    private void RebuildGroups()
    {
        _groups = _registrations.GroupBy(r => r.Command).Select(g => new Group(g.Key, g.ToArray())).ToArray();
        _generation++;
    }
    private void Remove(Registration registration)
    {
        Verify();
        _registrations = _registrations.Where(r => !ReferenceEquals(r, registration)).ToArray();
        RebuildGroups(); Refresh();
    }
    private void Verify()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Command bindings belong to their creating UI thread.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        Verify(); _disposed = true; _generation++; DetachEditor(); _editor = null; ExecutionFailed = null;
        foreach (var registration in _registrations) registration.Detach();
        _registrations = []; _groups = [];
    }
    private sealed record Group(EditorCommand Command, Registration[] Registrations);
    private sealed class Registration : IDisposable
    {
        private EditorCommandBindings? _owner;
        private readonly bool _executeOnClick;
        internal Control Target { get; }
        internal EditorCommand Command { get; }
        internal UIElement? Glyph { get; }
        internal double EnabledOpacity { get; }
        internal Registration(EditorCommandBindings owner, Control target, EditorCommand command, UIElement? glyph, bool executeOnClick)
        {
            _owner = owner; Target = target; Command = command; Glyph = glyph; EnabledOpacity = glyph?.Opacity ?? 1; _executeOnClick = executeOnClick;
            if (executeOnClick)
            {
                if (target is ButtonBase button) button.Click += OnClick;
                else ((MenuFlyoutItem)target).Click += OnClick;
            }
        }
        private async void OnClick(object sender, RoutedEventArgs args)
        {
            var owner = _owner;
            if (owner is null || owner._disposed || !Target.IsEnabled) return;
            try { await owner.TryExecuteAsync(Command); }
            catch (Exception error) { if (!owner._disposed) owner.ExecutionFailed?.Invoke(owner, error); }
        }
        internal void Detach()
        {
            if (_owner is null) return;
            _owner = null;
            if (_executeOnClick)
            {
                if (Target is ButtonBase button) button.Click -= OnClick;
                else ((MenuFlyoutItem)Target).Click -= OnClick;
            }
        }
        public void Dispose()
        {
            var owner = _owner; if (owner is null) return;
            owner.Verify(); Detach(); owner.Remove(this);
        }
    }
}
