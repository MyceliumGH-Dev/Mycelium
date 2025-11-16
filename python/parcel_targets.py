from __future__ import annotations

from dataclasses import dataclass
from typing import Tuple


@dataclass
class ParcelTargets:
    """Container for parcel-level design targets.

    Parameters
    ----------
    parcel_area : float
        Parcel / site area in m².
    gfa : float
        Total gross floor area target in m².
    far : float | None, optional
        Floor area ratio. If None, it will be computed from
        ``gfa / parcel_area``.
    floors_min : float, optional
        Minimum average number of floors.
    floors_max : float, optional
        Maximum average number of floors.
    floor_to_floor : float, optional
        Floor-to-floor height in meters, used to derive average height.
    """

    parcel_area: float
    gfa: float
    far: float | None = None
    floors_min: float = 5.0
    floors_max: float = 12.0
    floor_to_floor: float = 3.2

    def __post_init__(self) -> None:
        if self.parcel_area <= 0:
            raise ValueError("parcel_area must be positive")
        if self.gfa <= 0:
            raise ValueError("gfa must be positive")
        if self.floors_min <= 0 or self.floors_max <= 0:
            raise ValueError("floors_min/max must be positive")
        if self.floors_min > self.floors_max:
            raise ValueError("floors_min cannot exceed floors_max")

        if self.far is None:
            self.far = self.gfa / self.parcel_area
        else:
            # basic consistency check; we do not fail hard
            implied_far = self.gfa / self.parcel_area
            if abs(implied_far - self.far) > 0.1:
                # You can change this to a warning if desired.
                # For now we just overwrite to remain consistent.
                self.far = implied_far

    # --- helpers -----------------------------------------------------

    def footprint_range(self) -> Tuple[float, float]:
        """Return (A_fp_min, A_fp_max) in m².

        A_fp_max corresponds to using ``floors_min``,
        A_fp_min corresponds to using ``floors_max``.
        """
        a_fp_max = self.gfa / self.floors_min
        a_fp_min = self.gfa / self.floors_max
        return a_fp_min, a_fp_max

    def scr_range(self) -> Tuple[float, float]:
        """Return (SCR_min, SCR_max)."""
        a_fp_min, a_fp_max = self.footprint_range()
        return a_fp_min / self.parcel_area, a_fp_max / self.parcel_area

    def avg_floors(self) -> float:
        """Return simple mean of floors_min and floors_max."""
        return 0.5 * (self.floors_min + self.floors_max)

    def avg_height_range(self) -> Tuple[float, float]:
        """Return (H_min, H_max) in meters from floors_min/max."""
        return (
            self.floors_min * self.floor_to_floor,
            self.floors_max * self.floor_to_floor,
        )

    def floors_for_footprint(self, footprint_area: float) -> float:
        """Suggest floors given a footprint area in m²."""
        if footprint_area <= 0:
            raise ValueError("footprint_area must be positive")
        return self.gfa / footprint_area

    def height_for_floors(self, floors: float) -> float:
        """Return building height in meters for a given number of floors."""
        return floors * self.floor_to_floor
