# Changelog

All notable changes to Mycelium are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Massing Generator street-network modes: irregular `Rectilinear` (the backwards-compatible default), regular `Checkerboard`, diagonal/orthogonal `Hybrid`, and concentric `Radial`.
- Every published version now gets a git tag and a GitHub Release, cut by CI *after* Yak accepts the push, with the changelog section as its notes and both `.yak` distributions attached. Yak is still the only install channel; the tag exists so a version has an immutable ref pointing at the commit it was built from. Pre-releases are tagged too, marked as GitHub pre-releases.
- Re-release guard in the packaging workflow. A publish fails if the version's tag already exists on a different commit, or if Yak already holds both distributions — the "forgot to bump `manifest.yml`" case. On the `dev` dry-run it is a warning, not a failure, and a tag on the *same* commit (a re-run) and a partially-published version (the deliberate mac backfill) both stay allowed.
- `.github/dependabot.yml`, weekly on the github-actions ecosystem. NuGet is deliberately not enabled: the Grasshopper package pins the Rhino 8 ABI, so a bump there is a runtime change, not a build-tool one.

### Changed
- GitHub Actions are pinned to full commit SHAs instead of the floating `@v4` tags, so a re-pointed release tag cannot silently change what CI runs. The trailing `# vX.Y.Z` comment records the human-readable version.

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
