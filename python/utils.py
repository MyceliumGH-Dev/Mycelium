# python/utils.py
from __future__ import annotations

import matplotlib.pyplot as plt
from shapely.geometry import Polygon, MultiPolygon
from shapely.geometry.base import BaseGeometry


def _plot_polygon_with_holes(ax, poly: Polygon, filled: bool = False, alpha: float = 0.2, edgecolor: str = "black"):
    """
    Plot a polygon, properly handling interior holes (e.g., for courtyard buildings).
    """
    if poly.is_empty:
        return
    
    # Plot exterior ring
    x, y = poly.exterior.xy
    if filled:
        ax.fill(x, y, alpha=alpha, edgecolor=edgecolor, linewidth=1)
    else:
        ax.plot(x, y, color=edgecolor)
    
    # Plot holes: fill them with background color (white) to create hollow effect
    for interior in poly.interiors:
        ix, iy = interior.xy
        if filled:
            ax.fill(ix, iy, facecolor="white", edgecolor=edgecolor, linewidth=1)
        else:
            ax.plot(ix, iy, color=edgecolor)


def _plot_polygon(ax, poly: BaseGeometry, filled: bool = False, alpha: float = 0.2):
    """
    Plot any shapely geometry (Polygon, MultiPolygon, etc.).
    Handles polygons with holes correctly.
    """
    if poly.is_empty:
        return
    
    if isinstance(poly, MultiPolygon):
        for g in poly.geoms:
            _plot_polygon_with_holes(ax, g, filled, alpha)
    elif isinstance(poly, Polygon):
        _plot_polygon_with_holes(ax, poly, filled, alpha)
    elif hasattr(poly, "geoms"):
        # Generic multi-geometry fallback
        for g in poly.geoms:
            _plot_polygon(ax, g, filled, alpha)
    else:
        # Fallback for unknown geometry types
        x, y = poly.exterior.xy
        if filled:
            ax.fill(x, y, alpha=alpha)
        ax.plot(x, y)


def plot_alternative(parcel: Polygon, alternative: dict) -> None:
    """Simple matplotlib preview of a generated alternative."""
    fig, ax = plt.subplots()

    _plot_polygon(ax, parcel, filled=False)

    # Plot each building footprint from the new schema
    buildings = alternative.get("buildings", [])
    for b in buildings:
        fp = b.get("footprint")
        _plot_polygon(ax, fp, filled=True, alpha=0.4)

    ax.set_aspect("equal", "box")
    ax.set_xlabel("X [m]")
    ax.set_ylabel("Y [m]")
    ax.set_title("Parcel design alternative")

    plt.show()
