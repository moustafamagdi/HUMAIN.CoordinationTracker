# HUMAIN Power BI reports

Open `HUMAIN Dashboard.pbip` for Dark or `HUMAIN Dashboard - Light.pbip` for Light. Both share `HUMAIN.SemanticModel` and include the current 3D Clash Explorer.

## Setup

1. Generate CSV exports with Tracker v1.6. Default location: `%LOCALAPPDATA%\HUMAIN.CoordinationTracker\PowerBI`.
2. Install the Speckle Power BI connector and open a PBIP in Power BI Desktop.
3. In Transform data > Edit parameters, set **SourceFolder** to your CSV folder and **SpeckleModelUrl** to your Speckle model URL. Repository values are placeholders.
4. Authenticate the connector and the Speckle visual with your own account. Apply changes and Refresh.
5. Keep both report folders and the semantic model together. Refresh after updating Tracker exports and the corresponding Speckle model version.

The shared model now imports Speckle data, so opening this complete version requires the connector. The earlier six-page version remains in Git history before this dashboard synchronization.

If CSV cards work but 3D remains empty, check **SpeckleModelUrl** as well as SourceFolder: changing the CSV folder does not configure the model URL. After setting the real URL, refresh Objects and SpeckleElements (or Refresh the report), then select one clash in the 3D register. No-selection placeholders are expected. Local connection parameters and authentication should not be committed to the distribution copy.

## Pages

1. Executive Overview
2. Top Level
3. Clash Test Performance
4. Critical & Aging
5. Clash Details
6. Resolution & Approvals
7. Snapshot Quality
8. 3D Clash Explorer

**Top Level** is the presentation-sized management view: starting/current open work, explicit reduction arithmetic, added/resolved/accepted movements and the recent direction. Its whole-project scope is fixed so readers can reconcile the numbers directly. See the [page guide](../docs/top-level-page.md).

The 3D register shows only clashes matching the slicers. Select one row to show its uniquely matched A/B elements; multiple selection is not supported by the 3D measures. Source matching uses normalized model filename plus Element ID. Missing or ambiguous matches are excluded. Model-version alignment remains the user's responsibility.

Both local Speckle resources include the documented camera timing compatibility patch for direct clash-to-clash transitions. Re-test it after updating the custom visual. See [3D definitions and patch notes](extensions/README.md) and [matching rules](../docs/item-path-and-3d.md).

## Reporting definitions

Read the [Arabic card and meeting guide](../docs/dashboard-guide.ar.md) and [period/quality specification](../docs/reporting-periods-and-quality.md). OperationalProgress is authoritative for actionable metrics. Legacy DashboardKPI/OpenClashes and DailyProgress/Current include Approved among unresolved records.

## Executive review — September 28, 2026

The synchronized Dark/Light Executive layouts add net reduction excluding approvals, resolution-source captions and a seven-day actionable stock comparison. Forecast still includes approvals. Count cards use full numbers, Resolution Rate uses one decimal, flow bars group by date, and the Top 10 is a wider ranked table. Reviewed clashes and retained resolved records are shown on Resolution & Approvals. Severity colors are red/amber/blue; approved values use purple. Pair labels are normalized alphabetically in the Tests dimension during refresh, preserving A/B direction in source fields.

The Executive canvas is taller (1600 × 1330); use Fit to width when checking readability. The custom-date slicer shows whether it is active; it is not hidden. See [review results and Desktop checklist](../docs/executive-review-2026-09-28.md) and [exact added DAX](../docs/executive-review-new-measures.dax). This update has static/source and CSV validation, not Desktop runtime or visual validation.

## Distribution and privacy

This repository contains report layouts, Power Query, relationships, DAX, themes and embedded visual resources. It excludes `.pbi` caches, original XML, CSV datasets, snapshot/review stores and compiled binaries. Speckle visual `storedData` account state is removed from distribution definitions; recipients authenticate locally. The working reports retain their own local authentication.
