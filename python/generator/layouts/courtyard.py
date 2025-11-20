from __future__ import annotations

from typing import Tuple

import numpy as np
from shapely.geometry import Polygon
from shapely.ops import unary_union

from ..types import Building, LayoutResult, Typology


# --- Tunable "urban design" parameters ----------------------------------------

MIN_EDGE_BUFFER = 5.0          # meters, parcel boundary setback
MIN_BUILDING_THICKNESS = 6.0


def generate_courtyard_layout(
    parcel: Polygon,
    n_buildings: int,  # ignored 
    far: float,
    floors_min: float,
    floors_max: float,
    floor_to_floor: float,
    rng: np.random.Generator,
) -> LayoutResult:
    """
    COURTYARD:
    - A single connected ring-shaped building hugging the usable parcel interior.
    - Hollow inside.
    - Floors chosen to approximately achieve desired FAR.
    """

    # --- 1) Usable interior ---------------------------------------------------
    usable = _get_usable_parcel(parcel)
    minx, miny, maxx, maxy = usable.bounds

    width = maxx - minx
    height = maxy - miny
    if width <= 0 or height <= 0:
        raise ValueError("Parcel has non-positive width/height")

    min_dim = min(width, height)

    # Initial guess for ring thickness as a fraction of the smaller dimension.
    # We will *reduce* this if it produces no inner courtyard.
    thickness = max(MIN_BUILDING_THICKNESS, 0.15 * min_dim)

    # --- 1a) Find a thickness that yields a hollow interior -------------------
    inner = usable.buffer(-thickness)

    # Iteratively thin the ring if the inner collapses
    # (e.g., very small parcel or very concave shape).
    attempts = 0
    max_attempts = 5
    while inner.is_empty and attempts < max_attempts and thickness > MIN_BUILDING_THICKNESS:
        thickness *= 0.5
        inner = usable.buffer(-thickness)
        attempts += 1

    if inner.is_empty:
        # At this point, we could:
        # - raise an error, or
        # - fall back to POINT/LINEAR, or
        # - return no layout.
        # To preserve the "ring" semantics, we *do not* make it solid here.
        raise RuntimeError(
            "Parcel is too small or irregular to form a hollow courtyard ring "
            f"(final thickness tried: {thickness:.2f} m)."
        )

    # Now we *know* we have a hollow interior; the building is the ring only.
    ring_footprint = usable.difference(inner)

    if ring_footprint.is_empty or ring_footprint.area <= 1e-3:
        raise RuntimeError("Could not construct a valid courtyard ring footprint")

    # If geometry is fragmented, merge into one multi-edge ring.
    if ring_footprint.geom_type == "MultiPolygon":
        ring_footprint = unary_union(ring_footprint)

    footprint_area = ring_footprint.area

    # --- 2) Floors to approximate FAR -----------------------------------------
    floors_min_i = max(1, int(round(floors_min)))
    floors_max_i = max(floors_min_i, int(round(floors_max)))

    target_total_floor_area = far * parcel.area

    floors_float = target_total_floor_area / footprint_area
    floors = int(round(floors_float))
    floors = max(floors_min_i, min(floors, floors_max_i))

    # Slight per-building variation in floor height
    floor_height = floor_to_floor * rng.uniform(0.95, 1.05)
    total_height = floors * floor_height
    total_floor_area = footprint_area * floors
    far_actual = total_floor_area / parcel.area

    building = Building(
        footprint=ring_footprint,
        floors=floors,
        floor_height=floor_height,
        total_height=total_height,
        centroid=(ring_footprint.centroid.x, ring_footprint.centroid.y),
    )

    # Density metric: tighter courtyard (smaller inner area) → "denser" feel
    courtyard_area = inner.area
    density = footprint_area / (1.0 + courtyard_area)

    return LayoutResult(
        parcel=parcel,
        typology=Typology.O,  # O-shaped courtyard typology
        buildings=[building],
        far=far_actual,
        density=density,
    )


def _get_usable_parcel(parcel: Polygon) -> Polygon:
    """
    Apply setback buffer to parcel and return the usable interior.
    Handles MultiPolygon cases by taking the largest piece.
    """
    usable = parcel.buffer(-MIN_EDGE_BUFFER)

    if usable.is_empty:
        # Parcel too small for setback; use full parcel
        return parcel

    if usable.geom_type == "MultiPolygon":
        usable = max(usable.geoms, key=lambda g: g.area)

    return usable
