# python/demo_parcel.py
from shapely.geometry import Polygon

from generator import Typology, generate_layout_from_location
from utils import plot_alternative


def main() -> None:
    # Example rectangular parcel (in local meters; you can sub in projected coords)
    parcel_vertices = [(0, 0), (120, 0), (120, 120), (0, 120)]

    alt = generate_layout_from_location(
        parcel_vertices=parcel_vertices,
        structure_type=Typology.POINT.value,
        n_buildings=10,
        far=3.0,
        floors_min=5.0,
        floors_max=12.0,
        floor_to_floor=4,
        seed=45,
    )

    parcel = Polygon(parcel_vertices)

    print("=== Parcel metrics ===")
    for k, v in alt["metrics"].items():
        print(f"  {k}: {v}")

    print("\n=== Buildings ===")
    for i, b in enumerate(alt["buildings"], start=1):
        cx, cy = b["centroid"]
        print(f"Building {i}:")
        print(f"  centroid        : ({cx:.2f}, {cy:.2f})")
        print(f"  length x width  : {b['length']:.2f} m x {b['width']:.2f} m")
        print(f"  floors          : {b['floors']}")
        print(f"  floor height    : {b['floor_height']:.2f} m")
        print(f"  total height    : {b['total_height']:.2f} m")

    # Visualise with matplotlib
    plot_alternative(parcel, alt)


if __name__ == "__main__":
    main()
