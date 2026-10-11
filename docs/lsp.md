# Language Server Protocol integration

EditorScene.Protocol now includes a reusable `LspSyntaxLanguage` adapter. It implements the same `ISyntaxLanguage` contract as the built-in C#/VB/JSON/XML providers, so native Uno controls do not depend on protocol details. This is a bounded, explicitly connected LSP feature set, not a claim to implement every LSP or Actipro feature.

## Connect in the sample

Open **Language → Connect Language Server…**. Supply a trusted WebSocket endpoint, the absolute document URI expected by that server, and its language ID. Confirming Connect sends the current document and subsequent edits to that endpoint. `wss://` is supported; insecure `ws://` is accepted only for loopback development. The browser's own security, origin and mixed-content rules still apply. No endpoint credentials are stored; the app does not launch a server process or automatically reconnect.

The connection belongs to the selected document. Split views share the same provider, while other tabs retain their own language services. The status bar identifies LSP mode. **Disconnect Language Server** restores the previous built-in provider and performs didClose/shutdown/exit. Closing a document also disposes its connection. Upload/save still use normal file pickers; a successful save optionally sends the negotiated didSave notification. A failed server save notification does not undo an already successful file write.

GitHub Pages is static hosting and does not run a language server. Use a separately deployed WebSocket-to-LSP server appropriate to the document language. Browser arbitrary authorization headers and desktop process launching are not implemented by the sample. A host can construct its own authenticated transport and pass the connection to the reusable provider.

## Embed in another Uno host

```csharp
using EditorScene.Protocol;
using EditorScene.Text;
using System.Net.WebSockets;

// Capture on the document owner/UI thread before any await.
DocumentSnapshot snapshot = document.Snapshot();
var socket = new ClientWebSocket();
await socket.ConnectAsync(trustedEndpoint, cancellationToken);
var provider = await LspSyntaxLanguage.OpenAsync(
    new JsonRpcConnection(new WebSocketJsonRpcTransport(socket)),
    snapshot, new Uri("file:///workspace/Example.cs"), "csharp",
    cancellationToken: cancellationToken);
editor.SyntaxLanguage = provider;
// On teardown: first detach all controls/coordinators from the provider.
// Then await provider.DisposeAsync(); the provider owns this connection.
```

`StreamJsonRpcTransport` can be supplied instead for an explicitly managed desktop stream. A provider owns one document URI and connection; independent views of that same document may share it. Use separate sessions for distinct documents. Provider requests use a default 30-second deadline and linked cancellation. The host should explicitly dispose the provider rather than abandoning network resources.

## Implemented protocol surface

Initialization advertises supported capabilities and accepts UTF-16 positions only. The client negotiates full or incremental text synchronization, open/close and save behavior. Incremental changes preserve surrogate-pair and CRLF boundaries. A live provider requires both open/close and text changes; it refuses a server that cannot receive the edited document.

Only advertised requests are made. Supported results are plain-text completion (arrays or CompletionList, edit-range defaults, insert/replace edits, and additional text edits), hover, signature help including parameter label offsets, hierarchical/flat document symbols, nested folding regions, full and delta relative semantic tokens and document formatting. Full and unchanged pull-diagnostic reports retain result IDs. Versioned push diagnostics invalidate only the matching document revision and are routed through the existing analysis coordinator.

A document snapshot lease is held through each response. Newer didChange messages cannot race an older request. Cancellation releases the lease; late RPC replies are discarded by request ID. Results are validated before reaching the editor: malformed UTF-16 positions, overlapping edits, crossing folds, oversized arrays, excessive symbol depth, invalid semantic tokens and foreign-document symbols are rejected or filtered as documented in the converter.

Completion commits use the reusable `CompletionEditPlan`. It honors the entire server replacement range, including suffix text after the caret, and applies imports/additional edits in one undo group. Typing inside the completion span is translated through version history. An unrelated document edit invalidates the plan, even when that edit is subsequently undone. All edits are checked against read-only protection before mutation.

Incremental text changes use bounded snapshot history and persistent line indexes instead of scanning unchanged prefixes. Semantic-token decoding avoids per-token JSON position allocations. See [performance details](performance.md) for algorithms, size limits, cancellation/recovery behavior and benchmark methodology.

## Deliberate boundaries

- **Unversioned push diagnostics are ignored**, because the adapter cannot prove which snapshot they describe. Use versioned push notifications or a pull-diagnostic server. Stale/future notifications are also ignored; counters expose this to the host.
- Server completion snippets, indentation adjustment and executable completion commands are not advertised. Such completion items are skipped with a protocol warning, never executed or silently flattened into misleading text. Completion resolve, retriggering incomplete lists, full filter/commit-character parity and per-item UI matching behavior remain future work.
- Remote text and documentation are displayed as text, not interpreted as HTML or executed.
- Server requests to apply workspace edits or execute commands have no handler and receive method-not-found. No other file is opened or changed. Rename/refactoring, code actions, references, cross-document navigation, workspace folders, file watching and dynamic registration are not integrated.
- Full and negotiated delta semantic tokens are supported with validated immutable caching and a single full replacement on an invalid delta base. Cancellation never retries; malformed full recovery fails. Token range requests and multiline/overlapping tokens are not advertised. Server token modifiers are validated but not yet styled individually.
- Native UI interaction coverage does not establish universal compatibility with third-party language servers. A test peer is intentionally synthetic and identified as such.

## Validation

The LSP unit tests use actual paired JSON-RPC connections over channel transports, not mocked return methods on the provider. They cover synchronization negotiation, Unicode boundaries, versions, cancellation, snapshot leases, diagnostics, safe result conversion and completion transactions.

`tools/lsp-fixture.mjs` is a synthetic WebSocket language server used only by browser tests. The published .NET/Uno app connects through its real dialog and `ClientWebSocket`. Tests inspect wire messages and drive native input to verify remote analysis, suffix-replacing completion with an added import, undo/redo, hover/signatures, formatting, real save downloads and didSave, rejected workspace edits and teardown. The fixture is not a shipped compiler or a substitute for third-party-server conformance testing.

Reference: Language Server Protocol 3.17 specification, https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/ .
