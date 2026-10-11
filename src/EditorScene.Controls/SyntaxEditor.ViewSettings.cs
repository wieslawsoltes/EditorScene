namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    /// <summary>Whether this control has released its native view. The host still owns external documents/providers.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Copy view-owned settings, not text, carets, selection, undo history, services or event handlers.
    /// Use before attaching a new split to the visual tree. Subsequent changes remain independent.</summary>
    public void CopyViewSettingsFrom(SyntaxEditor source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source._disposed, source);
        Document.VerifyAccess(); source.Document.VerifyAccess();
        if (ReferenceEquals(source, this)) return;
        // Read all settings before applying any that can raise host callbacks.
        var readOnly = source.IsReadOnly; var wrap = source.WordWrap; var numbers = source.ShowLineNumbers;
        var whitespace = source.ShowWhitespace; var guides = source.ShowIndentGuides; var ruler = source.ShowRuler;
        var brackets = source.AutoCompleteBrackets; var completion = source.AutoShowCompletion;
        var fontSize = source.FontSize; var theme = source.Theme;
        IsReadOnly = readOnly; WordWrap = wrap; ShowLineNumbers = numbers;
        ShowWhitespace = whitespace; ShowIndentGuides = guides; ShowRuler = ruler;
        AutoCompleteBrackets = brackets; AutoShowCompletion = completion;
        FontSize = fontSize;
        if (!_disposed) Theme = theme;
    }
}
