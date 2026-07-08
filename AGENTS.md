# Agent Notes — Mycelium

Instructions for AI agents (and humans) working on this repository.

## Repository basics

- Default/development branch: `dev`. Releases flow `dev` → `pre-release` → `release` (branch pushes trigger the Yak publish workflows).
- `manifest.yml` at the repo root is the **single source of truth for the version** (4-part `X.Y.Z.W`). CI stamps it into the package and into the assembly (`AssemblyVersion` = full 4-part).
- Component GUIDs are load-bearing: existing Grasshopper files reference them. **Never change a `ComponentGuid`.**
- The Yak package name is registered as lowercase `mycelium` on the server (casing locked at first-ever push; McNeel contact required to change it). Server lookups are case-insensitive.

## ⚠️ IMPORTANT: keep Mycelium-Templates in sync with every release

The **Mycelium Templates** component syncs example definitions from
[`SustainableUrbanSystemsLab/Mycelium-Templates`](https://github.com/SustainableUrbanSystemsLab/Mycelium-Templates),
using a **branch named exactly after the plugin's 4-part version** (it reads
`AssemblyVersion` at runtime, e.g. `0.1.0.1`). If the branch is missing, the
component falls back to `main` — users then silently get development templates
instead of the ones matching their installed version.

**Release checklist (do not publish without this):**

1. Bump `version:` in `manifest.yml` and update `CHANGELOG.md` on `dev`.
2. In the **Mycelium-Templates** repo, create a branch named after the new
   version from its `main` tip:
   `git push origin main:<X.Y.Z.W>` (e.g. `git push origin main:0.1.0.2`).
3. Fast-forward `dev` → `pre-release` (publishes the beta), then → `release`
   (publishes the public version).

The templates repo's `main` tracks development; version branches are frozen
snapshots matching each release.

## Build & packaging

- `dotnet build Mycelium.sln -c Release` builds on any OS (`EnableWindowsTargeting`); NU1701 is suppressed intentionally (Grasshopper NuGet ships net48 ref assemblies, Rhino 8 provides .NET 7 at runtime).
- `scripts/package.sh` mirrors the CI staging logic locally.
- No bundled templates ship in the package — do not add `.gh`/`.ghx` files to the plugin repo.
