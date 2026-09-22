# HUMAIN Power BI reports

Open `HUMAIN Dashboard.pbip` for Dark or `HUMAIN Dashboard - Light.pbip` for Light. Both share `HUMAIN.SemanticModel` and include the current 3D Clash Explorer.

## Setup

1. Generate CSV exports with Tracker v1.6. Default location: `%LOCALAPPDATA%\HUMAIN.CoordinationTracker\PowerBI`.
2. Install the Speckle Power BI connector and open a PBIP in Power BI Desktop.
3. In Transform data > Edit parameters, set **SourceFolder** to your CSV folder and **SpeckleModelUrl** to your Speckle model URL. Repository values are placeholders.
4. Authenticate the connector and the Speckle visual with your own account. Apply changes and Refresh.
5. Keep both report folders and the semantic model together. Refresh after updating Tracker exports and the corresponding Speckle model version.

The shared model now imports Speckle data, so opening this complete version requires the connector. The earlier six-page version remains in Git history before this dashboard synchronization.

## Pages

1. Executive Overview
2. Clash Test Performance
3. Critical & Aging
4. Clash Details
5. Resolution & Approvals
6. Snapshot Quality
7. 3D Clash Explorer

The 3D register shows only clashes matching the slicers. Select one row to show its uniquely matched A/B elements; multiple selection is not supported by the 3D measures. Source matching uses normalized model filename plus Element ID. Missing or ambiguous matches are excluded. Model-version alignment remains the user's responsibility.

Both local Speckle resources include the documented camera timing compatibility patch for direct clash-to-clash transitions. Re-test it after updating the custom visual. See [3D definitions and patch notes](extensions/README.md) and [matching rules](../docs/item-path-and-3d.md).

## Reporting definitions

Read the [Arabic card and meeting guide](../docs/dashboard-guide.ar.md) and [period/quality specification](../docs/reporting-periods-and-quality.md). OperationalProgress is authoritative for actionable metrics. Legacy DashboardKPI/OpenClashes and DailyProgress/Current include Approved among unresolved records.

## Distribution and privacy

This repository contains report layouts, Power Query, relationships, DAX, themes and embedded visual resources. It excludes `.pbi` caches, original XML, CSV datasets, snapshot/review stores and compiled binaries. Speckle visual `storedData` account state is removed from distribution definitions; recipients authenticate locally. The working reports retain their own local authentication.
