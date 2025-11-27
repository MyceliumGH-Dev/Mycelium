from __future__ import annotations

from typing import List

import numpy as np
from shapely.geometry import Polygon
from shapely.ops import unary_union

from ..types import Building, LayoutResult, Typology


# --- Tunable "urban design" parameters ----------------------------------------

MIN_EDGE_BUFFER = 5.0          # meters, parcel boundary setback
MIN_BUILDING_BUFFER = 3.0      # meters, min edge-to-edge separation


def _get_usable_parcel(parcel: Polygon) -> Polygon:
    """
    Apply edge buffer to the parcel and return the usable interior.
    Falls back to the original parcel if buffering collapses it.
    """
    usable = parcel.buffer(-MIN_EDGE_BUFFER)
    if usable.is_empty:
        return parcel
    # In case buffer produces MultiPolygon, take union
    if usable.geom_type == "MultiPolygon":
        usable = unary_union(usable)
    return usable


def _fit_rectangle_to_parcel(rect: Polygon, parcel: Polygon) -> Polygon:
    """
    Shrink a rectangle to fit within a parcel while keeping it rectangular.
    Returns the largest axis-aligned rectangle that fits within the parcel.
    
    For irregular parcels, samples along the bar's length to find where
    it intersects the parcel boundary and trims accordingly.
    """
    from shapely.geometry import Point
    
    rect_minx, rect_miny, rect_maxx, rect_maxy = rect.bounds
    
    # Check if rectangle is already fully within parcel
    if rect.within(parcel):
        return rect
    
    # Determine if this is a horizontal or vertical bar
    width = rect_maxx - rect_minx
    height = rect_maxy - rect_miny
    is_horizontal = width > height
    
    # For horizontal bars, shrink from left and/or right
    # For vertical bars, shrink from top and/or bottom
    
    if is_horizontal:
        # Sample along the length to find valid x-range
        y_mid = (rect_miny + rect_maxy) / 2
        
        # Find leftmost valid x
        left_x = rect_minx
        for x in np.linspace(rect_minx, rect_maxx, 50):
            test_point = Point(x, y_mid)
            if parcel.contains(test_point):
                left_x = x
                break
        
        # Find rightmost valid x
        right_x = rect_maxx
        for x in np.linspace(rect_maxx, rect_minx, 50):
            test_point = Point(x, y_mid)
            if parcel.contains(test_point):
                right_x = x
                break
        
        # Ensure we have a valid range
        if left_x >= right_x:
            return Polygon()
        
        # Create fitted rectangle
        fitted = Polygon([
            (left_x, rect_miny),
            (right_x, rect_miny),
            (right_x, rect_maxy),
            (left_x, rect_maxy),
        ])
        
    else:
        # Vertical bar: shrink from top/bottom
        x_mid = (rect_minx + rect_maxx) / 2
        
        # Find bottommost valid y
        bottom_y = rect_miny
        for y in np.linspace(rect_miny, rect_maxy, 50):
            test_point = Point(x_mid, y)
            if parcel.contains(test_point):
                bottom_y = y
                break
        
        # Find topmost valid y
        top_y = rect_maxy
        for y in np.linspace(rect_maxy, rect_miny, 50):
            test_point = Point(x_mid, y)
            if parcel.contains(test_point):
                top_y = y
                break
        
        # Ensure we have a valid range
        if bottom_y >= top_y:
            return Polygon()
        
        # Create fitted rectangle
        fitted = Polygon([
            (rect_minx, bottom_y),
            (rect_maxx, bottom_y),
            (rect_maxx, top_y),
            (rect_minx, top_y),
        ])
    
    return fitted


def generate_linear_layout(
    parcel: Polygon,
    n_buildings: int,
    far: float,
    floors_min: float,
    floors_max: float,
    floor_to_floor: float,
    rng: np.random.Generator,
) -> LayoutResult:
    """
    LINEAR typology:
    - n_buildings ≈ number of rows (slabs).
    - Each row is a long bar spanning across the parcel interior.
    - Bars are stacked with MIN_BUILDING_BUFFER separation.
    - Geometry is determined first; floors are then chosen to roughly match FAR.
    """

    if n_buildings < 1:
        raise ValueError("n_buildings must be at least 1 for LINEAR typology")

    # --- 1) Usable interior ---------------------------------------------------
    usable = _get_usable_parcel(parcel)
    minx, miny, maxx, maxy = usable.bounds

    width = maxx - minx
    height = maxy - miny

    if width <= 0 or height <= 0:
        raise ValueError("Parcel has non-positive width/height")

    # Decide orientation:
    # - If width >= height, bars run horizontally (span X, stacked along Y)
    # - Else, bars run vertically (span Y, stacked along X)
    horizontal = width >= height

    buildings: List[Building] = []

    if horizontal:
        # Bars span X, stacked along Y
        span = width - 2 * MIN_BUILDING_BUFFER
        stack_dim = height - 2 * MIN_BUILDING_BUFFER
        if span <= 0 or stack_dim <= 0:
            raise ValueError("Parcel too small after buffers for LINEAR layout")

        # Thickness per row (so rows + gaps fill the stack_dim)
        total_gap = (n_buildings - 1) * MIN_BUILDING_BUFFER
        thickness = (stack_dim - total_gap) / n_buildings
        if thickness <= 0:
            raise ValueError("Not enough room for requested number of rows")

        x0 = minx + MIN_BUILDING_BUFFER
        x1 = maxx - MIN_BUILDING_BUFFER

        y_cursor = miny + MIN_BUILDING_BUFFER
        row_polys: List[Polygon] = []

        for _ in range(n_buildings):
            y0 = y_cursor
            y1 = y_cursor + thickness

            rect = Polygon(
                [
                    (x0, y0),
                    (x1, y0),
                    (x1, y1),
                    (x0, y1),
                ]
            )
            # Fit rectangle to parcel, keeping it rectangular
            footprint = _fit_rectangle_to_parcel(rect, usable)
            if not footprint.is_empty and footprint.area > 1e-3:
                row_polys.append(footprint)

            y_cursor = y1 + MIN_BUILDING_BUFFER

    else:
        # Bars span Y, stacked along X
        span = height - 2 * MIN_BUILDING_BUFFER
        stack_dim = width - 2 * MIN_BUILDING_BUFFER
        if span <= 0 or stack_dim <= 0:
            raise ValueError("Parcel too small after buffers for LINEAR layout")

        total_gap = (n_buildings - 1) * MIN_BUILDING_BUFFER
        thickness = (stack_dim - total_gap) / n_buildings
        if thickness <= 0:
            raise ValueError("Not enough room for requested number of rows")

        y0 = miny + MIN_BUILDING_BUFFER
        y1 = maxy - MIN_BUILDING_BUFFER

        x_cursor = minx + MIN_BUILDING_BUFFER
        row_polys = []

        for _ in range(n_buildings):
            x0 = x_cursor
            x1 = x_cursor + thickness

            rect = Polygon(
                [
                    (x0, y0),
                    (x1, y0),
                    (x1, y1),
                    (x0, y1),
                ]
            )
            # Fit rectangle to parcel, keeping it rectangular
            footprint = _fit_rectangle_to_parcel(rect, usable)
            if not footprint.is_empty and footprint.area > 1e-3:
                row_polys.append(footprint)

            x_cursor = x1 + MIN_BUILDING_BUFFER

    if not row_polys:
        raise RuntimeError("No usable linear building footprints could be created")

    # --- 2) Decide floors to approximate FAR -----------------------------------
    # Floors bounds as ints
    floors_min_i = max(1, int(round(floors_min)))
    floors_max_i = max(floors_min_i, int(round(floors_max)))

    total_footprint_area = sum(p.area for p in row_polys)
    if total_footprint_area <= 0:
        raise RuntimeError("Total footprint area is non-positive")

    target_total_floor_area = far * parcel.area

    # Average floors needed if all rows share same number of floors
    floors_avg_float = target_total_floor_area / total_footprint_area

    # Clamp to [floors_min_i, floors_max_i]
    floors_avg = int(round(floors_avg_float))
    floors_avg = max(floors_min_i, min(floors_avg, floors_max_i))

    # You could add some RNG variation per row; for now keep all equal
    total_floor_area = 0.0

    for poly in row_polys:
        floors = floors_avg
        
        # Floor height with slight variation
        floor_height = floor_to_floor * rng.uniform(0.95, 1.05)
        total_height = floors * floor_height
        
        total_floor_area += poly.area * floors

        buildings.append(
            Building(
                footprint=poly,
                floors=floors,
                floor_height=floor_height,
                total_height=total_height,
                centroid=(poly.centroid.x, poly.centroid.y),
            )
        )

    far_actual = total_floor_area / parcel.area

    # Density metric: for linear, use average spacing between slabs
    # Higher when slabs are closer together
    if len(buildings) > 1:
        # Calculate average center-to-center spacing
        centroids = [b.centroid for b in buildings]
        total_spacing = 0.0
        for i in range(len(centroids) - 1):
            c1 = centroids[i]
            c2 = centroids[i + 1]
            spacing = ((c1[0] - c2[0])**2 + (c1[1] - c2[1])**2)**0.5
            total_spacing += spacing
        avg_spacing = total_spacing / (len(buildings) - 1)
        density = len(buildings) / (1.0 + avg_spacing)
    else:
        density = 1.0

    return LayoutResult(
        parcel=parcel,
        typology=Typology.LINEAR,
        buildings=buildings,
        far=far,
        density=density,
    )
