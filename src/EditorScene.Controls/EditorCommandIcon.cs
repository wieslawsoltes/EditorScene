using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace EditorScene.Controls;

/// <summary>Original monochrome native command glyphs. No font or external image dependency.
/// Each call creates an independently parentable IconElement; command surfaces retain it.</summary>
public static class EditorCommandIcon
{
    public static PathIcon? Create(EditorCommand command)
    {
        var path = command switch
        {
            EditorCommand.Undo => "M1,6 L6,1 L6,4 L10,4 C14,4 16,7 15,12 L13,12 C14,8 13,6 10,6 L6,6 L6,9 Z",
            EditorCommand.Redo => "M15,6 L10,1 L10,4 L6,4 C2,4 0,7 1,12 L3,12 C2,8 3,6 6,6 L10,6 L10,9 Z",
            EditorCommand.Copy => "F0 M1,1 H11 V3 H3 V11 H1 Z M5,5 H15 V15 H5 Z M7,7 V13 H13 V7 Z",
            EditorCommand.Paste => "F0 M5,1 H11 V4 H5 Z M2,3 H4 V6 H12 V3 H14 V15 H2 Z M4,8 V13 H12 V8 Z",
            EditorCommand.Cut => "F0 M7,8 L11,1 L13,1 L9,9 L13,12 L12,14 L8,10 L5,14 C1,17 -2,11 2,9 C4,8 5,9 6,10 Z M3,10 C0,10 0,14 3,14 C6,14 6,10 3,10 Z M1,2 L3,1 L7,6 L6,8 Z",
            EditorCommand.Delete => "M2,3 L3,2 L8,7 L13,2 L14,3 L9,8 L14,13 L13,14 L8,9 L3,14 L2,13 L7,8 Z",
            EditorCommand.CompleteWord => "M9,0 L3,9 H7 L6,16 L14,6 H9 Z",
            EditorCommand.QuickInfo => "M7,2 H9 V4 H7 Z M7,6 H9 V14 H7 Z",
            _ => null
        };
        return path is null ? null : (PathIcon)XamlReader.Load("<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='16' Height='16' Data='" + path + "'/>");
    }
}
