# Conversion planning with observed metadata

The format status in line-ending-metadata.md uses the snapshot's immutable CR/LF/CRLF counts. LineEndingEditPlan now reuses completed counts from that same snapshot instead of scanning again solely to size its result array.

An already-canonical snapshot with observed metadata returns an empty revision-bound plan without a reader or source scan. Mixed snapshots still allocate exactly their real edits and scan once to emit them. Unobserved metadata retains the preceding counting scan followed, for noncanonical input, by an emitting scan. Planning does not force another worker's incomplete metadata scan to finish.

All paths keep the requested delimiter, identity, revision and source counts. Cached no-op plans still reject foreign/stale application and cancelled creation. This optimization does not bypass view permissions, macro policy, native undo, or the normal edit application path.

The benchmark retains original all-delimiter replacements and unobserved exact-array planning. Only after measuring both does it initialize metadata and add metadata_cached_plan on the same source. Initial metadata computation/maintenance is excluded from the cached measurement and explicitly identified. The canonical single-plan path can be near timer resolution; allocations and the structural no-reader path are more useful evidence than a claimed submicrosecond interaction latency.

At separately validated core source 61fa0a51b99f9f0752ae541f24d6bcb31fa57c28, before this metadata optimization, mixed 100,000-line planning used 1,200,896 managed bytes instead of the preceding growing builder's 5,395,064 bytes at dcd8830c725477a232e20ff429b662b56630f002. Those are different runs, not a same-run timing comparison. The 61fa0a5 core artifact 11847724315 has independently verified SHA256 66128ece997383e464700156944a467f33fde3e68e990b063f8b3132c311980f and passed 520 EditorScene, 325 UnoEdit core and 26 rendering tests. Its overall workflow failed in the separate overflow Escape browser regression, so this is not evidence of a fully passing integration.

Fresh core, native, package-only and three complete browser passes are required for the new source. Unfavorable compiler and tab-scanning measurements remain in the complete reports. No physical-GPU or universal end-to-end speedup is claimed.
