# Optional Speckle 3D model extension

`source-aware-3d.json` contains the DAX definitions used by the local 3D Clash Explorer. The complete PBIP reports now include this extension. The specification remains a reference for the calculated tables and measures. The Speckle connector is required; account state and imported model caches are not distributed.

## Prerequisites and imported tables

Install the Speckle Power BI connector, authenticate with your own account, and create a text parameter `SpeckleModelUrl` for your model URL. Import **Objects**:

```powerquery
let
    Source = Speckle.GetTables(SpeckleModelUrl, [TableLayout = "Simplified"]),
    Objects = Source{[Key = "objects"]}[Data]
in
    Objects
```

Import **SpeckleElements**:

```powerquery
let
    Source = Speckle.GetTables(SpeckleModelUrl, [TableLayout = "Simplified"]),
    Properties = Source{[Key = "expand-properties"]}[Data],
    Selected = Properties({"name", "properties.Element.Id", "properties.Source File", "properties.UniqueId", "properties.Element.Category"}, true)
in
    Selected
```

Both Object Key columns must be whole numbers. Element.Id, Source File, UniqueId and Model Info must be text. Add a hidden text calculated column `CurrentClashes[3D Clash Key]`:

```dax
CurrentClashes[TestName] & "|" & CurrentClashes[ClashGuid]
```

Create the specification's calculated columns, then calculated tables in listed order. Add the measures to Metrics with the specified format strings. Internal names containing `Candidates` are retained for compatibility; their current calculation requires source model + ID.

Create active, single-direction many-to-one relationships from `Clash3DSides[ClashKey]` and `Clash3DGeometry[ClashKey]` to `CurrentClashes[3D Clash Key]`. Do not add bidirectional filters into CurrentClashes or derive progress counts from geometry rows.

Bind the Speckle visual's Model Info to `[3D Model Info]`, Object Keys to `Clash3DGeometry[Object Key]`, and Color By to `Clash3DGeometry[Side]`. Enable zoom on filter. A register using CurrentClashes fields must filter the viewer and A/B detail measures. Disable the register's interaction with summary cards so they retain slicer scope. Authenticate the visual locally.

See [matching rules, usage, repair and validation](../../docs/item-path-and-3d.md).

## Local camera compatibility adjustment

The installed September 2026 Speckle visual requested camera fitting immediately after applying a new object filter. In the HUMAIN report, direct clash-to-clash transitions could leave the camera at the previous selection; clearing selection first avoided the problem.

`patch-speckle-camera.mjs` applies a narrowly scoped local adjustment to an existing visual resource in both report folders. It requests a render and defers the camera fit by two animation frames plus 120 ms. A revision guard rejects obsolete requests and disposed/replaced renderers. No model data, matching rules, tokens or network behavior are changed. Original visual resources are backed up under `work/speckle-camera-backup` before modification; the script rejects already-patched or unrecognized code.

Save and close the reports before running `node powerbi/extensions/patch-speckle-camera.mjs "C:\path\to\dashboard folder"`, then reopen. This is a local compatibility patch, not an upstream Speckle release; a future visual update may replace it. Re-test direct row changes after updating the visual. Validation covered direct transitions in Desktop and mocked latest-request/disposal checks.
