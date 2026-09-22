# HUMAIN Power BI reports

Open `HUMAIN Dashboard.pbip` for Dark or `HUMAIN Dashboard - Light.pbip` for Light. Both use `HUMAIN.SemanticModel`; report layouts are separate.

## Setup

1. Generate CSV exports with the tracker. Default location: `%LOCALAPPDATA%\HUMAIN.CoordinationTracker\PowerBI`.
2. Open either PBIP in Power BI Desktop with project/PBIR support enabled if required by your Desktop version.
3. In Transform data → Edit parameters, set **SourceFolder** to your actual absolute CSV folder. The repository default `C:\HUMAIN.CoordinationTracker\PowerBI` is a portable placeholder, not a bundled dataset.
4. Apply changes and Refresh. Save the project locally. Refresh again after each tracker export.

Keep both report folders and the semantic model together; relative dataset links depend on this structure. Initial opening requires your own CSVs because imported-data caches are intentionally excluded. Current-clash ages advance with tracker exports, not Refresh alone.

## Pages

1. Executive Overview
2. Clash Test Performance
3. Critical & Aging
4. Clash Details
5. Resolution & Approvals
6. Snapshot Quality

An optional [Speckle 3D extension specification](extensions/README.md) documents the additional model tables, measures, source-aware matching and viewer bindings used in the local 3D Clash Explorer. It requires the Speckle connector and local authentication; the base reports do not require it.

Read the [Arabic card and meeting guide](../docs/dashboard-guide.ar.md) and [period/quality specification](../docs/reporting-periods-and-quality.md).

The repository contains report definitions, Power Query, relationships, DAX and themes. It excludes `.pbi`, imported model caches, original XML snapshots, local CSV exports and compiled binaries. Dark and Light share business measures; their conditional net-reduction colors differ. Removed custom Help tooltips are not included; normal data tooltips remain.

OperationalProgress is authoritative for actionable metrics. Legacy DashboardKPI/OpenClashes and DailyProgress/Current include Approved among unresolved records and should not be substituted for the operational measures.
