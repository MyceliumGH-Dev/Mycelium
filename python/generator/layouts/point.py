from __future__ import annotations

from math import sqrt
from typing import List, Tuple

import numpy as np
from shapely.geometry import Polygon
from shapely.affinity import translate

from ..types import Building, LayoutResult, Typology


# --- Tunable "urban design" parameters ----------------------------------------

MIN_EDGE_BUFFER = 5.0          # meters, parcel boundary setback
MIN_BUILDING_BUFFER = 14.0      # meters, min edge-to-edge separation

# Minimum provisional side length for initial estimation
PROVISIONAL_SIDE = 10.0        # meters, minimum point tower size


def generate_point_layout(
    parcel: Polygon,
    n_buildings: int,
    far: float,
    floors_min: float,
    floors_max: float,
    floor_to_floor: float,
    rng: np.random.Generator,
    min_edge_buffer: float = MIN_EDGE_BUFFER,
    min_building_buffer: float = MIN_BUILDING_BUFFER,
) -> LayoutResult:
    """
    POINT typology implementation:

    Priority order:
    1. Maximize number of buildings (up to n_buildings)
    2. Maximize building footprint size (limited by spacing)
    3. Adjust floors to achieve target FAR (within floors_min/floors_max)
    """

    parcel_area = parcel.area
    if parcel_area <= 0:
        raise ValueError("Parcel polygon has non-positive area.")

    floors_min_i = max(1, int(round(floors_min)))
    floors_max_i = max(floors_min_i, int(round(floors_max)))

    # -------------------------------------------------------------------------
    # 1) Estimate building size from FAR for grid spacing
    # -------------------------------------------------------------------------
    
    # Estimate average floors for initial grid sizing
    avg_floors_estimate = (floors_min_i + floors_max_i) / 2.0
    total_floors_estimate = avg_floors_estimate * n_buildings
    
    if far > 0.0 and total_floors_estimate > 0:
        # Estimate footprint area per building from FAR
        estimated_footprint_area = (far * parcel_area) / total_floors_estimate
        side_estimate = sqrt(estimated_footprint_area)
    else:
        # Fallback to provisional size if no FAR target
        side_estimate = PROVISIONAL_SIDE
    
    # Ensure reasonable minimum size
    side_estimate = max(side_estimate, PROVISIONAL_SIDE)
    
    # -------------------------------------------------------------------------
    # 2) Create grid with spacing based on estimated size
    # -------------------------------------------------------------------------
    
    side_prov = side_estimate
    half_prov = side_prov / 2.0

    # Compute a "usable" parcel with a simple fixed setback
    usable_prov = parcel.buffer(-max(min_edge_buffer, half_prov))
    if usable_prov.is_empty:
        minx, miny, maxx, maxy = parcel.bounds
        raise RuntimeError(
            "Parcel too small after applying provisional setbacks for point towers. "
            f"Parcel: {maxx - minx:.1f}m × {maxy - miny:.1f}m, "
            f"Provisional side: {side_prov:.1f}m, "
            f"Setback: {max(min_edge_buffer, half_prov):.1f}m. "
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
    center_min_spacing = side_prov + min_building_buffer

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

    # -------------------------------------------------------------------------
    # 3) Compute maximum allowed footprint size
    # -------------------------------------------------------------------------

    # Inner parcel for final placement
    usable_final = parcel.buffer(-min_edge_buffer)
    if usable_final.is_empty:
        usable_final = parcel

    if usable_final.geom_type == "MultiPolygon":
        usable_final = max(usable_final.geoms, key=lambda g: g.area)

    # Calculate maximum size based on centroid spacing
    if len(centroids) > 1:
        # Find minimum distance between any two centroids
        min_centroid_dist = float('inf')
        for i in range(len(centroids)):
            for j in range(i + 1, len(centroids)):
                cx1, cy1 = centroids[i]
                cx2, cy2 = centroids[j]
                dist = sqrt((cx1 - cx2)**2 + (cy1 - cy2)**2)
                min_centroid_dist = min(min_centroid_dist, dist)
        
        # Maximum building size that maintains MIN_BUILDING_BUFFER spacing
        max_size_from_spacing = min_centroid_dist - min_building_buffer
    else:
        # Single building - only limited by parcel
        max_size_from_spacing = float('inf')
    
    # Also check distance from centroids to parcel boundary
    max_size_from_boundary = float('inf')
    for cx, cy in centroids:
        from shapely.geometry import Point
        centroid_point = Point(cx, cy)
        # Distance to usable parcel boundary
        dist_to_boundary = centroid_point.distance(usable_final.boundary)
        # Building can extend this far from center
        max_radius = dist_to_boundary
        max_size_from_boundary = min(max_size_from_boundary, max_radius * 2)
    
    # Take the minimum of all constraints
    max_allowed_size = min(max_size_from_spacing, max_size_from_boundary)
    max_allowed_size = max(max_allowed_size, PROVISIONAL_SIDE)  # Never smaller than provisional
    
    # Use maximum allowed size (buildings as large as spacing permits)
    side = max_allowed_size
    
    # -------------------------------------------------------------------------
    # 4) Calculate floors to achieve target FAR
    # -------------------------------------------------------------------------
    
    # Total footprint area
    footprint_area_per_building = side * side
    total_footprint_area = footprint_area_per_building * actual_n_buildings
    
    # Calculate floors needed to hit FAR target
    if far > 0.0:
        target_total_floor_area = far * parcel_area
        floors_needed = target_total_floor_area / total_footprint_area
    else:
        # No FAR target, use average of min/max
        floors_needed = (floors_min_i + floors_max_i) / 2.0
    
    # Round and clamp to constraints
    floors_uniform = int(round(floors_needed))
    floors_uniform = max(floors_min_i, min(floors_uniform, floors_max_i))
    
    # Assign floors to each building (could add variation here)
    floors_list = [floors_uniform] * actual_n_buildings
    
    # Floor heights with slight variation
    floor_heights = floor_to_floor * rng.uniform(0.9, 1.1, size=actual_n_buildings)
    
    # -------------------------------------------------------------------------
    # 5) Place final buildings at maximum allowed size
    # -------------------------------------------------------------------------

    buildings_polys: List[Polygon] = []
    for cx, cy in centroids:
        sq = _square_footprint(center=(cx, cy), side=side)
        buildings_polys.append(sq)

    # -------------------------------------------------------------------------
    # 6) Wrap in Building objects and compute density
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
