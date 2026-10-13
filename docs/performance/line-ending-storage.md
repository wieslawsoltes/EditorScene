# Line-ending edit storage review

The completed core run 38458136585 at source dcd8830c725477a232e20ff429b662b56630f002 passed 516 EditorScene, 325 UnoEdit core and 26 rendering tests. Its regression-tests artifact 11845261362 was downloaded and independently hashed as `2b895b4c13d92e546cfbfc79f254c67fc54fbe1ffc9e36f0e42dd8285bf67044`, matching GitHub's artifact digest.

That source's 100,000-line LF planning results (eleven samples, three warmups) exposed a noncanonical allocation regression:

| Input | Implementation | Median ms | Median managed bytes |
| --- | --- | ---: | ---: |
| Canonical LF | Previous all-delimiter replacements | 8.6479 | 1,600,200 |
| Canonical LF | Growing minimal-edit builder | 1.3764 | 656 |
| Mixed (75,000 changes) | Previous all-delimiter replacements | 8.5055 | 1,600,200 |
| Mixed (75,000 changes) | Growing minimal-edit builder | 3.6189 | 5,395,064 |

These are planning-only measurements on .NET 10.0.13, Ubuntu 24.04.5, x64, four reported processors. They exclude edit application, permissions, UI/layout and rendering; they do not establish whole-editor, physical GPU or vendor-relative performance. The complete original report also retains unrelated unfavorable workloads. The source's browser run was later superseded, so this core report is not evidence of a fully validated integration.

## Correction

The updated planner first counts delimiter types using its bounded 4,096-character reader buffer. Canonical input ends after that scan with an empty edit array. Noncanonical input allocates exactly the required number of TextEdit elements, then fills the owned array with a second scan of the same immutable source. The same reader buffer is reused across both scans and cleared on return. No full text string or line index is constructed.

ImmutableCollectionsMarshal.AsImmutableArray wraps the private array without copying. Only this method owns the array; it is never pooled, retained by the scanner, or modified after publication. Concurrent plans allocate independent result storage. The counts and minimal-edit semantics are unchanged.

This intentionally trades a second noncanonical scan for predictable result-sized storage. It is not a claim that every mixed-input timing improves over the preceding one-pass builder. Existing benchmarks remain unchanged so the current result can be compared with both the published numbers above and the original all-delimiter baseline. New tests cover all three target delimiters with 100,000 mixed lines and an allocation budget of exact result storage plus 64 KB, plus concurrent immutable plan ownership. All existing scalar-equivalence, chunk-boundary, cancellation, permission, undo/redo and native/browser checks remain required.

Fresh workflow results are required before claiming the updated source passes or reporting its timings.
