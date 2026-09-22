# Item paths and 3D matching (v1.5)

## Navisworks export

Enable **Item Path** in the XML report and include individual clash results. The tracker now reads the native `clashobject/pathlink/node` hierarchy in order, independently for A and B. It supports XML namespaces and retains a legacy SmartTag Item Path fallback. The hierarchy is exported as `File > coordination.nwd > source.nwc > ...` in ItemAPath/ItemBPath.

Earlier versions only read the SmartTag. Consequently, correctly exported native paths could be lost during import. New imports use the corrected parser automatically.

## Repair an existing latest snapshot

With the original, unchanged XML export available, run:

```text
HUMAIN.CoordinationTracker.exe --repair-paths "C:\path\to\original XML folder"
```

The command repairs only missing paths in the latest active snapshot. It validates every retained clash's non-path fields first, rejecting changed IDs, statuses, scope details, conflicting existing paths, or missing records. Extra XML records do not enter the snapshot. A subsequent run is a no-op when no paths are missing.

The existing transaction mechanism creates a backup, stages the changes, rebuilds exports, and publishes with rollback/recovery. The snapshot timestamp, test list and record population remain unchanged. Existing quality-review decisions are carried to their updated metadata hashes only after exact non-path equality is established; the migration is recorded in the review history. It does not approve pending alerts. Refresh Power BI after the command completes.

## Speckle 3D Clash Explorer

The optional 3D extension uses **source model + Revit Element ID**. Match ItemAElementId/ItemBElementId to Speckle `properties.Element.Id`, not Application ID, Object Key, or ClashGuid.

1. Take the deepest model-file node from each item path.
2. Normalize the basename to lowercase, remove `.rvt`, `.nwc`, `.nwd`, `.ifc`, or `.dwg`, and remove a trailing `_detached` only.
3. Apply the same normalization to Speckle `properties.Source File`.
4. Match the normalized model name and Element ID together. There is no fuzzy filename matching and no ID-only fallback.
5. Count distinct source identities using original Source File plus UniqueId. If UniqueId is absent, each Object Key is a separate identity. Exclude ambiguous identities; allow multiple geometry objects belonging to the same unique source element.

Select one clash in the register to load A/B geometry. Missing counterparts appear in the detail panels. With no single selection, the viewer can display its standard Add Model Info and Object Keys placeholder; the fields are already configured.

| Card | Meaning |
|---|---|
| Current clash records | All CurrentClashes rows after slicers, including Approved and retained Resolved. This is not actionable-open count. |
| Both elements matched | Clashes where both sides have exactly one source-element identity for model + ID. |
| Unmatched / ambiguous | Clashes lacking a unique match on either side. |
| Model + ID match coverage | Both elements matched divided by current clash records. |

Register selection filters the viewer and detail panels. Summary cards retain page-slicer scope. Single-direction relationships run from CurrentClashes to Clash3DSides and Clash3DGeometry using TestName + ClashGuid. Progress KPIs never count expanded geometry rows.

## Limits

- Refresh updates CSV and Speckle imports and recalculates mapping tables. The Speckle model URL resolves to its latest version; matching does not prove that version corresponds to the clash snapshot. Upload the corresponding model version.
- Filename changes require aligned source names. Similar discipline names are not enough to assume identity.
- This explores the current snapshot, not historic geometry. Selecting 3D geometry does not reverse-filter the register.
- Element selection does not reconstruct a saved Navisworks viewpoint or independently verify a collision point.
- Missing IDs require a corrected source export; missing source models require those models in the Speckle upload.

## Validation

273 tracker assertions pass, including native/namespace paths, A/B independence, SmartTag fallback, repair idempotence and rejection of changed snapshots. The live 22 September 2026 09:35 snapshot repair restored 59,158 paths. DashboardKPI, DailyProgress, TestPerformance and OperationalProgress stayed byte-identical; quality flags, decisions and reasons remained unchanged.

Live Power BI validation found 29,465 of 29,624 clashes matched on both sides (99.463%). The remaining 159 clashes comprise 108 missing element IDs and 51 source model/ID misses. These are point-in-time results, not fixed targets.

## Distribution

Speckle visuals can store account tokens inside `storedData`. Distribution copies must omit this data and `.pbi` caches. Keep authentication in the local working report only; each recipient signs in with their own account. CSV/XML/project data are not part of the public source.
