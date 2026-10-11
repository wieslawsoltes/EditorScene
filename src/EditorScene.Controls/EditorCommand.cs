namespace EditorScene.Controls;

public enum EditorCommand
{
    Undo, Redo, Cut, Copy, Paste, Delete, SelectAll, SelectLine, DuplicateLine, DeleteLine,
    Find, Replace, FindNext, FindPrevious, GoToLine, Indent, Outdent, Comment, Uncomment,
    Uppercase, Lowercase, TrimTrailingWhitespace, SortLines, TabsToSpaces, SpacesToTabs,
    CompleteWord, QuickInfo, SignatureHelp, FormatDocument,
    ToggleBookmark, NextBookmark, PreviousBookmark, ClearBookmarks, ToggleBreakpoint,
    ToggleFold, CollapseAll, ExpandAll, ToggleWordWrap, ToggleWhitespace, ToggleLineNumbers,
    ToggleIndentGuides, ToggleRuler, ToggleReadOnly, ZoomIn, ZoomOut, ResetZoom,
    SelectNextOccurrence, ClearExtraSelections, ConvertToLf, ConvertToCrLf
}
