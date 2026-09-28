# Executive Overview review implementation — 2026-09-28

Implemented on `improve/executive-overview-review`, based on `64daf88`, for review before merging. Power BI Desktop was not opened. No snapshot/CSV, Tracker source/export format, 3D Explorer definition or Speckle resource was changed.

## Files by review item

Paths below are relative to the repository. Report paths apply to both `powerbi/HUMAIN.Final.Report` and `powerbi/HUMAIN.Light.Report`.

| Items | Changes and files |
|---|---|
| 1–3 | `powerbi/HUMAIN.SemanticModel/model.bim`: additive net/split/7-day measures. `definition/pages/ExecutiveOverview/visuals/`: KPI/status bindings, `net_split`, `resolved_split`, `change_7d_note`, `forecast_scope`, forecast annotations. |
| 4 | `model.bim`: Tests Power Query normalization, same columns/relationships. `CriticalAging/visuals/ca_details/visual.json`: pair field uses Tests. Existing Tests-based slicers/charts inherit normalization, including the unchanged 3D slicer. |
| 5–6 | Executive KPI/status visuals: full count units; Resolution Rate precision 1. `model.bim`: its format string only changes to `0.0%`. |
| 7–8 | Executive `flows`: SnapshotDate categories; `toptests`: ranked table with wider names and height for ten rows. Burndown retains its timestamp axis and measure. |
| 9–10 | Executive `custom_dates`: conditional title/color. `model.bim`: additive Custom Period and Quality Alerts Banner measures. Existing quality-banner bindings updated in Executive, Test Performance, Resolution & Approvals and Snapshot Quality. |
| 11 | Executive `page.json` and positions; removed `status_5`, repurposed two stock slots. ResolutionAudit `page.json`, positions and new `ra_reviewed`/`ra_retained` cards; Approval Revoked already existed. |
| 12–13 | Executive approved card/flow colors, `severity` selectors and new `severity_note`. |
| Documentation | `docs/dashboard-guide.ar.md`, `docs/reporting-periods-and-quality.md`, `powerbi/README.md`, this report and `docs/executive-review-new-measures.dax`. |
| Validation | `tools/validate-executive.mjs`, `tools/check-executive-data.mjs`. |

Parity review also corrected two pre-existing Light-only differences: Test Performance scorecard now sorts by Actionable Open like Dark, and Snapshot Quality's empty filter placeholder was removed to match Dark. No active filter predicate was removed.

## Exact DAX and model review

All fourteen added measures, including caption and theme-color variants, are reproduced verbatim in [executive-review-new-measures.dax](executive-review-new-measures.dax).

Core reconciliation:

```dax
Net Reduction excl Approvals =
[Net Actionable Reduction] - [Actionable Approved]

Net Reduction Split Caption =
VAR engineering = [Actionable Resolved] - [New Actionable] - [Returned to Action] RETURN "Resolved-driven: " & FORMAT(engineering,"#,0;-#,0;0") & " | Approval-driven: " & FORMAT([Actionable Approved],"#,0")

Resolved Split Caption =
"By status: " & FORMAT([Resolved By Status],"#,0") & UNICHAR(10) & "By disappearance: " & FORMAT([Resolved Disappeared],"#,0")
```

Review `git diff 64daf88 -- powerbi/HUMAIN.SemanticModel/model.bim` before merge. Expected changes are exactly: fourteen new Metrics measures, Resolution Rate display format and the Tests partition normalization. Existing measure expressions, relationships, Tests columns and all other tables remain unchanged. The variable named `engineering` is the requested net component excluding approvals; it still includes disappearance and is not proof of engineering fixes.

## Validation performed

| Check | Result |
|---|---|
| Tracker Release build using installed .NET Framework MSBuild | Passed; existing ToolsVersion fallback warning. |
| Tracker `--self-test` | 280 assertions passed: workflow 209, quality 11, management 22, quality review 23, item-path repair 8, import feedback 7. |
| JSON parsing | 341 report/model/resource JSON documents parsed. |
| Visual field references | 484 column/measure bindings resolved against model definitions. |
| Existing measures | All 92 expressions unchanged; existing metadata unchanged except Resolution Rate format. |
| Dark/Light parity | Executive, Resolution, Critical & Aging, Test Performance and Snapshot Quality definitions equivalent after normalizing color values/theme measure variants and tab order. Executive/Resolution static coordinates within canvas with no overlaps. |
| New approved/severity colors | Calculated contrast against card backgrounds: Dark 5.61–9.65:1, Light 5.10–6.37:1. This is a numeric color check, not rendered visual verification. |
| Local OperationalProgress CSV | 925 non-baseline flow rows checked for net and resolution identities. |
| Date-grouped flow boundaries | 264 calendar boundary comparisons match direct timestamp sums. |
| Seven-day reference edge cases | Five synthetic cases pass: empty, single snapshot, short history, exact boundary and sparse history. |
| Pair aggregation | 20 directional groups become 16 alphabetical groups; actionable total remains 24,189. No reversed duplicates in normalized groups. |

The scripts reproduce arithmetic from exported values; they do not execute DAX or Power Query. JSON parsing and field checks do not prove that Power BI will accept every visual property or render the intended layout. No Desktop refresh, screenshots or visual verification were performed for this update.

Reproduce from repository root:

```powershell
& 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/MSBuild.exe' HUMAIN.CoordinationTracker/HUMAIN.CoordinationTracker.csproj /t:Build /p:Configuration=Release /verbosity:minimal /nologo
& ./HUMAIN.CoordinationTracker/bin/Release/HUMAIN.CoordinationTracker.exe --self-test
node tools/validate-executive.mjs
node tools/check-executive-data.mjs
```

The CSV checker defaults to `%LOCALAPPDATA%/HUMAIN.CoordinationTracker/PowerBI`, or accepts the folder as its first argument. Results below describe the locally available exports at review time, not fixed dashboard constants. Data files are not committed.

## Local data results

All history, without discipline/severity/test filters:

| Identity | Values |
|---|---:|
| Net excluding approvals + actionable approvals = net including approvals | 4,819 + 6,689 = 11,508 |
| Actionable resolved − new/returned inflow = net excluding approvals | 23,136 − 18,317 = 4,819 |
| By status + by disappearance = Resolved All | 1,573 + 22,853 = 24,426 |
| Latest actionable − reference actionable | 24,189 − 22,568 = **+1,621** |

Latest stock timestamp: **2026-09-28 13:06:05**. Seven-day reference: **2026-09-21 11:44:17**, approximately 7.06 elapsed days. These stock endpoints reproduce the upward movement in the underlying burndown data; the chart itself was not opened.

There are multiple September 17 snapshots, so a single unexplained “drop” would be misleading:

| Sep 17 snapshot | Actionable stock | Net reduction | Approval from actionable | Resolved All | By status | By disappearance |
|---|---:|---:|---:|---:|---:|---:|
| 09:00:13 | 30,280 | 68 | 155 | 784 | 0 | 784 |
| 09:51:41 | 24,918 | 5,362 | 5,362 | 0 | 0 | 0 |
| 11:53:31 | 26,590 | −1,672 | 0 | 0 | 0 | 0 |
| 13:34:17 | 24,159 | 2,431 | 48 | 2,607 | 0 | 2,607 |
| 15:25:43 | 24,100 | 59 | 0 | 400 | 95 | 305 |

September 17 resolution total: **3,791 = 95 by status + 3,696 by disappearance**. The largest actionable drop, at 09:51, is entirely approval-driven (5,362). The 13:34 resolution events are entirely disappearance-driven; inflow and approvals explain why net reduction differs from Resolved All.

## Implementation choices and follow-ups

- The requested short-history behavior contained both “blank if no seven-day reference” and an earliest-snapshot fallback. Implemented the explicitly labelled earliest earlier snapshot fallback. With only one snapshot the value is blank. The caption reports actual elapsed time, including longer gaps.
- Directional pairs are alphabetized centrally for grouping; DisciplineA/B remain unchanged. No Tracker parser/export change was needed.
- The Top 10 uses a ranked table instead of bars to provide space for names. Actual wrapping/ten-row visibility requires the Desktop check below.
- The date slicer remains visible and conditionally labelled/muted. No bookmarks or unsupported dynamic visibility mechanism were introduced.
- The Executive canvas is taller, 1600 × 1330, to accommodate captions and ten ranked rows. Forecast remains a single card and is smaller than before. Check presentation readability at the intended screen size.
- **Engineering-only forecast remains a follow-up.** Existing forecast semantics are unchanged. Excluding approvals alone would still leave disappearance-driven resolution, so its intended scope needs agreement before implementation.

## Required Power BI Desktop visual/runtime checks

1. Open each branch PBIP, configure the local SourceFolder if needed, and Refresh. Confirm all new measures and the Tests query evaluate without errors. Review the shared model diff before merging.
2. In All history with no test/discipline/severity filters, reconcile the numbers above against the current export. Check full count values (no K), one-decimal Resolution Rate and the resolved/net split captions.
3. Verify the +1,621 stock change against the burndown endpoints for these exports, red for increases and green for decreases. Check short/sparse history captions and blank with no earlier snapshot.
4. Switch All history, Today, Last 7 days and Custom period. Confirm only Custom uses the date range and the title changes. Check a no-export period and a day with multiple snapshots; flow totals must reconcile while the stock chart retains individual snapshots.
5. Check all ten ranked tests fit and names remain readable; inspect titles, caption wrapping, severity note and card alignment at Fit to width and your presentation scale, in both themes.
6. Verify Approved is purple, Critical red, Medium amber and Low blue. Check color readability and identical filters/layout between reports.
7. Confirm pair labels have no reversed duplicates and filtering selects both directions, including the existing 3D slicer/register. Directional A/B element information should remain intact.
8. Confirm Reviewed clashes and Resolved Retained appear on Resolution & Approvals, with one Approval Revoked card, and quality banners clearly refer to quality alerts.
