# Top Level — management view

Added 28 September 2026 to the Light and Dark reports, alongside Executive Overview. The 1600 × 900 page is intended for presentations and readers who do not work in BIM.

## Reading the page

“Outstanding clashes” means clashes still requiring action. Accepted and resolved records are excluded. The page always covers the whole project from its first tracked snapshot to its latest export. It has no slicers. Its headline measures deliberately ignore period and discipline filters; the trend retains its snapshot axis. This prevents the starting amount and the reported reduction from changing meaning when another report page is filtered.

| Display | Definition | Current example |
|---|---|---:|
| Initial Clashes | Actionable work in the first tracked snapshot | 35,697 |
| Clashes Requiring Action | Actionable work in the latest snapshot | 24,189 |
| Net Reduction | Starting open minus current open | 11,508 |
| Reduction % | Reduction divided by starting open | 11,508 / 35,697 = 32.2% |
| + New & Returning Clashes | New actionable work plus work returned to action since tracking began | 18,317 |
| − Recorded Resolutions | Resolution transitions from actionable work, including disappeared records | 23,136 |
| − Accepted Clashes | Actionable work moved to Approved | 6,689 |

The balance is displayed explicitly:

`35,697 + 18,317 − 23,136 − 6,689 = 24,189`

Movement counts are recorded transitions, not necessarily unique clashes: the same clash can return to action and subsequently be resolved again. First-snapshot records are the starting stock, not new movement. If the balance does not reconcile, its caption asks for review rather than showing a false equality.

**32.2% is a reduction in the open work list, not project completion or verified engineering progress.** Acceptance contributes to it. “Recorded as resolved” includes records absent from the next export, following the existing project rule. The page explains both limitations visibly.

When open work exceeds its starting value, the change/rate titles switch to “Net Increase” and “Increase %,” with warning color. The amount and percentage show the magnitude, with direction stated by their titles. With zero starting work the percentage is blank and its caption explains why. The recent-change card is omitted from Top Level. The full-width outstanding-clash trend shows changes over time; the detailed comparison remains in Executive Overview.

All percentage inputs appear on the page, counts use full numbers, and there is only one percentage. The chart cannot filter the headline cards. Data-review alerts produce a provisional-progress note. Otherwise the footer explains the disappearance rule and the distinction from construction completion.

## Implementation and validation

- Twenty-three additive measures in the shared model, under display folder `Top Level`; existing measure expressions are not changed. See [exact DAX](top-level-measures.dax).
- Twenty-two visuals in each report's `definition/pages/TopLevel`, registered after Executive Overview.
- Live Desktop engine evaluated all 23 measures as Ready without errors. The current balance, percentage and recent comparison were queried and reconciled.
- A live query with Custom period, September 13 and Critical severity filters still returned the intended whole-project values: 35,697, 24,189 and 32.238003…%.
- Static parsing, field bindings, canvas bounds, non-overlap and theme-normalized Dark/Light parity are checked by `node tools/validate-top-level.mjs`.
- Both Light and Dark pages were opened and inspected in Desktop; clipped captions were shortened and the heading height increased. The shared on-disk expressions were compared with the validated live definitions after saving.

The examples are from the locally available September 28 exports; they are not constants embedded in DAX. No Tracker or CSV changes are required.

Display-label update: the management page uses Clash/Clashes, with plain-language captions. Recorded Resolutions retains the distinction between recorded resolution and independently verified completion. Only display strings changed; numeric calculations are unchanged.

Layout update, 28 September 2026: removed the recent-change card and its caption; expanded the trend to the full content width. Existing measures are retained for compatibility. Static validation was rerun; the revised layout still needs a Desktop visual check.
