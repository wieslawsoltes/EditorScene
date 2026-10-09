using System.ComponentModel;
using EditorScene.LanguageServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UnoEdit.CodeCompletion;
using UnoEdit.Document;
using UnoEdit.Editing;
using Completion = EditorScene.LanguageServices.CompletionItem;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private CompletionWindow? _completion;
    private InsightWindow? _insight;
    private CancellationTokenSource? _intelliRequest;
    public bool IsCompletionOpen => _completion?.IsOpen == true;
    public int CompletionCount => _completion?.CompletionList.CurrentList.Count ?? 0;
    public IReadOnlyList<string> CompletionLabels => _completion?.CompletionList.CurrentList.Select(i => i.Text).ToArray() ?? [];
    public bool IsSignatureHelpOpen => _insight is OverloadInsightWindow && _insight.IsOpen;
    public bool IsQuickInfoOpen => _insight?.IsOpen == true;
    private CancellationToken BeginIntelliRequest()
    {
        _intelliRequest?.Cancel(); _intelliRequest?.Dispose(); _intelliRequest = new(); return _intelliRequest.Token;
    }
    public async Task ShowCompletionAsync()
    {
        if (IsReadOnly || !SyntaxLanguage.Capabilities.HasFlag(LanguageCapabilities.Completion)) { SetStatus("This language does not provide completion."); return; }
        var token = BeginIntelliRequest(); var document = Document; var snapshot = document.Snapshot(); var caret = CaretOffset; var language = SyntaxLanguage;
        try
        {
            var items = await language.CompleteAsync(snapshot, caret, token);
            if (_disposed || token.IsCancellationRequested || !ReferenceEquals(Document, document) || Document.Revision != snapshot.Revision || CaretOffset != caret || !ReferenceEquals(SyntaxLanguage, language)) return;
            _completion?.Dispose(); _completion = null;
            if (items.IsEmpty) { SetStatus("No completion items at this position."); return; }
            var start = items[0].ReplaceRange?.Offset ?? caret;
            if (start < 0 || start > caret) throw new InvalidOperationException("The language returned an invalid completion span.");
            var window = new CompletionWindow(_editor.TextArea) { StartOffset = start, EndOffset = caret, CloseWhenCaretAtBeginning = false };
            using (window.CompletionList.DeferRefresh())
                foreach (var item in items) window.CompletionList.CompletionData.Add(new CompletionData(item));
            _completion = window;
            window.Closed += (_, _) => { if (ReferenceEquals(_completion, window)) _completion = null; StateChanged?.Invoke(this, EventArgs.Empty); };
            FocusEditor(); window.Show(); SetStatus($"{items.Length} completion items"); StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public async Task ShowQuickInfoAsync()
    {
        var token = BeginIntelliRequest(); var document = Document; var snapshot = document.Snapshot(); var caret = CaretOffset; var language = SyntaxLanguage;
        try
        {
            var info = await language.GetQuickInfoAsync(snapshot, caret, token);
            if (_disposed || token.IsCancellationRequested || !ReferenceEquals(Document, document) || Document.Revision != snapshot.Revision || CaretOffset != caret) return;
            if (info is null) { SetStatus("No symbol information at the caret."); return; }
            _insight?.Dispose();
            var panel = new StackPanel { Spacing = 6 };
            panel.Children.Add(new TextBlock { Text = info.Signature, FontFamily = new FontFamily("monospace"), TextWrapping = TextWrapping.Wrap, FontSize = 13 });
            if (info.Documentation.Length > 0) panel.Children.Add(new TextBlock { Text = info.Documentation, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            _insight = new InsightWindow(_editor.TextArea)
            {
                StartOffset = info.Range.Offset, EndOffset = info.Range.End,
                Child = new Border { Background = Brush(Theme.Chrome), BorderBrush = Brush(Theme.Border), BorderThickness = new Thickness(1), Padding = new Thickness(10), MaxWidth = 560, Child = panel }
            };
            FocusEditor(); _insight.Show(); StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public async Task ShowSignatureHelpAsync()
    {
        var token = BeginIntelliRequest(); var document = Document; var snapshot = document.Snapshot(); var caret = CaretOffset; var language = SyntaxLanguage;
        try
        {
            var help = await language.GetSignatureHelpAsync(snapshot, caret, token);
            if (_disposed || token.IsCancellationRequested || !ReferenceEquals(Document, document) || Document.Revision != snapshot.Revision || CaretOffset != caret) return;
            if (help is null || help.Signatures.IsEmpty) return;
            _insight?.Dispose();
            _insight = new OverloadInsightWindow(_editor.TextArea) { StartOffset = caret, EndOffset = document.Buffer.TextLength, Provider = new SignatureProvider(help) };
            FocusEditor(); _insight.Show(); StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private void ClosePopups()
    { _intelliRequest?.Cancel(); _intelliRequest?.Dispose(); _intelliRequest = null; _completion?.Dispose(); _completion = null; _insight?.Dispose(); _insight = null; }

    private sealed class CompletionData(Completion item) : ICompletionData
    {
        public ImageSource? Image => null;
        public string Text => item.Label;
        public object Content => item.Label;
        public object Description => new TextBlock { Text = item.Description, TextWrapping = TextWrapping.Wrap, MaxWidth = 320, FontSize = 12 };
        public double Priority => 0;
        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            using (textArea.Document.RunUpdate())
            { textArea.Session.Select(completionSegment.Offset, completionSegment.Length); textArea.Session.ReplaceSelection(item.InsertText); }
        }
    }
    private sealed class SignatureProvider : IOverloadProvider
    {
        private readonly SignatureHelp _help; private int _index;
        public SignatureProvider(SignatureHelp help) { _help = help; _index = Math.Clamp(help.ActiveSignature, 0, help.Signatures.Length - 1); }
        public int SelectedIndex { get => _index; set { _index = Math.Clamp(value, 0, Count - 1); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); } }
        public int Count => _help.Signatures.Length;
        public string CurrentIndexText => $"{SelectedIndex + 1} of {Count}";
        public object CurrentHeader => new TextBlock { Text = _help.Signatures[_index].Label, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("monospace"), FontSize = 13 };
        public object CurrentContent
        {
            get
            {
                var signature = _help.Signatures[_index];
                var parameter = _help.ActiveParameter < signature.Parameters.Length ? "Parameter: " + signature.Parameters[_help.ActiveParameter] : "";
                return new TextBlock { Text = parameter + (signature.Documentation.Length > 0 ? "\n" + signature.Documentation : ""), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
