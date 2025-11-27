from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import List, Tuple, Dict, Any
from shapely.geometry import Polygon
import math


class Typology(str, Enum):
    POINT = "point"
    LINEAR = "linear"
    COURTYARD = "courtyard"
    SLAB = "slab"
    L = "l_shaped"
    U = "u_shaped"
    O = "o_shaped"


@dataclass
class Building:
    """Simple representation of a single generated building."""

    footprint: Polygon          # 2D footprint in parcel coordinates
    floors: int                 # total number of floors
    floor_height: float         # height per floor (meters)
    total_height: float         # floors * floor_height
    centroid: Tuple[float, float]


@dataclass
class LayoutResult:
    """Result of a layout generation run for a single parcel."""

    parcel: Polygon
    typology: Typology
    buildings: List[Building]
    far: float                  # target FAR used
    density: float              # simple density metric based on building spacing
    
    def to_dict(self) -> Dict[str, Any]:
        """
        Convert LayoutResult to dictionary format with computed metrics.
        
        Returns a dict with:
        - buildings: list of building dicts with footprint, centroid, dimensions, floors, etc.
        - metrics: dict with parcel_area, target_far, actual_far, actual_gfa, scr, 
                   n_buildings, density, avg_floors
        """
        parcel_area = self.parcel.area
        
        # Compute actual metrics from placed buildings
        total_footprint_area = sum(b.footprint.area for b in self.buildings)
        total_gfa = sum(b.footprint.area * b.floors for b in self.buildings)
        actual_far = total_gfa / parcel_area if parcel_area > 0 else 0.0
        scr = total_footprint_area / parcel_area if parcel_area > 0 else 0.0
        avg_floors = sum(b.floors for b in self.buildings) / len(self.buildings) if self.buildings else 0.0
        
        # Convert buildings to dict format
        buildings_list = []
        for b in self.buildings:
            # Compute length and width from footprint using minimum rotated rectangle
            length, width = self._compute_length_width(b.footprint)
            
            buildings_list.append({
                "footprint": b.footprint,
                "centroid": b.centroid,
                "length": length,
                "width": width,
                "floors": b.floors,
                "floor_height": b.floor_height,
                "total_height": b.total_height,
            })
        
        metrics = {
            "parcel_area": parcel_area,
            "target_gfa": self.far * parcel_area,
            "actual_gfa": total_gfa,
            "target_far": self.far,
            "actual_far": actual_far,
            "scr": scr,
            "n_buildings": len(self.buildings),
            "density": self.density,
            "avg_floors": avg_floors,
        }
        
        return {
            "buildings": buildings_list,
            "metrics": metrics,
            "typology": self.typology.value,
        }
    
    @staticmethod
    def _compute_length_width(footprint: Polygon) -> Tuple[float, float]:
        """Compute length and width from footprint using minimum rotated rectangle."""
        if footprint.is_empty:
            return 0.0, 0.0
        
        mrr = footprint.minimum_rotated_rectangle
        coords = list(mrr.exterior.coords)
        
        # Compute edge lengths
        edges = []
        for i in range(len(coords) - 1):
            x1, y1 = coords[i]
            x2, y2 = coords[i + 1]
            dist = math.hypot(x2 - x1, y2 - y1)
            if dist > 1e-9:
                edges.append(dist)
        
        if not edges:
            return 0.0, 0.0
        
        # Get unique lengths (rectangle has 2 pairs of equal edges)
        unique_lengths = sorted(set(round(e, 6) for e in edges))
        
        if len(unique_lengths) == 1:
            # Square
            length = width = unique_lengths[0]
        else:
            width = unique_lengths[0]
            length = unique_lengths[-1]
        
        return float(length), float(width)
