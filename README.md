# HUMAIN Coordination Tracker

Local Navisworks clash-history tracker for the HUMAIN project.

## v1.6: clearer import and quality feedback

Before confirming an import or replacement, the manager lists added, removed and retained tests with before/after record counts. Replacement compares against the selected snapshot; interval-quality checks still compare against the preceding active snapshot.

The export-folder check includes subfolders and warns when XML modification times span more than five minutes. This is an advisory heuristic, not proof of stale data; no files are automatically excluded. A fresh empty export folder remains the most reliable way to avoid leftovers.

The staged review and saved result list cleared, remaining and new quality alerts across active intervals. Accepted scope alerts explicitly say `Scope change - accepted review`: the decision is saved, but the scope issue remains. These changes do not alter forecast gating, clash classification or source data. Validation: 280 assertions pass.

## v1.5: native item paths

Navisworks XML Item Path is now read from `pathlink/node`, with legacy SmartTag fallback. Existing latest-snapshot paths can be recovered from the original XML using `--repair-paths "C:\path\to\XML"`; the transaction rejects non-path differences and preserves progress and existing review decisions. See [item-path repair and Speckle 3D matching](docs/item-path-and-3d.md) for the workflow and source-model + Element ID matching rules.

## Snapshot Manager (Windows interface)

Launch the executable without arguments to open the snapshot list, import preview, selected-snapshot replacement, reversible exclusion/restoration, and backup controls. Changes rebuild in staging and publish with rollback/recovery. Refresh Power BI only after completion.

[Arabic Snapshot Manager guide](docs/snapshot-manager.ar.md)

Use `--console` for the previous interactive command-line workflow, or `--gui --data-root C:\path\to\project` for an isolated project. Backup restore of a whole project is not a UI feature in this release; Restore refers to excluded snapshots.

## Daily workflow

1. Refresh the NWC files in the coordination NWF.
2. Run all Clash Detective tests.
3. Export reports as **All tests (separate) / XML**.
4. Put the XML files in one folder.
5. Run `HUMAIN.CoordinationTracker`.
6. Choose the XML folder and actual snapshot timestamp, review Preview import, then approve.
7. Refresh Power BI.

The tracker uses the Navisworks clash result GUID as the historical identity and classifies each daily result as:

- `New`
- `Existing`
- `Resolved`
- `Reopened`

Navisworks status is retained separately, but `Resolved` now closes a clash immediately, even when it is still present in the export. Disappearance also closes an unresolved GUID. A later Compact of an already resolved GUID adds **zero** further resolutions. A resolved GUID returning to an unresolved status is `Reopened`.

`Approved` means accepted, not physically resolved. `Reviewed` remains actionable. Unknown statuses also remain actionable. Baseline closed/approved records are stocks rather than interval progress.

## Operational dashboard (September 2026)

`OperationalProgress.csv` is rebuilt for every test and snapshot alongside the original exports:

- `TotalCurrent`: all rows physically present in the snapshot, including Approved and Resolved.
- `Unresolved`: present rows excluding Resolved; includes Approved.
- `Actionable`: present rows excluding both Approved and Resolved.
- `Approved`, `Reviewed`, `ResolvedPresent`: current status stocks.
- `ResolvedByStatus`, `ResolvedByDisappearance`: mutually exclusive routes into resolution during the interval.
- `ResolvedFromActionable`, `ApprovedFromActionable`: mutually exclusive exits from actionable work.
- `NewActionable`, `ReturnedActionable`: new work and work returning from a closed, accepted or previously absent state.
- `NewlyApproved`: observed transitions to Approved (first seen already approved is not an observed approval event).
- `ApprovalRevoked`: Approved to actionable; already included in ReturnedActionable, so do not add it again.

Each non-baseline test/interval is checked against:

`PreviousActionable + NewActionable + ReturnedActionable - ResolvedFromActionable - ApprovedFromActionable = Actionable`

Approval followed by resolution contributes once to actionable reduction. Resolution still appears in the physical closure count. `CurrentClashes.csv` intentionally retains **all current records** for status auditing. Its name does not imply every row is actionable. `DailyProgress.Current`, `TestPerformance.Current` and legacy `DashboardKPI.OpenClashes` count unresolved including Approved; use OperationalProgress for actionable reporting. The legacy daily rolling forecast follows unresolved stock; the Power BI actionable forecast uses operational movement and requires at least three distinct snapshot dates within its recent window.

Export the full, consistent test scope including status rows. The agreed disappearance rule cannot distinguish Compact from rows omitted by a filtered export. Rebuilding cannot recover statuses never exported. All available snapshots are rebuilt chronologically; original snapshot files are preserved.

Non-interactive maintenance:

```text
HUMAIN.CoordinationTracker.exe --self-test
HUMAIN.CoordinationTracker.exe --rebuild
HUMAIN.CoordinationTracker.exe --rebuild --data-root C:\path\to\test-data
```

The self-test covers status transitions, repeated Resolved, later Compact, reopening, approvals, revocation, baseline and flow reconciliation. The project builds on .NET Framework 4.8 without external packages.

## Local storage

`%LOCALAPPDATA%\HUMAIN.CoordinationTracker`

Snapshots are stored under:

`Snapshots\yyyy-MM-dd\`

Multiple snapshots per day are supported. In the UI, replacement targets the selected snapshot and preserves its timestamp; new imports use the selected date and time. Active snapshots rebuild chronologically, so imports can be corrected or inserted out of order. Excluded snapshots remain on disk and are omitted from calculations.

## Power BI folder

`%LOCALAPPDATA%\HUMAIN.CoordinationTracker\PowerBI`

Generated datasets:

### DashboardKPI.csv
One-row executive dataset for cards:

- Open clashes
- New
- Reopened
- Resolved
- Net change / net burn
- rolling 7-day resolution rate
- rolling 7-day clash inflow
- rolling 7-day net burn rate
- forecast days to zero
- forecast finish date
- average open clash age
- stale 14+ day clashes
- critical open clashes
- total clash tests

### DailyProgress.csv
Historical trend dataset for burndown and forecast visuals. Includes:

- Previous / Current
- New / Reopened / Existing / Resolved
- Inflow
- Net Change
- Net Burn
- Resolution Rate %
- Rolling 7-day Resolved per Day
- Rolling 7-day New per Day
- Rolling 7-day Net Burn per Day
- Forecast Days to Zero
- Forecast Finish Date

A finish forecast is intentionally blank when rolling net burn is zero or negative.

### TestPerformance.csv
Daily test-level performance, including zero-clash tests:

- Severity
- Discipline A / B
- Discipline Pair
- Previous / Current
- New / Reopened / Existing / Resolved
- Net Change
- Resolution Rate %

Severity and disciplines are derived from the HUMAIN clash-test naming convention.

### ClashLifecycle.csv
One row per unique clash GUID:

- First Seen
- Last Seen
- Last Resolved
- Open / Approved / Resolved
- Age Days
- Age Bucket
- Reopen Count
- Resolution Count
- Last Navisworks Status
- Last Distance
- Test / Severity / Discipline Pair

### CurrentClashes.csv
All physically present current clashes (including Approved and retained Resolved), enriched with:

- Tracker State
- First Seen
- Age Days
- Age Bucket
- Reopen Count
- Stale 14+ flag
- Severity
- Discipline Pair
- Grid location
- Clash point
- element IDs / item names

### ClashHistory.csv
Snapshot-level historical detail for drill-through and auditing.

## Power BI dashboards and documentation

The complete Dark and Light reports share one semantic model in [powerbi](powerbi/README.md). Pages: Executive Overview, Clash Test Performance, Critical & Aging, Clash Details, Resolution & Approvals, and Snapshot Quality.

- [Arabic guide: every card, formula, interpretation and meeting workflow](docs/dashboard-guide.ar.md)
- [Reporting periods and quality rules](docs/reporting-periods-and-quality.md)

`SnapshotQuality.csv` records missing/added tests, large physical record drops (at least 100 and 30%), and unknown statuses. Alerts flag possible export issues without changing closure counts. The actionable forecast is suppressed when recent quality alerts exist.

The self-test currently passes 204 workflow, 11 quality and 22 snapshot-management assertions. Local exports, snapshots, binaries and Power BI caches are not distributed. Configure SourceFolder before the first report refresh.

## Framework

C# / .NET Framework 4.8. No external packages are required.

## Quality reviews (1.2)

Use **Quality reviews** in the Windows manager to record Pending Review, Accepted or Needs Correction with a required reason and reviewer. Decisions are bound to source interval fingerprints and included in backup/recovery. Original alerts and operational counts remain unchanged. Power BI forecasts gate on unresolved action requirements, not all historical flags. Missing/added test scope alerts remain blockers even if accepted.

[Arabic quality review guide](docs/quality-reviews.ar.md). Use 1.2 for all imports/rebuilds; older executables omit review columns. The complete test suite now has 255 assertions.
