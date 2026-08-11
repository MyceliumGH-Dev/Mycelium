# Agent Notes — Mycelium

Instructions for AI agents (and humans) working on this repository.

## Repository basics

- Default/development branch: `dev`. Releases flow `dev` → `pre-release` → `release` (branch pushes trigger the Yak publish workflows).
- `manifest.yml` at the repo root is the **single source of truth for the version** (4-part `X.Y.Z.W`). CI stamps it into the package and into the assembly (`AssemblyVersion` = full 4-part).
- Component GUIDs are load-bearing: existing Grasshopper files reference them. **Never change a `ComponentGuid`.**
- The Yak package name is registered as lowercase `mycelium` on the server (casing locked at first-ever push; McNeel contact required to change it). Server lookups are case-insensitive.

## ⚠️ IMPORTANT: keep Mycelium-Templates in sync with every release

The **Mycelium Templates** component syncs example definitions from
[`MyceliumGH-Dev/Mycelium-Templates`](https://github.com/MyceliumGH-Dev/Mycelium-Templates),
using a **branch named exactly after the plugin's 4-part version** (it reads
`AssemblyVersion` at runtime, e.g. `0.1.0.1`). If the branch is missing, the
component falls back to `main` — users then silently get development templates
instead of the ones matching their installed version.

**Release checklist (do not publish without this):**

1. Bump `version:` in `manifest.yml` and update `CHANGELOG.md` on `dev`.
2. Fast-forward `dev` → `pre-release` (publishes the beta), then → `release`
   (publishes the public version).

Step 2 is no longer preceded by a manual branch push: `template-branch-sync.yml`
creates the matching branch in Mycelium-Templates on every push to `pre-release`
and `release`, branching from the newest existing version branch, and makes it
the repo default on a stable release. **It needs the
`MYCELIUM_TEMPLATE_RELEASE_TOKEN` secret** (a fine-grained PAT covering
Mycelium-Templates with Contents + Administration write).
Without it the workflow only warns — it never blocks a release — and the
branch must still be created by hand:
`git push origin main:<X.Y.Z.W>` in the templates repo.

The templates repo's `main` tracks development; version branches are frozen
snapshots matching each release.

## Keeping templates consistent with the components

Nothing at run time checks that a template's components still exist or still have
the ports the plug-in registers, so a stale `.ghx` fails silently on the user's
canvas. Two things guard it, both Rhino-free:

- `tools/TemplateSync.Cli` — reports drift, and repairs port labels and stale
  component GUIDs with `--fix`. Port **count** changes need a Grasshopper re-save;
  the tool reports and refuses those deliberately.
- `tests/Mycelium.Templates.Tests`, run by `template-integrity.yml` against the
  branch matching `manifest.yml` (falling back to `main`).

Both parse component definitions out of the C# source rather than reflecting over a
built `.gha`. **Consequence: renaming a parameter or reordering `pManager.Add*` calls
is a template-breaking change** — run the CLI in the same commit.

## Icons

- Shipped icons are 24×24 PNGs in `src/Mycelium/Icons/`, embedded by the csproj glob and
  resolved by `ComponentIcons.Get("<name>")`. A miss returns **null** and Grasshopper draws
  its default box — it never errors, so a typo is invisible in every build.
- The name is the wiring, and unlike Eddy3D it is an explicit string literal in the
  component. Never rename a glyph without changing that literal in the same commit.
- Every visible component needs a distinct icon, and the assembly mark must not share a
  silhouette with any component glyph.
- The drawing language (space, two stroke weights, projection, family accents from the
  brand palette) and the per-glyph brief live in
  [design/icons/BRIEF.md](design/icons/BRIEF.md); what was actually built, and how to
  regenerate it, is [design/icons/README.md](design/icons/README.md). The glyph↔component
  contract is [design/icons/manifest.csv](design/icons/manifest.csv) — every row has a
  glyph, every glyph has a row. Read the brief before drawing.
- The PNGs are generated, not hand-drawn: `design/icons/src/myc-vec.js` is the engine and
  `myc-vec-set.js` holds one `def()` per glyph. Change a glyph there and re-rasterise at
  24×24 — never draw large and downscale, the 1.25 stroke has to land on the pixel grid.
- The plugin logo (`docs/images/logo.svg`) wraps the `Mycelium` assembly mark in the cream
  rounded square. It damps the glyph's strokes to 0.67 so they land near 12px at 512 rather
  than the ~18px a straight 14× scale-up would give, which fills in the massing volumes.

## Build & packaging

- `dotnet build Mycelium.sln -c Release` builds on any OS (`EnableWindowsTargeting`); NU1701 is suppressed intentionally (Grasshopper NuGet ships net48 ref assemblies, Rhino 8 provides .NET 7 at runtime).
- `scripts/package.sh` mirrors the CI staging logic locally.
- No bundled templates ship in the package — do not add `.gh`/`.ghx` files to the plugin repo.
