# Reporting periods and snapshot quality

Open **HUMAIN Dashboard.pbip**. Refresh loads the Tracker CSV exports from the configured SourceFolder.

Executive Overview, Clash Test Performance, Resolution & Approvals and Snapshot Quality now offer All history, Today, Last 7 days and Custom period. The date range is used only for Custom period. Period choices are local to each page.

Movement cards sum observed non-baseline transitions whose snapshot timestamp falls within the selected calendar dates. A transition is attributed to the snapshot where it was first observed; its exact real-world completion time is unknown. Current stock cards use the latest snapshot before the end of the period, even if that snapshot predates its start. The stock date is always displayed. A period with no exports shows zero observed movement and a notice explaining that stock is last known.

Opening actionable + new + returned - actionable resolved - actionable approved = closing actionable. Resolution rate is actionable resolved divided by opening actionable plus new and returned work. Resolved - All also includes resolution of previously approved clashes, so it can exceed the actionable resolved component used in the balance.

Critical & Aging and Clash Details continue to show current-export records only.

SnapshotQuality.csv is generated automatically by the Tracker. Alerts identify tests absent from the next export, added or renamed tests, blank/unrecognized statuses and per-test drops of at least 30% AND 100 records. A test listed in the export with zero clashes differs from an absent test. Baseline tests are not treated as newly added tests. Counts refer to test/snapshot occurrences, not unique tests.

Alerts are review signals, not proof of a bad export. Compact can explain a drop. No rule triggered does not guarantee complete exports. Alerts do not change resolution counts. Forecasts are withheld when the trailing seven-day input contains quality alerts, and otherwise still require at least three observed dates and positive net burn.

Validated: 204 workflow assertions and 11 quality assertions; all available non-baseline test/interval balances; Desktop refresh, all-history totals, Today without an export, custom date range boundaries and the quality register. Original raw snapshots were unchanged by the initial rebuild.
