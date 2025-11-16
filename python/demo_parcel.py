from shapely.geometry import Polygon

from generator import Typology, generate_alternative
from parcel_targets import ParcelTargets
from utils import plot_alternative


def main() -> None:
    # Example rectangular parcel
    parcel = Polygon([(0, 0), (120, 0), (120, 80), (0, 80)])

    # Targets similar to the UI screenshot
    targets = ParcelTargets(
        parcel_area=parcel.area,
        gfa=88756.0,
        far=3.0,
        floors_min=5.0,
        floors_max=12.0,
    )

    alt = generate_alternative(
        site_poly=parcel,
        typology=Typology.POINT,
        targets=targets,
        n_buildings=1,
        seed=42,
    )

    print("Metrics:")
    for k, v in alt["metrics"].items():
        print(f"  {k}: {v}")

    # Visualise with matplotlib
    plot_alternative(parcel, alt)


if __name__ == "__main__":
    main()
