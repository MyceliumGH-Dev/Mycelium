# Grasshopper usage

1. Open Rhino and Grasshopper.
2. Create a new definition and draw a closed planar boundary curve in Rhino.
3. Reference the boundary into a Grasshopper `Curve` parameter.
4. Add a `GhPython` component.
5. Copy the contents of `gh_parcel_gen.py` into the GhPython editor.
6. Connect:
   - `Boundary` → your parcel curve
   - other numeric/text inputs as indicated at the top of the script.

The component will output:
- `Footprints` (plan curves),
- `Masses` (extruded Breps),
- `Heights`,
- `Metrics` (text).
