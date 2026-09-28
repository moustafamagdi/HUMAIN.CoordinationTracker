# Reporting periods and snapshot quality

Open **HUMAIN Dashboard.pbip**. Refresh loads the Tracker CSV exports from the configured SourceFolder.

Executive Overview, Clash Test Performance, Resolution & Approvals and Snapshot Quality now offer All history, Today, Last 7 days and Custom period. The date range is used only for Custom period. Period choices are local to each page.

Movement cards sum observed non-baseline transitions whose snapshot timestamp falls within the selected calendar dates. A transition is attributed to the snapshot where it was first observed; its exact real-world completion time is unknown. Current stock cards use the latest snapshot before the end of the period, even if that snapshot predates its start. The stock date is always displayed. A period with no exports shows zero observed movement and a notice explaining that stock is last known.

Opening actionable + new + returned - actionable resolved - actionable approved = closing actionable. Resolution rate is actionable resolved divided by opening actionable plus new and returned work. Resolved - All also includes resolution of previously approved clashes, so it can exceed the actionable resolved component used in the balance.

Critical & Aging and Clash Details continue to show current-export records only.

SnapshotQuality.csv is generated automatically by the Tracker. Alerts identify tests absent from the next export, added or renamed tests, blank/unrecognized statuses and per-test drops of at least 30% AND 100 records. A test listed in the export with zero clashes differs from an absent test. Baseline tests are not treated as newly added tests. Counts refer to test/snapshot occurrences, not unique tests.

Alerts are review signals, not proof of a bad export. Compact can explain a drop. No rule triggered does not guarantee complete exports. Alerts do not change resolution counts. Forecasts are withheld when the trailing seven-day input contains quality alerts, and otherwise still require at least three observed dates and positive net burn.

Initial implementation validation (historical, not repeated for the September 28 review): 204 workflow assertions and 11 quality assertions; all available non-baseline test/interval balances; Desktop refresh, all-history totals, Today without an export, custom date range boundaries and the quality register. Original raw snapshots were unchanged by the initial rebuild.

## Executive review — September 28, 2026

Executive Overview now shows net reduction both including and excluding approvals. The latter subtracts Actionable Approved from Net Actionable Reduction; it still includes GUID disappearance under the project's resolution rule. Captions separate approval-driven reduction and status/disappearance resolution. Forecast logic is unchanged and its label explicitly says it includes approvals.

The new actionable-change card compares the selected stock snapshot with the latest snapshot at or before seven days earlier. When available history is shorter, it uses the earliest earlier snapshot and explicitly labels the shorter elapsed window. With no earlier snapshot it returns blank. The caption includes the reference timestamp and elapsed days, including gaps longer than seven days. This stock comparison differs from the calendar-date movement period named Last 7 days.

The movement chart groups flow measures by SnapshotDate; the stock trend retains SnapshotDateTime. Existing flow predicates use the latest timestamp within each date. Because period boundaries are midnight-to-midnight, all snapshots of a date belong to the same period, preserving daily flow totals. The custom-date slicer remains visible with a conditional ACTIVE/INACTIVE title and color; it affects calculations only in Custom period. Executive defaults to All history in both reports.

Tests[DisciplinePair] now combines trimmed uppercase DisciplineA/B in alphabetical order in Power Query. Blank component values retain the existing pair label. The dimension's existing relationships propagate the normalized grouping to report slicers and charts; CSV values and directional A/B columns remain unchanged. Quality banner wording explicitly refers to quality alerts, distinct from clashes whose Navisworks status is Reviewed.

Current validation and the required Desktop checks are recorded in [the review report](executive-review-2026-09-28.md). Power BI Desktop was not opened for this update; DAX and Power Query runtime evaluation remain to be checked there.
