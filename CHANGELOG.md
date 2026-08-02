# Changelog

All notable changes to Mycelium are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0.4] - 2026-08-02

### Added
- Urban morphology indicators from the Massing Generator: plan area density (`lambda_p`), open-space and park ratios, direction-dependent gross frontal area density (`lambda_f`), and building-height mean, standard deviation, minimum, median, 90th percentile, and maximum.
- Optional `AnalysisDirection` input for directional frontal-area calculations. The vector is normalized in the XY plane and falls back to world X when it is zero or vertical.
- Schema-versioned `CaseManifest` JSON output containing a deterministic SHA-256 case ID, random seed, installed plug-in version, model units, network family and subtype, effective generator inputs, geometry counts, development metrics, and morphology metrics.
- Published JSON Schema for the case manifest at `docs/case-manifest.schema.json`.
- `CITATION.cff` metadata for GitHub and Zenodo software citations.
- Dataset-export example in Mycelium-Templates, with morphology metrics and JSON manifest outputs wired to panels for inspection and file streaming.

### Changed
- Massing Generator retains its existing GUID and parameter order; the analysis-direction input and new outputs are appended for compatibility with existing Grasshopper definitions.
- Repository metadata now uses the canonical `MyceliumGH-Dev` organization URL after the GitHub organization rename.

## [0.1.0.3] - 2026-08-01

### Added
- Radial-Concentric Grid subtypes in the Massing Generator: full circular `Civic Core`, straight-sided `Polygonal Radial`, and a one-sided `Fan Plan` with a less-permeable rear sector.
- Diagonal Grid subtypes in the Massing Generator: `Single Axis`, intersecting `Cross Axes`, and an `Orthogonal Overlay` that cuts a wider diagonal boulevard through a regular grid.
- Irregular Grid subtypes in the Massing Generator: the backwards-compatible `Recursive Orthogonal`, seeded `Deformed Grid`, and offset-row `Staggered Grid` with T-junctions.
- Orthogonal Grid subtypes in the Massing Generator's nested right-click menu: `Regular Grid`, elongated `Rectangular Grid`, chamfered `Cerdà Grid`, and `Hierarchical Superblock` with wider primary streets around 3×3 groups of local blocks.
- Massing Generator right-click street-network selector with `Irregular Grid` (the backwards-compatible default), `Orthogonal Grid`, `Diagonal Grid`, and `Radial–Concentric Grid` modes. The selection is saved with the Grasshopper definition.
- Every published version now gets a git tag and a GitHub Release, cut by CI *after* Yak accepts the push, with the changelog section as its notes and both `.yak` distributions attached. Yak is still the only install channel; the tag exists so a version has an immutable ref pointing at the commit it was built from. Pre-releases are tagged too, marked as GitHub pre-releases.
- Re-release guard in the packaging workflow. A publish fails if the version's tag already exists on a different commit, or if Yak already holds both distributions — the "forgot to bump `manifest.yml`" case. On the `dev` dry-run it is a warning, not a failure, and a tag on the *same* commit (a re-run) and a partially-published version (the deliberate mac backfill) both stay allowed.
- `.github/dependabot.yml`, weekly on the github-actions ecosystem. NuGet is deliberately not enabled: the Grasshopper package pins the Rhino 8 ABI, so a bump there is a runtime change, not a build-tool one.

### Changed
- Radial–Concentric Grid now terminates its spokes at a finite central civic/focal block and surrounding ring street. Spoke density is limited by available inner-ring frontage, avoiding unrealistic needle-shaped parcels at the center.
- GitHub Actions are pinned to full commit SHAs instead of the floating `@v4` tags, so a re-pointed release tag cannot silently change what CI runs. The trailing `# vX.Y.Z` comment records the human-readable version.

### Fixed
- Updating Mycelium Templates now clears downloaded template files before synchronizing, so changed `.gh`/`.ghx` content is fetched automatically. The redundant **Force Refresh Template List** command has been removed.

## [0.1.0.2] - 2026-07-30

### Changed
- Redrawn component icon set: all eleven 24x24 PNGs replaced with an isometric set built from a shared engine (30 degree projection, uniform 1.25 stroke, family accents for built/plant/ground/tool, one motif plus at most one badge per glyph). File names and the `ComponentIcons.Get` keys are unchanged, so nothing in the components had to move.
- Plugin logo redrawn from the new assembly mark. Strokes are damped to 0.67 of the glyph value so the 1.25 stroke, scaled 14x to 512px, does not fill in the massing volumes.

### Added
- `design/icons/` — the vector source the PNGs come from: per-glyph SVGs, a `<symbol>` sprite sheet, the `myc-vec.js` generator, and `manifest.csv` mapping every glyph to its component, ribbon panel, family, and accent.
- `Mycelium Icon Spec.dc.html` — contact sheet for reviewing the set; renders from the generator rather than from the committed SVGs.

## [0.1.0.1] - 2026-07-09

### Changed
- Yak package name capitalized `mycelium` → `Mycelium` (server lookups are case-insensitive, so this is cosmetic — matches the manifest/repo casing everywhere, Eddy3D-style).
- Template component now syncs from a branch matching the running assembly version (was hardcoded to `main`); `quick_start.ghx` moved out of the plugin into [Mycelium-Templates](https://github.com/SustainableUrbanSystemsLab/Mycelium-Templates), no bundled templates ship anymore.

### Removed
- GitHub Release / Pre-Release workflows (tagged zip releases on GitHub). Yak remains the only distribution channel.

## [0.1.0.0] - 2026-07-08

First release under the **Mycelium** name (previously *MetaForm*). Existing
Grasshopper files keep working: all component GUIDs are unchanged.

### Added
- Yak packaging (`scripts/package.sh`, `src/Mycelium/manifest.yml`) and GitHub Actions CI that builds the `.yak` on every push and attaches it to releases on `v*` tags.
- New logo and a complete 24x24 icon set in the mycelium-network style; the Terrain Generator finally has an icon.
- Trees can now actually be generated inside courtyards: the Tree Config `GenerateInCourtyards` flag was previously parsed but ignored.
- `CHANGELOG.md`, `.editorconfig`, and a comprehensive `.gitignore`.

### Changed
- Plugin renamed MetaForm → Mycelium; Grasshopper tab is now **Mycelium** with panels *Massing*, *Building Types*, *Vegetation*, *Site*, and *Utilities* (the Terrain Generator previously sat in a stray "FormFlux" tab).
- Retargeted from .NET Framework 4.8 / Rhino 7 to .NET 7 / Rhino 8.
- Repository restructured: plugin source under `src/Mycelium/`, docs and compressed images under `docs/`, legacy Python/GhPython prototypes removed, build artifacts un-tracked.
- Component config wire format now serializes culture-invariantly, fixing broken configs on systems with comma decimal separators.
- Icons load from embedded PNG resources; the resx/Resources.Designer indirection is gone.

### Fixed
- Assembly info GUID no longer collides with the Massing Generator component GUID.
- Config component base class no longer returns a fresh random `ComponentGuid` on every call.
- Mojibake em-dashes in Terrain Generator parameter descriptions.
- Template component folder labels now use platform path separators (was Windows-only).
