from __future__ import annotations

from typing import Sequence, Tuple, Union, Optional, Dict, Any

import numpy as np
from shapely.geometry import Polygon

from .types import Typology, LayoutResult
from .layouts.point import generate_point_layout
from .layouts.linear import generate_linear_layout
from .layouts.courtyard import generate_courtyard_layout


def generate_layout_from_location(
    parcel_vertices: Sequence[Tuple[float, float]],
    structure_type: Union[Typology, str],
    n_buildings: int,
    far: float,
    floors_min: float,
    floors_max: float,
    floor_to_floor: float,
    seed: Optional[int] = None,
    min_edge_buffer: Optional[float] = None,
    min_building_buffer: Optional[float] = None,
    min_building_thickness: Optional[float] = None,
) -> Dict[str, Any]:
    """
    Main entry-point for generating building layouts.
    
    Parameters
    ----------
    parcel_vertices : Sequence[Tuple[float, float]]
        List of (x, y) coordinates defining the parcel boundary.
    structure_type : Union[Typology, str]
        Building typology (e.g., "point", "slab").
    n_buildings : int
        Number of buildings to place.
    far : float
        Floor area ratio (target).
    floors_min, floors_max : float
        Range for randomized floor counts.
    floor_to_floor : float
        Height of each floor in meters.
    seed : Optional[int]
        Random seed for reproducibility.
    min_edge_buffer : Optional[float]
        Parcel boundary setback in meters (default varies by typology).
    min_building_buffer : Optional[float]
        Minimum edge-to-edge separation between buildings in meters (for linear/point).
    min_building_thickness : Optional[float]
        Minimum building thickness in meters (for courtyard).
        
    Returns
    -------
    Dict[str, Any]
        Dictionary containing:
        - buildings: list of building dicts with footprint, centroid, length, 
                     width, floors, floor_height, total_height
        - metrics: dict with parcel_area, target_gfa, actual_gfa, target_far,
                   actual_far, scr, n_buildings, density, avg_floors
        - typology: string name of the typology used
        
    Note: 'density' is computed automatically based on building spacing.
    """

    if isinstance(structure_type, Typology):
        typology = structure_type
    else:
        typology = Typology(structure_type)

    parcel = Polygon(parcel_vertices)
    if not parcel.is_valid or parcel.area <= 0:
        raise ValueError("Parcel polygon is invalid or has non-positive area.")

    rng = np.random.default_rng(seed)

    # Route to specified layout's generator
    if typology is Typology.POINT:
        kwargs = {
            'parcel': parcel,
            'n_buildings': n_buildings,
            'far': far,
            'floors_min': floors_min,
            'floors_max': floors_max,
            'floor_to_floor': floor_to_floor,
            'rng': rng,
        }
        if min_edge_buffer is not None:
            kwargs['min_edge_buffer'] = min_edge_buffer
        if min_building_buffer is not None:
            kwargs['min_building_buffer'] = min_building_buffer
        result = generate_point_layout(**kwargs)
    elif typology is Typology.LINEAR:
        kwargs = {
            'parcel': parcel,
            'n_buildings': n_buildings,
            'far': far,
            'floors_min': floors_min,
            'floors_max': floors_max,
            'floor_to_floor': floor_to_floor,
            'rng': rng,
        }
        if min_edge_buffer is not None:
            kwargs['min_edge_buffer'] = min_edge_buffer
        if min_building_buffer is not None:
            kwargs['min_building_buffer'] = min_building_buffer
        result = generate_linear_layout(**kwargs)
    elif typology is Typology.COURTYARD:
        kwargs = {
            'parcel': parcel,
            'n_buildings': n_buildings,
            'far': far,
            'floors_min': floors_min,
            'floors_max': floors_max,
            'floor_to_floor': floor_to_floor,
            'rng': rng,
        }
        if min_edge_buffer is not None:
            kwargs['min_edge_buffer'] = min_edge_buffer
        if min_building_thickness is not None:
            kwargs['min_building_thickness'] = min_building_thickness
        result = generate_courtyard_layout(**kwargs)
    else:
        raise NotImplementedError(f"Typology {typology.value!r} is not implemented yet.")
    
    # Convert to dict format with computed metrics including density
    return result.to_dict()
