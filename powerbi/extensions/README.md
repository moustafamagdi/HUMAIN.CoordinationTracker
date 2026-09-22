# Optional Speckle 3D model extension

`source-aware-3d.json` contains the DAX definitions used by the local 3D Clash Explorer. It is a model-extension specification, not a PBIP file or automatic installer. The six-page base reports remain usable without the Speckle connector. Local working reports additionally contain an authenticated Speckle visual; account state and imported model caches are not distributed here.

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
