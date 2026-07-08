<p align="center">
  <img src="docs/images/logo.png" alt="Mycelium logo" width="160"/>
</p>

<h1 align="center">Mycelium</h1>

<p align="center">
  Generative urban massing for <a href="https://www.grasshopper3d.com/">Grasshopper</a> (Rhino 8).<br/>
  Subdivide a parcel into blocks and streets, grow building typologies, allocate parks and trees, and generate terrain.
</p>

<p align="center">
  <a href="https://github.com/SustainableUrbanSystemsLab/Mycelium/actions/workflows/ci-build.yml"><img src="https://github.com/SustainableUrbanSystemsLab/Mycelium/actions/workflows/ci-build.yml/badge.svg" alt="Build"/></a>
  <a href="https://github.com/SustainableUrbanSystemsLab/Mycelium/releases"><img src="https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fyak.rhino3d.com%2Fpackages%2FMycelium&query=%24.version&label=version&color=blue" alt="Version"/></a>
  <a href="https://rhinopackages.github.io/?search=Mycelium"><img src="https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fyak.rhino3d.com%2Fpackages%2FMycelium&query=%24.version&suffix=%20&logo=Rhinoceros&label=Yak" alt="Yak"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-blue.svg" alt="License"/></a>
  <img src="https://img.shields.io/badge/Rhino-8-black.svg" alt="Rhino 8"/>
</p>

---

![Sample massing output](docs/images/sample-1.jpeg)

## What it does

Mycelium takes a closed parcel boundary curve and generates complete urban massing alternatives:

1. **Subdivision** — recursive binary space partitioning splits the parcel into building blocks separated by streets.
2. **Typologies** — each block receives a randomly selected building type from the configurations you allow: courtyard (perimeter block), linear bar, point block, L-shape, U-shape, or tall tower.
3. **Open space** — a chosen number of blocks become parks, populated with procedural trees; courtyards can receive trees too.
4. **Metrics** — GFA, GIA, NIA, FAR, and unit-count estimates for every generated alternative.

Every output is driven by a random seed, so alternatives are fully reproducible.

## Installation

### Via the Package Manager (recommended)

1. In Rhino 8, run the `_PackageManager` command.
2. Search for **mycelium**.
3. Install and restart Rhino.

Components appear in Grasshopper under the **Mycelium** tab.

### Manual

Download `Mycelium.gha` from the [latest release](https://github.com/SustainableUrbanSystemsLab/Mycelium/releases), unblock it, and place it in your Grasshopper libraries folder (`_GrasshopperFolders` > Components).

## Components

| Component | Tab / Panel | Purpose |
|---|---|---|
| **Massing Generator** | Mycelium / Massing | Main generator: parcel in, city block out |
| **Courtyard Config** | Mycelium / Building Types | Allow perimeter blocks with central courtyards |
| **Linear Config** | Mycelium / Building Types | Allow bar buildings along the block's long axis |
| **Point Config** | Mycelium / Building Types | Allow compact point blocks |
| **L-Shape Config** | Mycelium / Building Types | Allow L-shaped buildings |
| **U-Shape Config** | Mycelium / Building Types | Allow U-shaped buildings |
| **Tall Building Config** | Mycelium / Building Types | Allow towers |
| **Tree Config** | Mycelium / Vegetation | Tree density, size, and courtyard placement |
| **Terrain Generator** | Mycelium / Site | Procedural terrain from OpenSimplex noise ([docs](docs/terrain-generator.md)) |
| **Mycelium Templates** | Mycelium / Utilities | Browse and insert example definitions synced from [Mycelium-Templates](https://github.com/SustainableUrbanSystemsLab/Mycelium-Templates) |

<details>
<summary><b>Massing Generator inputs &amp; outputs</b></summary>

**Inputs**: Boundary (closed planar curve), FloorHeight, Divisions (subdivision depth), StreetWidth, BuildingConfigs (from the config components), NumParks, GenerateFloorSlabs, Trees (from Tree Config), Seed.

**Outputs**: Footprints, Masses, Heights, Streets, FloorSlabs, Parks, Courtyards, Trees, Parcels, Metrics.

Each Building Type Config component exposes: floor range, corner radius, minimum footprint area, setback range, and building depth range. Feed any combination of configs into the generator — each block picks one at random.

</details>

## Quick start

Drop a **Mycelium Templates** component on the canvas and click **Select Template** — it lists every template from the [Mycelium-Templates](https://github.com/SustainableUrbanSystemsLab/Mycelium-Templates) repository, synced from the branch matching this plugin's version (downloaded and cached on first use). Click one to insert a working example graph next to the component.

![Algorithm overview](docs/images/algorithm.jpeg)

## Development

<details>
<summary><b>Building from source</b></summary>

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (8.0 or newer).

```bash
git clone https://github.com/SustainableUrbanSystemsLab/Mycelium.git
cd Mycelium
dotnet build Mycelium.sln -c Release
# → src/Mycelium/bin/Release/net7.0-windows/Mycelium.gha
```

The project targets `net7.0-windows` and builds on Windows, macOS, and Linux (`EnableWindowsTargeting`). The Grasshopper NuGet package ships .NET Framework reference assemblies; Rhino 8 supplies the real .NET 7 assemblies at run time, so the NU1701 restore warning is suppressed intentionally.

</details>

<details>
<summary><b>Creating a Yak package locally</b></summary>

```bash
scripts/package.sh
```

This builds the solution, assembles `dist/`, and produces the `.yak` package using the yak CLI from a local Rhino 8 installation. The package version comes from the root `manifest.yml`. CI runs the same staging logic (see below) as a dry-run on every push and PR to `dev`.

</details>

<details>
<summary><b>Releasing to the Package Manager</b></summary>

Publishing is fully automated through branch-triggered workflows (requires the `YAK_TOKEN_PATRICK` repository secret):

| Branch push | Workflow | Result on the Yak server |
|---|---|---|
| `pre-release` | *Yak Pre-Release* | `X.Y.Z-beta.W` (visible with "include pre-releases") |
| `release` | *Yak Release* | `X.Y.Z.W` (public) |

1. Bump `version:` in `manifest.yml` (4-part `X.Y.Z.W`) and update `CHANGELOG.md` on `dev`.
2. Merge / fast-forward `dev` into `pre-release` and push — CI builds Windows + Mac distributions and publishes the beta.
3. When the beta checks out, push the same state to `release` for the public version.

Pushes are idempotent (already-published distributions are skipped), and every run verifies the version is searchable on the server afterwards. To hide a bad version, run the *Yak Yank* workflow from the Actions tab.

</details>

<details>
<summary><b>Repository layout</b></summary>

```
├── manifest.yml            # Yak package manifest (version source of truth)
├── src/Mycelium/           # Plugin source (C#, .gha)
│   ├── Components/         # Grasshopper components
│   ├── Core/               # Geometry + noise logic (no GH dependencies)
│   └── Icons/              # 24x24 component icons
├── docs/                   # Documentation and images
├── scripts/package.sh      # Local Yak packaging
└── .github/workflows/      # CI, packaging dry-run, Yak release channels
```

Example `.gh`/`.ghx` definitions live in the separate [Mycelium-Templates](https://github.com/SustainableUrbanSystemsLab/Mycelium-Templates) repository, branched per plugin version.

</details>

## Authors

- **Dr. Ilker Karadag** ([@karadagi](https://github.com/karadagi)) — Associate Professor of Architecture, Sakarya University. Original author.
- **Dr. Patrick Kastner** ([@kastnerp](https://github.com/kastnerp)) — Assistant Professor, School of Architecture, Georgia Institute of Technology; [Sustainable Urban Systems Lab](https://github.com/SustainableUrbanSystemsLab).

## License

[Apache-2.0](LICENSE)
