# Snapshot readers

`SnapshotReader` in `EditorScene.Text` is a seekable `TextReader` over an immutable snapshot or a bounded `SnapshotSpan`. It uses a single 4,096-character window from the rope instead of flattening the complete document. Each reader has its own mutable cursor; a snapshot and independent readers may be used on separate workers. Do not share a reader concurrently.

```csharp
var snapshot = document.Snapshot(); // Capture on the document's owning thread.
using var reader = snapshot.CreateReader();
reader.SkipWhitespace();
var checkpoint = reader.CapturePosition();
if (reader.Matches("class", StringComparison.Ordinal))
    reader.ReadText(5);
else
    reader.RestorePosition(checkpoint);
```

`Position` and `SeekOrigin.Begin` use absolute document offsets. A reader over a span can only seek between that span's start and end; `SeekOrigin.End` means the upper bound of the span. A checkpoint is valid only for the identical snapshot and a position within the reader's bounds. `Clone` creates an independent cursor and shares only immutable data.

`Peek`, `Read`, `PeekPrevious` and `ReadPrevious` operate on UTF-16 units, with -1 denoting EOF/beginning and 0 denoting an actual NUL. `TryReadRune` and `TryReadPreviousRune` read Unicode scalars without crossing the span boundaries. A malformed surrogate or a pair split by a boundary yields one replacement scalar; these APIs do not implement grapheme navigation.

`ReadText` and `PeekText` require the entire requested range and reject excess lengths without advancing. Buffer reads return as much as available, like `TextReader`. `ReadLine` recognizes CR, LF and CRLF, including delimiters across cache boundaries. `ReadToEnd` allocates only the requested remaining text. `Matches` uses the selected .NET `StringComparison`.

Readers remain valid after live document edits or disposal. Contract tests cover range boundaries, overflow, invalid arguments, disposal, speculative reads, Unicode, line endings, cross-window reads and worker use; a regression also verifies that sequential scans do not initialize the snapshot's flattened-text cache. This is an EditorScene API, not a binary-compatible implementation of Actipro's complete snapshot-reader/token-reader interface.
