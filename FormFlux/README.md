# Form Flux - Grasshopper Plugin

A C# Grasshopper plugin for generative parcel massing with multiple building typologies.

## Features

- **5 Building Types**: Courtyard, Linear, Point, L-Shape, U-Shape
- **Parcel Subdivision**: Recursive binary space partitioning
- **Park Generation**: Random park allocation with procedural trees
- **Comprehensive Metrics**: FAR, GFA, GIA, NIA, unit counts
- **Smart Fallbacks**: Automatic building type adaptation for small parcels

## Building the Project

### Prerequisites

- Visual Studio 2022
- .NET Framework 4.8 Developer Pack
- Rhino 7+ installed

### Build Steps

1. Open `FormFlux.csproj` in Visual Studio 2022
2. Restore NuGet packages (automatically done on build)
3. Build the solution (Ctrl+Shift+B)
4. The output will be `FormFlux.gha` in the `bin` folder

### Installation

Copy `FormFlux.gha` to:
```
%APPDATA%\Grasshopper\Libraries\
```

Or for all users:
```
%PROGRAMDATA%\Grasshopper\Libraries\
```

## Usage

1. Open Grasshopper
2. Find the component: **Urban Design > Massing > Form Flux**
3. Connect a closed, planar curve as the Boundary input
4. Adjust parameters as needed
5. Set BuildingTypes list (e.g., `courtyard`, `linear`, `point`, `l-shape`, `u-shape`)

### Input Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| Boundary | Curve | Required | Parcel boundary (closed, planar) |
| Setback | Number | 3.0 | Distance from edge to building (m) |
| BuildingDepth | Number | 12.0 | Building wing depth (m) |
| MinFootprintArea | Number | 100.0 | Minimum footprint area (m²) |
| Floors_min | Number | 3.0 | Minimum floors |
| Floors_max | Number | 6.0 | Maximum floors |
| FloorHeight | Number | 3.2 | Floor-to-floor height (m) |
| Divisions | Integer | 0 | Subdivision recursion depth |
| StreetWidth | Number | 5.0 | Width of streets (m) |
| BuildingTypes | List[Text] | ["courtyard"] | Allowed types |
| NumParks | Integer | 0 | Number of park parcels |
| GenerateFloorSlabs | Boolean | false | Generate floor slabs |
| Seed | Integer | 0 | Random seed |

### Output Parameters

| Parameter | Description |
|-----------|-------------|
| Footprints | Building footprint curves |
| Masses | Building mass Breps |
| Heights | Building heights (m) |
| Streets | Street geometry |
| FloorSlabs | Individual floor slabs |
| Parks | Park boundaries |
| Trees | Tree spheres |
| Parcels | Building parcel boundaries |
| Metrics | Area and unit statistics |

## Building Types

### Courtyard
Perimeter block with central courtyard. Automatically falls back to point block if parcel too small.

### Linear
Bar building along the longer axis of the parcel.

### Point
Compact tower in the center of the parcel.

### L-Shape
Two perpendicular wings forming an L. Random orientation.

### U-Shape  
Three wings forming a U with one open side. Falls back to linear if parcel too small.

## Custom Icon

The project includes a placeholder icon. To add your custom icon:

1. Create a 24x24 PNG image named `icon_24x24.png`
2. Place it in the `Resources` folder
3. Update the project file to embed it
4. Rebuild

## License

MIT License - feel free to modify and distribute.

## Version History

- **1.0.0** - Initial release
  - 5 building types
  - Park and tree generation
  - Comprehensive metrics
