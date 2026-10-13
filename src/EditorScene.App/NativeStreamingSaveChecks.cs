using System.Text;
using EditorScene.Controls;
using EditorScene.Text;
using Windows.Storage;

namespace EditorScene.App;

internal static class NativeStreamingSaveChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Streaming save: " + name); checks.Add(name); }
        var file = await ApplicationData.Current.TemporaryFolder.CreateFileAsync("editorscene-save-" + Guid.NewGuid().ToString("N") + ".txt", CreationCollisionOption.GenerateUniqueName);
        try
        {
            var text = new string('a', 4095) + "🙂\r\n日本語\rline\n" + new string('b', 30000);
            using var document = new EditorDocument(); document.Buffer.Text = text;
            var snapshot = document.Snapshot(); var encoding = new UTF8Encoding(true, true);
            var content = SnapshotSaveContent.Create(snapshot, encoding);
            await EditorPage.WritePreparedFileAsync(file, content);
            var bytes = await ReadBytesAsync();
            Check(bytes.SequenceEqual(encoding.GetPreamble().Concat(encoding.GetBytes(text))), "Actual StorageFile streaming preserves BOM, boundary surrogates and mixed line endings");
            Check(document.IsModified && document.Revision == snapshot.Revision, "Provider writing does not itself mark caller-owned documents saved");
            document.Buffer.Text = "short"; var smaller = SnapshotSaveContent.Create(document.Snapshot(), new UTF8Encoding(false, true));
            await EditorPage.WritePreparedFileAsync(file, smaller);
            Check((await ReadBytesAsync()).SequenceEqual(Encoding.UTF8.GetBytes("short")), "Saving a shorter document truncates the actual destination without retaining the old tail");
            document.Buffer.Text = "changed after capture";
            await EditorPage.WritePreparedFileAsync(file, content);
            Check((await ReadBytesAsync()).SequenceEqual(bytes), "Prepared native saves write their captured revision rather than later source text");
            using var invalid = new EditorDocument("invalid\uD800"); var rejected = false;
            try { SnapshotSaveContent.Create(invalid.Snapshot(), new UTF8Encoding(false, true)); } catch (EncoderFallbackException) { rejected = true; }
            Check(rejected && (await ReadBytesAsync()).SequenceEqual(bytes), "Strict encoder preflight rejects invalid UTF-16 before the existing destination is touched");
            using var empty = new EditorDocument(); var emptyContent = SnapshotSaveContent.Create(empty.Snapshot(), new UnicodeEncoding(false, true, true));
            await EditorPage.WritePreparedFileAsync(file, emptyContent);
            Check((await ReadBytesAsync()).SequenceEqual(new byte[] { 255, 254 }), "Empty encoded native save contains only its selected BOM");
            Check(EditorPage.IsSaveAllGesture(Windows.System.VirtualKey.F12, true, true, false) && !EditorPage.IsSaveAllGesture(Windows.System.VirtualKey.S, true, false, true) && !EditorPage.IsSaveAllGesture(Windows.System.VirtualKey.S, true, true, false), "Save All uses its nontext shortcut without consuming AltGr-letter or existing Save As gestures");
            var firstIcon = EditorFileIcons.CreateSaveAll(); var secondIcon = EditorFileIcons.CreateSaveAll();
            Check(!ReferenceEquals(firstIcon, secondIcon) && firstIcon.Data is not null && secondIcon.Data is not null, "Save All native vector controls are independent and need no icon font");
            var plan = DocumentSavePlan.Create([new(document.Id, true), new(empty.Id, false)]);
            Check(plan.DocumentIds.Length == 1 && plan.DocumentIds[0] == document.Id, "Native Save All planning excludes clean files and retains the dirty document identity");
        }
        finally { await file.DeleteAsync(); }
        async Task<byte[]> ReadBytesAsync()
        {
            using var source = await file.OpenStreamForReadAsync(); using var output = new MemoryStream();
            await source.CopyToAsync(output); return output.ToArray();
        }
    }
}
