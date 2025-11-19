from __future__ import annotations

from math import sqrt
from typing import List, Tuple

import numpy as np
from shapely.geometry import Polygon
from shapely.affinity import translate

from ..types import Building, LayoutResult, Typology


# --- Tunable "urban design" parameters ----------------------------------------

MIN_EDGE_BUFFER = 5.0          # meters, parcel boundary setback
MIN_BUILDING_BUFFER = 6.0      # meters, min edge-to-edge separation

# Provisional side length used only to find feasible centroids
PROVISIONAL_SIDE = 10.0        # meters, small-ish point tower

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

    # Bounds for building centers (to ensure they stay inside the usable parcel)
    min_cx = u_minx + half_prov
    max_cx = u_maxx - half_prov
    min_cy = u_miny + half_prov
    max_cy = u_maxy - half_prov

    width_centers = max_cx - min_cx
    height_centers = max_cy - min_cy

    if width_centers < 0 or height_centers < 0:
        minx, miny, maxx, maxy = parcel.bounds
        raise RuntimeError(
            "Usable parcel too small for provisional towers after setbacks. "
            f"Parcel: {maxx - minx:.1f}m × {maxy - miny:.1f}m."
        )

    # Minimum center-to-center spacing to respect edge buffer
    center_min_spacing = side_prov + MIN_BUILDING_BUFFER

    # Max possible columns/rows with at least the minimum spacing
    if width_centers <= 0:
        max_cols = 1
    else:
        max_cols = int(width_centers // center_min_spacing) + 1

    if height_centers <= 0:
        max_rows = 1
    else:
        max_rows = int(height_centers // center_min_spacing) + 1

    max_cols = max(1, max_cols)
    max_rows = max(1, max_rows)

    # Total capacity of the grid
    max_cells = max_rows * max_cols
    if max_cells == 0:
        minx, miny, maxx, maxy = parcel.bounds
        raise RuntimeError(
            "Cannot place any provisional points in parcel with current setbacks/spacing. "
            f"Parcel: {maxx - minx:.1f}m × {maxy - miny:.1f}m."
        )

    # We can never place more towers than total cells
    n_target = min(n_buildings, max_cells)

    # Choose (rows, cols) such that rows*cols >= n_target,
    # trading off empties vs aspect ratio.
    aspect_parcel = (width_centers / height_centers) if height_centers > 0 else 1.0

    # Weights: aspect ratio is more important than a couple extra empties.
    EMPTY_WEIGHT = 1.0
    ASPECT_WEIGHT = 6.0

    best_rows, best_cols = 1, max(1, n_target)
    best_score = None  # (cost, empty_cells, aspect_diff)

    for rows in range(1, max_rows + 1):
        for cols in range(1, max_cols + 1):
            cells = rows * cols
            if cells < n_target:
                continue  # not enough capacity

            empty_cells = cells - n_target
            aspect_grid = cols / rows
            aspect_diff = abs(aspect_grid - aspect_parcel)

            cost = EMPTY_WEIGHT * empty_cells + ASPECT_WEIGHT * aspect_diff

            score = (cost, empty_cells, aspect_diff)
            if best_score is None or score < best_score:
                best_score = score
                best_rows, best_cols = rows, cols

    rows = best_rows
    cols = best_cols
    cells = rows * cols  # total grid slots

    # Compute evenly spaced grid that uses the full inner-usable extent.
    # Spacing will be >= center_min_spacing because of how max_rows/max_cols are defined.

    if cols == 1:
        # Single column → center in X
        xs = [0.5 * (min_cx + max_cx)]
    else:
        spacing_x = width_centers / (cols - 1)
        xs = [min_cx + i * spacing_x for i in range(cols)]

    if rows == 1:
        # Single row → center in Y
        ys = [0.5 * (min_cy + max_cy)]
    else:
        spacing_y = height_centers / (rows - 1)
        ys = [min_cy + j * spacing_y for j in range(rows)]


    # Build all grid centers
    grid_centers: List[Tuple[float, float]] = [
        (x, y) for y in ys for x in xs
    ]

    # Filter to those whose provisional footprint is fully inside the usable parcel
    candidates: List[Tuple[float, float, Polygon]] = []
    for cx, cy in grid_centers:
        sq = _square_footprint(center=(cx, cy), side=side_prov)
        if sq.within(usable_prov):
            candidates.append((cx, cy, sq))

    if not candidates:
        minx, miny, maxx, maxy = parcel.bounds
        raise RuntimeError(
            "No valid provisional tower positions inside usable parcel. "
            f"Parcel: {maxx - minx:.1f}m × {maxy - miny:.1f}m, "
            f"Provisional side: {side_prov:.1f}m."
        )

    # We might have lost some cells due to polygon shape
    # Final number of buildings we can actually place at provisional step:
    n_place = min(n_target, len(candidates))

    # Sort candidates by distance to parcel center so we fill from the middle outwards
    center_x = 0.5 * (u_minx + u_maxx)
    center_y = 0.5 * (u_miny + u_maxy)

    candidates.sort(
        key=lambda c: (c[0] - center_x) ** 2 + (c[1] - center_y) ** 2
    )

    chosen = candidates[:n_place]

    centroids: List[Tuple[float, float]] = [(cx, cy) for (cx, cy, _sq) in chosen]
    provisional_polys: List[Polygon] = [_sq for (_cx, _cy, _sq) in chosen]

    actual_n_buildings = len(centroids)
    if actual_n_buildings == 0:
        minx, miny, maxx, maxy = parcel.bounds
        raise RuntimeError(
            "Could not place any point buildings inside parcel with current "
            "provisional setbacks and spacing constraints."
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
