from __future__ import annotations

from math import sqrt
from typing import List, Tuple

import numpy as np
from shapely.geometry import Polygon
from shapely.affinity import translate

from ..types import Building, LayoutResult, Typology


# --- Tunable "urban design" parameters ----------------------------------------

# Hard-ish geometric constraints
MIN_EDGE_BUFFER = 5.0          # meters, parcel boundary setback
MIN_BUILDING_BUFFER = 6.0      # meters, min edge-to-edge separation

# Provisional side length used only to find feasible centroids
PROVISIONAL_SIDE = 15.0        # meters, small-ish point tower

# Iterative resize parameters for FAR balancing
MAX_RESIZE_ITERS = 20
RESIZE_SHRINK_FACTOR = 0.9     # shrink 10% per failed attempt


def generate_point_layout(
    parcel: Polygon,
    n_buildings: int,
    far: float,
    floors_min: float,
    floors_max: float,
    floor_to_floor: float,
    rng: np.random.Generator,
) -> LayoutResult:
    """
    POINT typology implementation with constraint-relaxation:

    1. Sample floors and floor heights for up to `n_buildings`.
    2. Use a provisional, small square footprint (PROVISIONAL_SIDE) to:
       - create an inner "usable" parcel (setbacks)
       - place as many buildings as fit (up to n_buildings) on a jittered grid.
       This step defines feasible centroids and actual_n_buildings.
    3. Given actual_n_buildings and their floors, compute the *ideal* footprint
       area to hit the requested FAR, and derive a target side length.
    4. Iteratively attempt to place buildings with this side length, shrinking
       the side if needed to satisfy:
         - within inner parcel (MIN_EDGE_BUFFER)
         - minimum edge-to-edge spacing (MIN_BUILDING_BUFFER)
    5. Wrap the final footprints + heights into Building objects and return
       a LayoutResult. Actual FAR may be lower than target if constraints bind.
    """

    # --- 1) RNG for floors + floor height -------------------------------------
    floors_min_i = max(1, int(round(floors_min)))
    floors_max_i = max(floors_min_i, int(round(floors_max)))

    # Requested floors list (upper bound for how many we MIGHT place)
    floors_list: List[int] = rng.integers(
        low=floors_min_i,
        high=floors_max_i + 1,
        size=n_buildings,
    ).tolist()

    # Floor height: vary around floor_to_floor (±10%) as RNG
    floor_heights = floor_to_floor * rng.uniform(0.9, 1.1, size=n_buildings)

    parcel_area = parcel.area
    if parcel_area <= 0:
        raise ValueError("Parcel polygon has non-positive area.")

    # -------------------------------------------------------------------------
    # 2) PROVISIONAL STEP: find feasible centroids with a small footprint
    # -------------------------------------------------------------------------

    side_prov = PROVISIONAL_SIDE
    half_prov = side_prov / 2.0

    # Compute a "usable" parcel with a simple fixed setback
    usable_prov = parcel.buffer(-max(MIN_EDGE_BUFFER, half_prov))
    if usable_prov.is_empty:
        minx, miny, maxx, maxy = parcel.bounds
        raise RuntimeError(
            "Parcel too small after applying provisional setbacks for point towers. "
            f"Parcel: {maxx - minx:.1f}m × {maxy - miny:.1f}m, "
            f"Provisional side: {side_prov:.1f}m, "
            f"Setback: {max(MIN_EDGE_BUFFER, half_prov):.1f}m. "
            "Try increasing parcel size or reducing setbacks."
        )

    # If the usable parcel is multiple disjoint regions, keep only the largest.
    if usable_prov.geom_type == "MultiPolygon":
        usable_prov = max(usable_prov.geoms, key=lambda g: g.area)

    u_minx, u_miny, u_maxx, u_maxy = usable_prov.bounds

    # Center spacing based on provisional size + min spacing
    center_spacing = side_prov + MIN_BUILDING_BUFFER

    # Generate jittered grid of candidate centers
    candidate_points: List[Tuple[float, float]] = []

    y = u_miny + half_prov
    while y <= u_maxy - half_prov:
        x = u_minx + half_prov
        while x <= u_maxx - half_prov:
            jitter_range = 0.25 * center_spacing
            jx = rng.uniform(-jitter_range, jitter_range)
            jy = rng.uniform(-jitter_range, jitter_range)

            cx = x + jx
            cy = y + jy

            # Clamp inside usable bounds
            cx = float(np.clip(cx, u_minx + half_prov, u_maxx - half_prov))
            cy = float(np.clip(cy, u_miny + half_prov, u_maxy - half_prov))

            candidate_points.append((cx, cy))

            x += center_spacing
        y += center_spacing

    rng.shuffle(candidate_points)

    # Place provisional buildings to determine feasible centroids
    centroids: List[Tuple[float, float]] = []
    provisional_polys: List[Polygon] = []

    for cx, cy in candidate_points:
        if len(centroids) >= n_buildings:
            break

        sq = _square_footprint(center=(cx, cy), side=side_prov)

        if not sq.within(usable_prov):
            continue

        # Reject a building if it violates the spacing constraints
        if any(sq.distance(other) < MIN_BUILDING_BUFFER for other in provisional_polys):
            continue

        provisional_polys.append(sq)
        centroids.append((cx, cy))

    actual_n_buildings = len(centroids)
    if actual_n_buildings == 0:
        minx, miny, maxx, maxy = parcel.bounds
        raise RuntimeError(
            "Could not place any point buildings inside parcel with current "
            "provisional setbacks and spacing constraints. "
            f"Parcel: {maxx - minx:.1f}m × {maxy - miny:.1f}m, "
            f"Provisional side: {side_prov:.1f}m, "
            f"Setback: {MIN_EDGE_BUFFER:.1f}m, "
            f"Min edge spacing: {MIN_BUILDING_BUFFER:.1f}m. "
            "Try reducing minimum spacing or increasing parcel size."
        )

    # Trim floor lists to actual placement capacity
    floors_list = floors_list[:actual_n_buildings]
    floor_heights = floor_heights[:actual_n_buildings]

    # -------------------------------------------------------------------------
    # 3) Compute ideal footprint size from FAR + actual floors
    # -------------------------------------------------------------------------

    total_floors = float(sum(floors_list))
    if total_floors <= 0:
        raise ValueError("Total floors computed as zero; check floors_min/floors_max.")

    if far > 0.0:
        ideal_footprint_area = far * parcel_area / total_floors
        side_target = sqrt(ideal_footprint_area)
    else:
        # No FAR target → just keep provisional size
        side_target = side_prov

    # We'll try to use side_target, but may shrink to satisfy constraints
    side = side_target

    # Inner parcel for final placement; we keep a basic fixed setback
    usable_final = parcel.buffer(-MIN_EDGE_BUFFER)
    if usable_final.is_empty:
        usable_final = parcel  # fall back to full parcel if setback kills everything

    if usable_final.geom_type == "MultiPolygon":
        usable_final = max(usable_final.geoms, key=lambda g: g.area)

    # -------------------------------------------------------------------------
    # 4) Iteratively attempt to place final-sized buildings, shrinking if needed
    # -------------------------------------------------------------------------

    buildings_polys: List[Polygon] = []
    for _ in range(MAX_RESIZE_ITERS):
        candidate_polys: List[Polygon] = []
        ok = True

        for cx, cy in centroids:
            sq = _square_footprint(center=(cx, cy), side=side)

            if not sq.within(usable_final):
                ok = False
                break

            # Enforce minimum spacing
            if any(sq.distance(other) < MIN_BUILDING_BUFFER for other in candidate_polys):
                ok = False
                break

            candidate_polys.append(sq)

        if ok:
            buildings_polys = candidate_polys
            break

        # If constraints fail, shrink the footprint and try again
        side *= RESIZE_SHRINK_FACTOR

    if not buildings_polys:
        # As a last resort, fall back to provisional size (which we know fits)
        buildings_polys = []
        for (cx, cy) in centroids:
            buildings_polys.append(_square_footprint(center=(cx, cy), side=side_prov))
        side = side_prov  # realized side is provisional

    # -------------------------------------------------------------------------
    # 5) Wrap in Building objects and compute density
    # -------------------------------------------------------------------------

    buildings: List[Building] = []
    for i, fp in enumerate(buildings_polys):
        floors_i = floors_list[i]
        fh_i = float(floor_heights[i])
        buildings.append(
            Building(
                footprint=fp,
                floors=floors_i,
                floor_height=fh_i,
                total_height=floors_i * fh_i,
                centroid=centroids[i],
            )
        )

    density_value = _compute_density_from_centroids(centroids)

    return LayoutResult(
        parcel=parcel,
        typology=Typology.POINT,
        buildings=buildings,
        far=far,
        density=density_value,
    )


def _square_footprint(center: Tuple[float, float], side: float) -> Polygon:
    """Axis-aligned square centered at `center` with edge length `side`."""
    half = side / 2.0
    cx, cy = center
    base = Polygon([(-half, -half), (half, -half), (half, half), (-half, half)])
    return translate(base, xoff=cx, yoff=cy)


def _compute_density_from_centroids(centroids: List[Tuple[float, float]]) -> float:
    """
    Toy density metric:
      density = n_buildings / (1 + mean_pairwise_distance)

    Higher when buildings are closer together.
    """
    n = len(centroids)
    if n < 2:
        return float(n)

    dists = []
    for i in range(n):
        x1, y1 = centroids[i]
        for j in range(i + 1, n):
            x2, y2 = centroids[j]
            d = sqrt((x1 - x2) ** 2 + (y1 - y2) ** 2)
            dists.append(d)

    mean_dist = sum(dists) / len(dists) if dists else 0.0
    return n / (1.0 + mean_dist)
