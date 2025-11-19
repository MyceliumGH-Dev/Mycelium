# python/generator.py
from __future__ import annotations

import math
import random
from enum import Enum
from typing import Dict, List

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
          "footprints": list of shapely Polygons,
          "heights": list of floats (meters),
          "metrics": dict with GFA, FAR, SCR, etc.
        }
    """
    if n_buildings <= 0:
        raise ValueError("n_buildings must be positive")

    rng = random.Random(seed)

    parcel_area = site_poly.area
    # simple average floors for now
    avg_floors = targets.avg_floors()

    total_footprint_area = targets.gfa / avg_floors
    per_building_area = total_footprint_area / n_buildings

    proto = _prototype_shape(typology)

    footprints: List[Polygon] = []
    heights: List[float] = []

    for _ in range(n_buildings):
        scaled = _scale_shape_to_area(proto, per_building_area)
        placed = _place_shape_in_site(scaled, site_poly, rng)

        if placed is None:
            # failed to place this building; skip
            continue

        footprints.append(placed)
        heights.append(targets.height_for_floors(avg_floors))

    # recompute metrics from actual geometry we managed to place
    actual_fp_area = sum(p.area for p in footprints)
    actual_gfa = actual_fp_area * avg_floors
    actual_far = actual_gfa / parcel_area if parcel_area > 0 else 0.0
    scr = actual_fp_area / parcel_area if parcel_area > 0 else 0.0

    metrics = {
        "parcel_area": parcel_area,
        "target_gfa": targets.gfa,
        "actual_gfa": actual_gfa,
        "target_far": targets.far,
        "actual_far": actual_far,
        "avg_floors": avg_floors,
        "scr": scr,
        "n_buildings": len(footprints),
    }

    return {
        "footprints": footprints,
        "heights": heights,
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
