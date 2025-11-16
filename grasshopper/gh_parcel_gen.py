"""GhPython script for parcel massing alternatives.

Inputs
------
Boundary : Rhino.Geometry.Curve
    Parcel boundary curve (closed, planar).
Typology : str
    One of: "Point", "Slab", "L", "U", "O" (case-insensitive).
GFA : float
    Target gross floor area in m².
FAR : float
    Target FAR; if 0 or negative, it will be derived from GFA / parcel area.
Floors_min : float
    Minimum average floors.
Floors_max : float
    Maximum average floors.
Seed : int
    Random seed.

Outputs
-------
Footprints : list[Rhino.Geometry.Curve]
Masses : list[Rhino.Geometry.Brep]
Heights : list[float]
Metrics : str
"""

import math
import random
import Rhino.Geometry as rg

# --- helpers --------------------------------------------------------------

def parcel_area_from_curve(curve):
    amp = rg.AreaMassProperties.Compute(curve)
    return amp.Area if amp else 0.0


def make_prototype_curve(typology):
    pl = rg.Plane.WorldXY

    def rect(w, h, x0=0.0, y0=0.0):
        rect3d = rg.Rectangle3d(pl, rg.Point3d(x0, y0, 0), rg.Point3d(x0 + w, y0 + h, 0))
        return rect3d.ToNurbsCurve()

    t = typology.lower()
    if t == "point":
        return rect(1.0, 1.0)
    if t == "slab":
        return rect(4.0, 1.0)
    if t == "l":
        r1 = rect(0.6, 1.0, 0.0, 0.0)
        r2 = rect(1.0, 0.4, 0.0, 0.0)
        union = rg.Curve.CreateBooleanUnion([r1, r2])
        return union[0] if union else r1
    if t == "u":
        left = rect(0.3, 1.0, 0.0, 0.0)
        right = rect(0.3, 1.0, 0.7, 0.0)
        bottom = rect(0.4, 0.3, 0.3, 0.0)
        union = rg.Curve.CreateBooleanUnion([left, right, bottom])
        return union[0] if union else left
    if t == "o":
        outer = rect(1.0, 1.0, 0.0, 0.0)
        inner = rect(0.4, 0.4, 0.3, 0.3)
        diff = rg.Curve.CreateBooleanDifference(outer, inner)
        return diff[0] if diff else outer

    # fallback
    return rect(1.0, 1.0)


def scale_curve_to_area(curve, target_area):
    if target_area <= 0:
        return curve

    amp = rg.AreaMassProperties.Compute(curve)
    if not amp or amp.Area <= 0:
        return curve

    factor = math.sqrt(target_area / amp.Area)
    xform = rg.Transform.Scale(rg.Point3d.Origin, factor)
    c = curve.DuplicateCurve()
    c.Transform(xform)
    return c


def place_curve_in_boundary(curve, boundary, rng, max_attempts=200):
    plane = rg.Plane.WorldXY
    bb_site = boundary.GetBoundingBox(True)

    for _ in range(max_attempts):
        angle_deg = rng.uniform(0.0, 360.0)
        angle_rad = math.radians(angle_deg)
        c = curve.DuplicateCurve()

        # rotate around origin
        xrot = rg.Transform.Rotation(angle_rad, plane.ZAxis, rg.Point3d.Origin)
        c.Transform(xrot)

        # pick random point in site bounding box
        x = rng.uniform(bb_site.Min.X, bb_site.Max.X)
        y = rng.uniform(bb_site.Min.Y, bb_site.Max.Y)
        target_pt = rg.Point3d(x, y, 0.0)

        amp = rg.AreaMassProperties.Compute(c)
        centroid = amp.Centroid if amp else rg.Point3d.Origin
        move_vec = target_pt - centroid
        xmove = rg.Transform.Translation(move_vec)
        c.Transform(xmove)

        rel = rg.Curve.PlanarClosedCurveRelationship(c, Boundary, plane, 1e-3)
        if rel == rg.RegionContainment.AInsideB:
            return c

    return None


# --- main -----------------------------------------------------------------

Footprints = []
Masses = []
Heights = []
Metrics = ""

if Boundary is None:
    return

rng = random.Random(Seed)
parcel_area = parcel_area_from_curve(Boundary)

if parcel_area <= 0:
    Metrics = "Invalid parcel area."
    return

# derive / check GFA & FAR
if GFA <= 0 and FAR > 0:
    GFA = FAR * parcel_area
elif GFA > 0 and FAR <= 0:
    FAR = GFA / parcel_area
elif GFA <= 0 and FAR <= 0:
    Metrics = "Please provide either GFA or FAR."
    return

floors_min = max(1.0, Floors_min)
floors_max = max(floors_min, Floors_max)
avg_floors = 0.5 * (floors_min + floors_max)
floor_to_floor = 3.2
height = avg_floors * floor_to_floor

total_fp_area = GFA / avg_floors

proto = make_prototype_curve(Typology)
building_curve = scale_curve_to_area(proto, total_fp_area)
placed = place_curve_in_boundary(building_curve, Boundary, rng)

if placed is None:
    Metrics = "Failed to place building inside parcel."
    return

Footprints.append(placed)
Heights.append(height)

# simple extrusion for mass
dir_vec = rg.Vector3d(0, 0, height)
breps = rg.Brep.CreateFromExtrusion(placed, dir_vec, True)
if breps:
    Masses.extend(breps)

# recompute actual footprint & metrics
amp_fp = rg.AreaMassProperties.Compute(placed)
actual_fp_area = amp_fp.Area if amp_fp else total_fp_area
actual_gfa = actual_fp_area * avg_floors
actual_far = actual_gfa / parcel_area
scr = actual_fp_area / parcel_area

lines = []
lines.append("Parcel area   : {:.2f} m²".format(parcel_area))
lines.append("Target GFA    : {:.2f} m²".format(GFA))
lines.append("Actual GFA    : {:.2f} m²".format(actual_gfa))
lines.append("Target FAR    : {:.2f}".format(FAR))
lines.append("Actual FAR    : {:.2f}".format(actual_far))
lines.append("Avg floors    : {:.2f}".format(avg_floors))
lines.append("Height        : {:.2f} m".format(height))
lines.append("SCR           : {:.2f}".format(scr))

Metrics = "\n".join(lines)
