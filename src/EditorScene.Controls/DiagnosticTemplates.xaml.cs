using Microsoft.UI.Xaml;

namespace EditorScene.Controls;

/// <summary>Compiled native templates, including the recyclable diagnostic presenter.
/// Compilation preserves template types in trimmed desktop/WebAssembly applications.</summary>
public sealed partial class DiagnosticTemplates : ResourceDictionary
{
    public DiagnosticTemplates() => InitializeComponent();
}
