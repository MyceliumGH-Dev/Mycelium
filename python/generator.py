# python/generator.py
from __future__ import annotations

import math
import random
from enum import Enum
from typing import Dict, List, Iterable, Tuple

from shapely import affinity
from shapely.geometry import Polygon
from shapely.ops import unary_union

from parcel_targets import ParcelTargets


class Typology(str, Enum):
    POINT = "point"
    SLAB = "slab"
    L = "l_shaped"
    U = "u_shaped"
    O = "o_shaped"


# ---------------------------------------------------------------------------
# Prototype shapes in local coordinates (around origin)
# ---------------------------------------------------------------------------

def _prototype_shape(typology: Typology) -> Polygon:
    """Return a unit prototype polygon for the given typology.

    The shape is defined in local coordinates around (0, 0) and will later
    be scaled and placed inside the parcel.
    """
    if typology == Typology.POINT:
        # 1x1 square
        return Polygon([(0, 0), (1, 0), (1, 1), (0, 1)])
    elif typology == Typology.SLAB:
        # 4x1 bar
        return Polygon([(0, 0), (4, 0), (4, 1), (0, 1)])
    elif typology == Typology.L:
        # union of two rectangles forming an L
        r1 = Polygon([(0, 0), (0.6, 0), (0.6, 1.0), (0, 1.0)])
        r2 = Polygon([(0, 0), (1.0, 0), (1.0, 0.4), (0, 0.4)])
        return unary_union([r1, r2])
    elif typology == Typology.U:
        # U shape open at the top
        left = Polygon([(0, 0), (0.3, 0), (0.3, 1.0), (0, 1.0)])
        right = Polygon([(0.7, 0), (1.0, 0), (1.0, 1.0), (0.7, 1.0)])
        bottom = Polygon([(0.3, 0), (0.7, 0), (0.7, 0.3), (0.3, 0.3)])
        return unary_union([left, right, bottom])
    elif typology == Typology.O:
        # donut / perimeter block with inner courtyard
        outer = Polygon([(0, 0), (1.0, 0), (1.0, 1.0), (0, 1.0)])
        inner = Polygon([(0.3, 0.3), (0.7, 0.3), (0.7, 0.7), (0.3, 0.7)])
        return outer.difference(inner)
    else:
        raise ValueError(f"Unsupported typology: {typology}")


def _scale_shape_to_area(poly: Polygon, target_area: float) -> Polygon:
    if target_area <= 0:
        raise ValueError("target_area must be positive")

    current_area = poly.area
    if current_area <= 0:
        raise ValueError("prototype polygon has zero area")

    factor = math.sqrt(target_area / current_area)
    return affinity.scale(poly, xfact=factor, yfact=factor, origin=(0, 0))


def _place_shape_in_site(
    shape: Polygon,
    site_poly: Polygon,
    rng: random.Random,
    max_attempts: int = 200,
) -> Polygon | None:
    """Randomly rotate and translate `shape` so that it fits inside `site_poly`.

    Returns the placed polygon or None if placement failed.
    """
    minx, miny, maxx, maxy = site_poly.bounds

    for _ in range(max_attempts):
        angle_deg = rng.uniform(0.0, 360.0)
        rotated = affinity.rotate(shape, angle_deg, origin=(0, 0))

        sminx, sminy, smaxx, smaxy = rotated.bounds

        # restrict translations so bounding box stays within site bbox
        tx_min = minx - sminx
        tx_max = maxx - smaxx
        ty_min = miny - sminy
        ty_max = maxy - smaxy

        if tx_min >= tx_max or ty_min >= ty_max:
            continue

        tx = rng.uniform(tx_min, tx_max)
        ty = rng.uniform(ty_min, ty_max)

        moved = affinity.translate(rotated, xoff=tx, yoff=ty)

        if site_poly.contains(moved):
            return moved

    return None


# ---------------------------------------------------------------------------
# Geometry helpers
# ---------------------------------------------------------------------------

def _length_width_from_footprint(poly: Polygon) -> Tuple[float, float]:
    """Compute (length, width) from a footprint using the minimum rotated rectangle.

    Returns the longer side as `length` and the shorter as `width`.
    """
    if poly.is_empty:
        return 0.0, 0.0

    mrr = poly.minimum_rotated_rectangle
    coords = list(mrr.exterior.coords)

    # minimum rotated rectangle is a 5-point polygon (first == last)
    edges = []
    for i in range(len(coords) - 1):
        x1, y1 = coords[i]
        x2, y2 = coords[i + 1]
        dx = x2 - x1
        dy = y2 - y1
        dist = math.hypot(dx, dy)
        if dist > 1e-9:
            edges.append(dist)

    if not edges:
        return 0.0, 0.0

    # rectangle: two pairs of equal edges; take unique lengths
    unique_lengths = sorted(set(round(e, 6) for e in edges))
    if len(unique_lengths) == 1:
        # square
        length = width = unique_lengths[0]
    else:
        width = unique_lengths[0]
        length = unique_lengths[-1]

    return float(length), float(width)


def _compute_density(footprints: Iterable[Polygon]) -> float:
    """Compute a simple density metric based on distances between buildings.

    Heuristic:
      - For each building, compute distance to its nearest neighbor.
      - Take the average nearest-neighbor distance `d_avg`.
      - Define Density = N / (1 + d_avg).

    This grows when you have *more* buildings and they are *closer together*.
    """
    footprints = [fp for fp in footprints if not fp.is_empty]
    n = len(footprints)
    if n == 0:
        return 0.0
    if n == 1:
        # Single building; arbitrary low density > 0
        return 1.0

    distances = []
    for i, fp in enumerate(footprints):
        d_min = None
        for j, fp2 in enumerate(footprints):
            if i == j:
                continue
            d = fp.distance(fp2)
            if d_min is None or d < d_min:
                d_min = d
        if d_min is not None:
            distances.append(d_min)

    if not distances:
        return float(n)

    d_avg = sum(distances) / len(distances)
    density = n / (1.0 + d_avg)
    return float(density)


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------

def generate_alternative(
    site_poly: Polygon,
    typology: Typology,
    targets: ParcelTargets,
    n_buildings: int = 1,
    seed: int | None = None,
) -> Dict[str, object]:
    """Generate a single massing alternative.

    Parameters
    ----------
    site_poly : Polygon
        Parcel boundary as a Shapely polygon.
    typology : Typology
        Building typology to use.
    targets : ParcelTargets
        Parcel-level targets (GFA, FAR, floors range, etc.).
    n_buildings : int, optional
        Number of buildings to place inside the parcel.
    seed : int | None, optional
        Random seed for reproducible results.

    Returns
    -------
    dict
        {
          "buildings": [
              {
                  "footprint": Polygon,
                  "centroid": (x, y),
                  "length": float,
                  "width": float,
                  "floors": float,
                  "floor_height": float,
                  "total_height": float,
              },
              ...
          ],
          "metrics": {
              "parcel_area": float,
              "target_gfa": float,
              "actual_gfa": float,
              "target_far": float,
              "actual_far": float,
              "avg_floors": float,
              "scr": float,
              "n_buildings": int,
              "density": float,
          },
        }
    """
    if n_buildings <= 0:
        raise ValueError("n_buildings must be positive")

    rng = random.Random(seed)

    parcel_area = site_poly.area
    # simple average floors for now
    avg_floors = targets.avg_floors()
    floor_height = targets.floor_to_floor

    total_footprint_area = targets.gfa / avg_floors
    per_building_area = total_footprint_area / n_buildings

    proto = _prototype_shape(typology)

    buildings: List[Dict[str, object]] = []

    for _ in range(n_buildings):
        scaled = _scale_shape_to_area(proto, per_building_area)
        placed = _place_shape_in_site(scaled, site_poly, rng)

        if placed is None:
            # failed to place this building; skip
            continue

        length, width = _length_width_from_footprint(placed)
        centroid = (placed.centroid.x, placed.centroid.y)
        total_height = avg_floors * floor_height

        buildings.append(
            {
                "footprint": placed,
                "centroid": centroid,
                "length": length,
                "width": width,
                "floors": avg_floors,
                "floor_height": floor_height,
                "total_height": total_height,
            }
        )

    # recompute metrics from actual geometry we managed to place
    footprints = [b["footprint"] for b in buildings]
    actual_fp_area = sum(fp.area for fp in footprints)
    actual_gfa = actual_fp_area * avg_floors
    actual_far = actual_gfa / parcel_area if parcel_area > 0 else 0.0
    scr = actual_fp_area / parcel_area if parcel_area > 0 else 0.0

    density = _compute_density(footprints)

    metrics = {
        "parcel_area": parcel_area,
        "target_gfa": targets.gfa,
        "actual_gfa": actual_gfa,
        "target_far": targets.far,
        "actual_far": actual_far,
        "avg_floors": avg_floors,
        "scr": scr,
        "n_buildings": len(buildings),
        "density": density,
    }

    return {
        "buildings": buildings,
        "metrics": metrics,
    }


def generate_batch(
    site_poly: Polygon,
    typology: Typology,
    targets: ParcelTargets,
    n_alternatives: int = 10,
    n_buildings: int = 1,
    base_seed: int = 0,
) -> List[Dict[str, object]]:
    """Generate multiple alternatives with different seeds."""
    alts: List[Dict[str, object]] = []
    for i in range(n_alternatives):
        alt = generate_alternative(
            site_poly=site_poly,
            typology=typology,
            targets=targets,
            n_buildings=n_buildings,
            seed=base_seed + i,
        )
        alts.append(alt)
    return alts


# --------
# API
# --------

def generate_layout_from_location(
    parcel_vertices: List[Tuple[float, float]],
    structure_type: str,
    *,
    n_buildings: int = 1,
    far: float = 3.0,
    floors_min: float = 5.0,
    floors_max: float = 12.0,
    floor_to_floor: float = 3.2,  # meters; ~10.5 ft
    seed: int | None = None,
) -> Dict[str, object]:
    """High-level API for your backend.

    Inputs
    ------
    parcel_vertices : list of (x, y)
        The parcel's location/geometry in projected coordinates
        (you can convert from lat/lon earlier in the pipeline).
    structure_type : {"point", "slab", "l_shaped", "u_shaped", "o_shaped"}
        Building typology.
    n_buildings : int, optional
        Number of buildings to try to place.
    far : float, optional
        Target floor area ratio. Used to derive GFA from parcel area.
    floors_min, floors_max : float, optional
        Floors range for targets.
    floor_to_floor : float, optional
        Height of each floor in meters.
    seed : int | None, optional
        Random seed.

    Outputs
    -------
    Same schema as `generate_alternative`, i.e.:
      - "buildings": list of dicts with
          centroid, length, width, floors, floor_height, total_height, footprint
      - "metrics" including "density"
    """
    site_poly = Polygon(parcel_vertices)
    parcel_area = site_poly.area
    gfa = far * parcel_area

    targets = ParcelTargets(
        parcel_area=parcel_area,
        gfa=gfa,
        far=far,
        floors_min=floors_min,
        floors_max=floors_max,
        floor_to_floor=floor_to_floor,
    )

    typology = Typology(structure_type)

    return generate_alternative(
        site_poly=site_poly,
        typology=typology,
        targets=targets,
        n_buildings=n_buildings,
        seed=seed,
    )
