# python/utils.py
from __future__ import annotations

import matplotlib.pyplot as plt
from shapely.geometry import Polygon
from shapely.geometry.base import BaseGeometry


def _plot_polygon(ax, poly: BaseGeometry, filled: bool = False, alpha: float = 0.2):
    if poly.is_empty:
        return
    if hasattr(poly, "geoms"):
        for g in poly.geoms:
            _plot_polygon(ax, g, filled, alpha)
        return

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
