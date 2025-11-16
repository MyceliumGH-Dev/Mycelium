# Design Alternatives Generator  
*(Pure Python + Grasshopper, Apache-2.0)*

This toolkit generates **building massing alternatives** for a given **parcel boundary**, using controls similar to:

- **Building typology**: Point, Slab, L-shaped, U-shaped, O-shaped  
- **Targets**: GFA, FAR, average height / floors  
- **Derived**: footprint area range, Site Coverage Ratio (SCR)

The goal:

> **Parcel boundary + design targets → many geometry alternatives**  
> in both **pure Python** (batch) and **Grasshopper** (interactive).

---

## 1. Repository Structure

Suggested layout:

```text
.
├── README.md
├── LICENSE                 # Apache-2.0
├── python/
│   ├── requirements.txt
│   ├── parcel_targets.py   # GFA/FAR/floors/SCR calculations
│   ├── generator.py        # geometry generation per typology
│   ├── demo_parcel.py      # simple example script
│   └── utils.py            # plotting / export helpers (optional)
└── grasshopper/
    ├── gh_parcel_gen.py    # GhPython script (same logic as python/)
    └── README_GRASSHOPPER.md
```

You can rename/reorganize as needed; just keep Python and GhPython APIs in sync.

---

## 2. Design Targets & Formulas

For a **single parcel** we use:

- `A_parcel` – parcel area [m²]  
- `GFA` – gross floor area [m²]  
- `FAR` – floor area ratio `FAR = GFA / A_parcel` [-]  
- `N_floors` – average number of floors (range)  
- `H_avg` – average building height (range, optional check)  
- `A_fp` – total building footprint area [m²]  
- `SCR` – site coverage ratio `SCR = A_fp / A_parcel` [-]

Basic relationships:

```text
GFA      = A_fp * N_floors
FAR      = GFA / A_parcel
SCR      = A_fp / A_parcel
A_fp     = GFA / N_floors
N_floors = GFA / A_fp
```

From UI-like inputs we:

1. **Given** `A_parcel` and `FAR` → `GFA = FAR * A_parcel`.  
2. **Given** `GFA` and a range `[N_min, N_max]` → compute footprint range:

   ```text
   A_fp_max = GFA / N_min
   A_fp_min = GFA / N_max
   ```

3. Convert to **SCR range**:

   ```text
   SCR_min = A_fp_min / A_parcel
   SCR_max = A_fp_max / A_parcel
   ```

This reproduces the “Footprint area” and “SCR” sliders at the bottom of the parcel panel.

---

## 3. Building Typologies

We support five schematic massing patterns per parcel:

- `Point` – one or more compact towers (nearly square footprints)  
- `Slab` – bar buildings (elongated rectangles)  
- `L_shaped` – L-footprints (two bars joined)  
- `U_shaped` – U-footprints (three bars around a courtyard)  
- `O_shaped` – perimeter / donut block (ring with inner courtyard)

These types only affect **how footprint area is distributed and shaped**. All obey the same parcel-level targets.

---

## 4. Pure Python API

### 4.1. Install

```bash
cd python
pip install -r requirements.txt
```

Example `requirements.txt`:

```txt
shapely
matplotlib
```

### 4.2. Targets helper (`parcel_targets.py`)

`ParcelTargets` encapsulates parcel-level relationships:

```python
from parcel_targets import ParcelTargets

targets = ParcelTargets(
    parcel_area=120.0 * 80.0,
    gfa=88756.0,
    far=3.0,
    floors_min=5.0,
    floors_max=12.0,
)

print(targets.footprint_range())  # (A_fp_min, A_fp_max)
print(targets.scr_range())        # (SCR_min, SCR_max)
```

### 4.3. Geometry generator (`generator.py`)

Key entry point:

```python
from shapely.geometry import Polygon
from parcel_targets import ParcelTargets
from generator import generate_alternative, Typology

parcel = Polygon([(0, 0), (120, 0), (120, 80), (0, 80)])

targets = ParcelTargets(
    parcel_area=parcel.area,
    gfa=88756.0,
    far=3.0,
    floors_min=5.0,
    floors_max=12.0,
)

alt = generate_alternative(
    site_poly=parcel,
    typology=Typology.POINT,
    targets=targets,
    n_buildings=1,
    seed=0,
)

print(alt["metrics"])
```

`generate_alternative` returns a dictionary with:

- `footprints`: list of Shapely polygons  
- `heights`: list of heights [m] (one per footprint)  
- `metrics`: dict (`GFA`, `FAR`, `SCR`, etc.)

See `demo_parcel.py` for a working example, including plotting.

---

## 5. Grasshopper Workflow

The **Grasshopper** script (`grasshopper/gh_parcel_gen.py`) exposes the same controls inside a GhPython component.

**Inputs (GhPython component):**

- `Boundary` (Curve) – closed, planar parcel boundary in World XY  
- `Typology` (Text) – `"Point"`, `"Slab"`, `"L"`, `"U"`, `"O"`  
- `GFA` (Number) – target GFA [m²]  
- `FAR` (Number) – target FAR [-] (optional; used as a check)  
- `Floors_min`, `Floors_max` (Number) – average floors range  
- `Seed` (Integer) – random seed  

**Outputs:**

- `Footprints` – list of planar curves (building footprints)  
- `Masses` – list of extruded Breps (optional)  
- `Heights` – list of numbers (one per footprint)  
- `Metrics` – basic metrics summary string

A short usage note is in `grasshopper/README_GRASSHOPPER.md`.

---

## 6. License

This project is licensed under the **Apache License 2.0**.

See the [`LICENSE`](LICENSE) file for the full text.

You are free to:

- use the code commercially,  
- modify and distribute it,  
- as long as you keep the copyright & license notices and state changes.
