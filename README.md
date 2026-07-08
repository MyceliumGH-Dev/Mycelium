<p align="center">
  <img src="docs/images/logo.png" alt="Mycelium logo" width="160"/>
</p>

<h1 align="center">Mycelium</h1>

<p align="center">
  Generative urban massing for <a href="https://www.grasshopper3d.com/">Grasshopper</a> (Rhino 8).<br/>
  Subdivide a parcel into blocks and streets, grow building typologies, allocate parks and trees, and generate terrain.
</p>

<p align="center">
  <a href="https://github.com/SustainableUrbanSystemsLab/Mycelium/actions/workflows/build.yml"><img src="https://github.com/SustainableUrbanSystemsLab/Mycelium/actions/workflows/build.yml/badge.svg" alt="Build"/></a>
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
| **Mycelium Templates** | Mycelium / Utilities | Insert bundled example definitions via right-click |

<details>
<summary><b>Massing Generator inputs &amp; outputs</b></summary>

**Inputs**: Boundary (closed planar curve), FloorHeight, Divisions (subdivision depth), StreetWidth, BuildingConfigs (from the config components), NumParks, GenerateFloorSlabs, Trees (from Tree Config), Seed.

**Outputs**: Footprints, Masses, Heights, Streets, FloorSlabs, Parks, Courtyards, Trees, Parcels, Metrics.

Each Building Type Config component exposes: floor range, corner radius, minimum footprint area, setback range, and building depth range. Feed any combination of configs into the generator — each block picks one at random.

</details>

## Quick start

Drop a **Mycelium Templates** component on the canvas, right-click it, and insert *quick_start* — a working example graph wired up for you.

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
<summary><b>Creating a Yak package</b></summary>

```bash
scripts/package.sh
```

This builds the solution, assembles `dist/`, and produces `mycelium-<version>-rh8_0-any.yak` using the yak CLI from a local Rhino 8 installation. CI does the same on every push and attaches the package to the GitHub release on version tags (`v*`).

To publish to the [Yak server](https://developer.rhino3d.com/guides/yak/):

```bash
cd dist
yak login
yak push mycelium-<version>-rh8_0-any.yak
```

</details>

<details>
<summary><b>Releasing a new version</b></summary>

1. Bump `<Version>` in `src/Mycelium/Mycelium.csproj` and update `CHANGELOG.md`.
2. Tag: `git tag v<version> && git push --tags`.
3. CI builds the `.yak` and attaches it to the GitHub release; push it to the Yak server manually.

</details>

<details>
<summary><b>Repository layout</b></summary>

```
├── src/Mycelium/           # Plugin source (C#, .gha)
│   ├── Components/         # Grasshopper components
│   ├── Core/               # Geometry + noise logic (no GH dependencies)
│   ├── Icons/              # 24x24 component icons
│   ├── Templates/          # .ghx templates shipped with the package
│   └── manifest.yml        # Yak package manifest
├── docs/                   # Documentation and images
├── scripts/package.sh      # Local Yak packaging
└── .github/workflows/      # CI: build + package + release
```

</details>

## Authors

- **Dr. Ilker Karadag** ([@karadagi](https://github.com/karadagi)) — Associate Professor of Architecture, Sakarya University. Original author.
- **Dr. Patrick Kastner** ([@kastnerp](https://github.com/kastnerp)) — Assistant Professor, School of Architecture, Georgia Institute of Technology; [Sustainable Urban Systems Lab](https://github.com/SustainableUrbanSystemsLab).

## License

[Apache-2.0](LICENSE)
