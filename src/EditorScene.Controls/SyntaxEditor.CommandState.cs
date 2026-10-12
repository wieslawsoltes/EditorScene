using EditorScene.LanguageServices;

namespace EditorScene.Controls;

/// <summary>Cheap view-state advice for command surfaces, not a protected-range authorization.
/// Native execution still performs permission checks. Clipboard is not read to query Paste.</summary>
public readonly record struct EditorCommandState(bool IsEnabled, bool IsChecked = false);

public sealed partial class SyntaxEditor
{
    private bool HasContextBookmark()
    {
        var items = Indicators.Items;
        for (var i = 0; i < items.Count; i++) if (items[i].Kind == EditorScene.Text.IndicatorKind.Bookmark) return true;
        return false;
    }
    public EditorCommandState GetCommandState(EditorCommand command)
    {
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        if (_disposed) return default;
        Document.VerifyAccess();
        if (MacroSessionActive) return default;
        var editable = !IsReadOnly && !_editor.IsReadOnly;
        var single = !Selections.IsActive;
        var selection = SelectionLength != 0;
        var text = Document.Buffer.TextLength != 0;
        var ordinary = editable && single && !IsSnippetActive;
        var capabilities = SyntaxLanguage.Capabilities;
        return command switch
        {
            EditorCommand.Undo => new(editable && CanUndo),
            EditorCommand.Redo => new(editable && CanRedo),
            EditorCommand.Copy => new(single && selection),
            EditorCommand.Cut => new(ordinary && selection),
            EditorCommand.Paste => new(ordinary), // Authorization/content can only be checked on user execution.
            EditorCommand.Delete => new(ordinary && (selection || CaretOffset < Document.Buffer.TextLength)),
            EditorCommand.SelectAll or EditorCommand.SelectLine => new(text),
            EditorCommand.Uppercase or EditorCommand.Lowercase => new(ordinary && selection),
            EditorCommand.ConvertSelectedSpacesToTabs or EditorCommand.ConvertSelectedTabsToSpaces => new(ordinary && selection),
            EditorCommand.Indent or EditorCommand.Outdent or EditorCommand.DuplicateLine or EditorCommand.DeleteLine
                or EditorCommand.Comment or EditorCommand.Uncomment or EditorCommand.TrimTrailingWhitespace or EditorCommand.SortLines
                or EditorCommand.TabsToSpaces or EditorCommand.SpacesToTabs or EditorCommand.TabifySelectedLines or EditorCommand.UntabifySelectedLines
                or EditorCommand.ConvertToLf or EditorCommand.ConvertToCrLf => new(ordinary && text),
            EditorCommand.FormatDocument => new(ordinary && capabilities.HasFlag(LanguageCapabilities.Formatting)),
            EditorCommand.CompleteWord => new(ordinary && capabilities.HasFlag(LanguageCapabilities.Completion)),
            EditorCommand.QuickInfo => new(capabilities.HasFlag(LanguageCapabilities.QuickInfo)),
            EditorCommand.SignatureHelp => new(capabilities.HasFlag(LanguageCapabilities.SignatureHelp)),
            EditorCommand.CollapseAll or EditorCommand.ExpandAll or EditorCommand.ToggleFold => new(Analysis is { Foldings.IsEmpty: false }),
            EditorCommand.ContractSelection => new(SelectionExpansionDepth > 0),
            EditorCommand.ExpandSelection => new(single && text),
            EditorCommand.ClearExtraSelections => new(Selections.IsActive),
            EditorCommand.ClearBookmarks or EditorCommand.NextBookmark or EditorCommand.PreviousBookmark => new(HasContextBookmark()),
            EditorCommand.ToggleWordWrap => new(true, WordWrap),
            EditorCommand.ToggleWhitespace => new(true, ShowWhitespace),
            EditorCommand.ToggleLineNumbers => new(true, ShowLineNumbers),
            EditorCommand.ToggleIndentGuides => new(true, ShowIndentGuides),
            EditorCommand.ToggleRuler => new(true, ShowRuler),
            EditorCommand.ToggleReadOnly => new(true, IsReadOnly),
            EditorCommand.ToggleDefaultContextMenu => new(true, IsDefaultContextMenuEnabled),
            EditorCommand.ToggleSecondaryPointerCaret => new(true, CanMoveCaretForSecondaryPointerButton),
            EditorCommand.ZoomIn => new(FontSize < 40),
            EditorCommand.ZoomOut => new(FontSize > 8),
            EditorCommand.ResetZoom => new(FontSize != 13),
            _ => new(true)
        };
    }
}
