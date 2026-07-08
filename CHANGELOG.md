# Changelog

All notable changes to Mycelium are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

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
