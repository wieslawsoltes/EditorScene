# Boundary-read allocation correction

The first bulk-storage regression at9b1c575b5b43595de930f005ffd9ff1a71ea1b8d failed in workflow38471133822. Of543 EditorScene tests,542 passed and the new allocation test failed: identical1,000-edit transactions used1,858,800 bytes without observed metadata and1,920,880 bytes with metadata, exceeding the unchanged4KB overhead limit. The suite correctly stopped; no complete core pass is claimed for that revision.

Inspection of the pinned UnoEdit source explains an additional cost beyond immutable metadata objects: TextDocument.GetCharAt calls Rope's indexer, which builds an ImmutableStack of cache nodes when its cache is empty. Every mutation clears that cache. The metadata updater's two neighbor reads can therefore add cache nodes after each edit. GetTextAsMemory uses the same cache and is not a fix.

DocumentBoundaryReader instead uses the public TextDocument.WriteTextTo range API to read exactly one UTF-16 boundary unit. The pinned CharRope writer visits the leaf directly, without creating index-cache stacks. This tiny owner-thread scratch writer is created once per observed document, accepts one code unit, clears its scratch value in finally and retains no source, arrays or strings. It does not change the dependency, use reflection/private APIs or flatten text. Removed/inserted source counting and all CRLF semantics remain unchanged.

The test retains its4KB limit and now covers both1,000 and4,097 edits. All existing short-input scalar, chunk-boundary, undo/redo and old-snapshot cases also exercise the new reads. Fresh full validation is required before claiming the repair passes. This is a specific allocation correction, not proof that all edit/runtime costs are eliminated.

Inspected dependency: https://github.com/wieslawsoltes/AvaloniaEdit/blob/1a2df473f157a0127d2559f241dc712d1ce60188/src/UnoEdit.Core/Utils/Rope.cs and CharRope.cs in the same directory.
