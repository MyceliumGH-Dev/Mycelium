# Mycelium Icon System

Vector source and generator for the twelve Mycelium component icons.
Only the PNGs ship; everything in this directory is the source they come from.

```
design/icons/
  README.md                  this file
  manifest.csv               glyph, component, ribbon panel, family, accent, motif, badge — the contract
  svg/                       one standalone 24x24 SVG per glyph
  Mycelium_Icons_Vector.svg  sprite sheet, one <symbol id="myc-NAME"> per glyph
  src/myc-vec.js             the engine: projection, motif library M, badge set B, safe-area guard
  src/myc-vec-set.js         one def() per glyph, grouped by ribbon panel
../../Mycelium Icon Spec.dc.html   the contact sheet — open it in a browser; this is the review artefact
src/Mycelium/Icons/*.png     SHIPPED. 24x24 PNG, transparent, embedded by the csproj glob
```

## Regenerating

Both the SVG and the PNG come out of the same `V.glyph(def)` call, so the guard runs on
every glyph in both paths. Rasterise by drawing the 24x24 SVG into a 24x24 canvas — never
draw at 96 and downscale, the 1.25 stroke has to land on the pixel grid.

```js
MYC.all();                    // 12 glyphs, throws if anything leaves the safe area
MYC.all()[0].svg;             // standalone 24x24 SVG string
MYC.sprite();                 // <symbol> sheet
```

## The rules the engine enforces

| | |
| --- | --- |
| Space | 24 x 24 units, `viewBox="0 0 24 24"`, all art inside `0.7 .. 23.3`. `V.glyph` throws on any path outside the box; there is no exceptions list. |
| Stroke | `1.25` every contour, `0.85` interior hairlines only (floor plates, parcel divisions, contour lines, branch veins). Round caps, round joins. |
| Ink | pine `#29473A`. |
| Fill | White for built volume; `shadeA #E7EAE8` / `shadeB #C9D1CD` for the two visible side faces (pine over white at 12% / 26% — derived neutrals, not brand colours). The family accent fills what grows, flows, or is ground. |
| Tinting | A filled detail narrower than `3.5` units is stroked in its own colour, not ink. Hairlines drawn on top of an accent fill are cream, not ink. |
| Effects | None. |
| Projection | 30 degree isometric, viewer above-left, extrusion up: `x = ox + (u-v)*cos30`, `y = oy + (u+v)/2 - w`. A face is drawn when its plan normal satisfies `nu + nv > 0`. Plan views are reserved for footprint operations. |
| Composition | One motif from `M`, at most one badge from `B`. Badge centred at `(17, 17)`, `r 4.4`, on cream `#F1EDE1`. |

## Families

| Family | Accent | Applies to |
| --- | --- | --- |
| `built` | pine `#29473A` | building typologies, assembly mark |
| `plant` | sage `#7E9469` | trees, parks, vegetation |
| `ground` | soil `#6B4F35` | terrain, parcels, site |
| `tool` | slate `#5A6660` | templates, utilities, diagnostics |

## The vocabulary

Motifs in `M` — a new glyph should reach for one of these before anything else:

- `mass(pj, plan, h, opts)` — any plan polygon extruded, optional `hole` (courtyard),
  optional `plates` (floor hairlines at a height step). Seven of the eleven glyphs are this.
- `plate(pj, plan, thick, opts)` — a slab: ground, a card, a base.
- `field(pj, plan, thick, cuts, opts)` — a plate ruled into parcels.
- `terrain(pj, size, hf, depth, opts)` — a height-function surface with a skirt and contours.
- `canopy(pj, plan, opts)` — a tree standing on isometric ground.
- `stack(pj, plan, count, gap, thick, opts)` — versioned cards.
- `roots(strands, opts)` — mycelial filaments. Screen-space; assembly mark only.

Badges in `B`: `gear` (configure), `down` (fetch), `grid` (subdivide).

If a component cannot be said with one motif plus one badge, add a motif every other glyph
could also use — not a one-off drawing.

## Wiring

The resource name is named explicitly in the component and `ComponentIcons.Get` returns
null on a miss, so a drift is silent:

```csharp
protected override Bitmap Icon => ComponentIcons.Get("MyceliumCourtyard");
```

Keep the `Mycelium` prefix. Do not rename a glyph for tidiness; if a name must change,
change the string literal in the same commit. `manifest.csv` is the contract — every row
has a glyph, every glyph has a row.

## As built — deviations worth knowing

- **`shadeA` / `shadeB` are new.** The brief asks for "grey shaded faces" without naming
  them. They are pine over white at 12% and 26% so they stay inside the palette's hue.
- **Cream is also a hairline colour.** Ink hairlines vanish on a soil fill, so parcel
  divisions and terrain contours are drawn in cream `#F1EDE1` where they sit on an accent.
  Cream is still never used as a backdrop except behind a badge.
- **The U opens toward the viewer's lower-*left*-of-centre, not the far lower-right.** At
  the specified badge position `(17, 17)` an opening aimed at the lower right lands under
  the badge disc. The mouth is rotated just far enough to clear it and stay visible.
- **`spec.html` is `Mycelium Icon Spec.dc.html`** at the repo root, and it renders from the
  live engine rather than from committed SVGs — so it cannot drift from the source.
