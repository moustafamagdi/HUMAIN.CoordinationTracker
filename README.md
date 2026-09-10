# HUMAIN Coordination Tracker

Local Navisworks clash-history tracker for the HUMAIN project.

## Daily workflow

1. Refresh the NWC files in the coordination NWF.
2. Run all Clash Detective tests.
3. Export reports as **All tests (separate) / XML**.
4. Put the XML files in one folder.
5. Run `HUMAIN.CoordinationTracker`.
6. Enter the XML folder and snapshot date.
7. Refresh Power BI.

The tracker uses the Navisworks clash result GUID as the historical identity and classifies each daily result as:

- `New`
- `Existing`
- `Resolved`
- `Reopened`

Manual Navisworks workflow status is kept separately from the tracker state.

## Local storage

`%LOCALAPPDATA%\HUMAIN.CoordinationTracker`

Snapshots are stored under:

`Snapshots\yyyy-MM-dd\`

Re-importing an existing date replaces that snapshot and rebuilds all derived files chronologically, so imports can be corrected or inserted out of order.

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
- Open / Resolved
- Age Days
- Age Bucket
- Reopen Count
- Resolution Count
- Last Navisworks Status
- Last Distance
- Test / Severity / Discipline Pair

### CurrentClashes.csv
Current open clashes enriched with:

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

## Recommended Power BI pages

1. **Executive Overview** — KPI cards, burndown, rolling burn rate, forecast.
2. **Clash Tests** — test ranking, open/new/resolved trend, resolution rate.
3. **Coordination Analysis** — severity, discipline pair, grid/location analysis.
4. **Aging & Forecast** — age buckets, stale clashes, reopen behavior, finish forecast.

## Framework

C# / .NET Framework 4.8. No external packages are required.
