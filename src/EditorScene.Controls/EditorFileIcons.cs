using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace EditorScene.Controls;

/// <summary>Original native vector file-command icons; caller owns the returned parentable control.</summary>
public static class EditorFileIcons
{
    /// <summary>Overlapping save documents, inheriting the host's native foreground palette.</summary>
    public static PathIcon CreateSaveAll() => (PathIcon)XamlReader.Load(
        "<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='16' Height='16' " +
        "Data='F0 M0,0 H10 L12,2 V3 H10 V2 H2 V10 H3 V12 H0 Z M4,4 H13 L16,7 V16 H4 Z M6,5 V9 H13 V5 Z M7,11 V15 H13 V11 Z M10,5 H12 V8 H10 Z' />");
}
