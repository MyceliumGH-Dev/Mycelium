"""GhPython script for parcel massing alternatives.

Inputs
------
Boundary : Rhino.Geometry.Curve
    Parcel boundary curve (closed, planar).
Setback : float
    Distance from parcel edge to building face.
BuildingDepth : float
    Depth of the building wing.
MinFootprintArea : float
    Minimum buildable footprint area (after setback) in m². Parcels smaller than this will be skipped.
GenerateFloorSlabs : bool
    If True, generates individual floor slab geometry (may overlap with Masses).
Floors_min : float
    Minimum average floors.
Floors_max : float
    Maximum average floors.
BuildingTypes : list[str]
    List of allowed building types: 'courtyard', 'linear', 'point', 'l-shape', 'u-shape'.
NumParks : int
    Number of parcels to reserve as parks.
FloorHeight : float
    Floor-to-floor height in meters (default: 3.2).
Seed : int
    Random seed.

Outputs
-------
Footprints : list[Rhino.Geometry.Curve]
Masses : list[Rhino.Geometry.Brep]
Heights : list[float]
Metrics : str
Streets : list[Rhino.Geometry.Curve]
FloorSlabs : list[Rhino.Geometry.Brep]
Parks : list[Rhino.Geometry.Curve]
Trees : list[Rhino.Geometry.Brep]
Parcels : list[Rhino.Geometry.Curve]
    Building parcels (excludes parks)
"""

import math
import random
import System
import Rhino
import Rhino.Geometry as rg

# --- helpers --------------------------------------------------------------

def coerce_curve(obj):
    if isinstance(obj, rg.Curve):
        return obj
    if isinstance(obj, System.Guid):
        doc_obj = Rhino.RhinoDoc.ActiveDoc.Objects.FindId(obj)
        if doc_obj and isinstance(doc_obj.Geometry, rg.Curve):
            return doc_obj.Geometry
    return None


def parcel_area_from_curve(curve):
    amp = rg.AreaMassProperties.Compute(curve)
    return amp.Area if amp else 0.0


def generate_perimeter_block(curve, setback, depth):
    plane = rg.Plane.WorldXY
    footprints = []
    
    # 1. Offset for Setback (Inner offset)
    # Ensure curve is CCW
    if curve.ClosedCurveOrientation(plane) == rg.CurveOrientation.Clockwise:
        curve.Reverse()
        
    # Offset Inwards for Setback
    outer_offsets = curve.Offset(plane, -setback, 1e-3, rg.CurveOffsetCornerStyle.Sharp)
    
    if not outer_offsets:
        return []
        
    for outer_curve in outer_offsets:
        # Check if valid and closed
        if not outer_curve.IsClosed:
            outer_curve.MakeClosed(1e-3)
            
        # 2. Offset for Building Depth (Inner offset from outer_curve)
        # Check if there's enough room for a courtyard
        # Estimate: get bounding box and check if we have room for setback + depth on both sides
        success, check_plane = outer_curve.TryGetPlane()
        if not success: check_plane = rg.Plane.WorldXY
        
        outer_bbox = outer_curve.GetBoundingBox(check_plane)
        min_dimension = min(outer_bbox.Max.X - outer_bbox.Min.X, outer_bbox.Max.Y - outer_bbox.Min.Y)
        
        # Need at least 2*depth for a courtyard to make sense (depth on each side)
        # Plus some minimum courtyard size
        min_courtyard_width = depth  # Courtyard should be at least as wide as building depth
        required_width = 2 * depth + min_courtyard_width
        
        block_parts = []
        
        if min_dimension > required_width:
            # Parcel is large enough for a courtyard
            inner_offsets = outer_curve.Offset(plane, -depth, 1e-3, rg.CurveOffsetCornerStyle.Sharp)
        else:
            # Too small for courtyard - fall back to point block
            return generate_point_block(curve, setback, depth)
        
        if inner_offsets:
            # We have potential courtyards
            # Prepare outer curve for boolean op
            if outer_curve.ClosedCurveOrientation(plane) == rg.CurveOrientation.Clockwise:
                outer_curve.Reverse()
                
            # Prepare inner curves
            valid_inner = []
            for ic in inner_offsets:
                if not ic.IsClosed:
                    ic.MakeClosed(1e-3)
                if ic.ClosedCurveOrientation(plane) == rg.CurveOrientation.Clockwise:
                    ic.Reverse()
                valid_inner.append(ic)
                
            # Boolean Difference: Outer - Inner
            if valid_inner:
                diff = rg.Curve.CreateBooleanDifference(outer_curve, valid_inner)
                if diff:
                    block_parts.extend(diff)
            else:
                block_parts.append(outer_curve)
        else:
            # No inner offset possible (too small for courtyard), use solid block
            block_parts.append(outer_curve)
            
        # Fallback if boolean diff failed but we had inner offsets (shouldn't happen often)
        if not block_parts and not inner_offsets:
             block_parts.append(outer_curve)
             
        footprints.extend(block_parts)
        
    return footprints


def generate_linear_block(curve, setback, depth):
    plane = rg.Plane.WorldXY
    footprints = []
    
    # 1. Offset for Setback (Inner offset)
    if curve.ClosedCurveOrientation(plane) == rg.CurveOrientation.Clockwise:
        curve.Reverse()
        
    # Offset Inwards for Setback to get buildable area
    buildable_offsets = curve.Offset(plane, -setback, 1e-3, rg.CurveOffsetCornerStyle.Sharp)
    
    if not buildable_offsets:
        return []
        
    for buildable_curve in buildable_offsets:
        if not buildable_curve.IsClosed:
            buildable_curve.MakeClosed(1e-3)

        # 2. Oriented Bounding Box
        success, obb_plane = buildable_curve.TryGetPlane()
        if not success: obb_plane = rg.Plane.WorldXY
        
        # Transform to plane to get bbox
        xform = rg.Transform.PlaneToPlane(obb_plane, rg.Plane.WorldXY)
        c_local = buildable_curve.DuplicateCurve()
        c_local.Transform(xform)
        bbox = c_local.GetBoundingBox(True)
        
        width = bbox.Max.X - bbox.Min.X
        height = bbox.Max.Y - bbox.Min.Y
        
        # Create bar along longest axis
        # Center of bbox
        center = bbox.Center
        
        if width > height:
            # Horizontal bar
            bar_len = width
            bar_wid = min(height, depth)
            rect = rg.Rectangle3d(rg.Plane.WorldXY, 
                                  rg.Point3d(bbox.Min.X, center.Y - bar_wid/2, 0),
                                  rg.Point3d(bbox.Max.X, center.Y + bar_wid/2, 0))
        else:
            # Vertical bar
            bar_len = height
            bar_wid = min(width, depth)
            rect = rg.Rectangle3d(rg.Plane.WorldXY, 
                                  rg.Point3d(center.X - bar_wid/2, bbox.Min.Y, 0),
                                  rg.Point3d(center.X + bar_wid/2, bbox.Max.Y, 0))
                                  
        bar_curve = rect.ToNurbsCurve()
        
        # Transform back
        xform_back = rg.Transform.PlaneToPlane(rg.Plane.WorldXY, obb_plane)
        bar_curve.Transform(xform_back)
        
        # Intersect with buildable area to trim ends if non-rectangular
        # Boolean Intersection
        intersection = rg.Curve.CreateBooleanIntersection(buildable_curve, bar_curve)
        
        if intersection:
            footprints.extend(intersection)
        else:
            # Fallback if intersection fails (e.g. perfectly coincident or tolerance issues)
            # Just use the bar if it's mostly inside? Or skip.
            # Let's try to use the bar curve itself if it's valid, but strictly it should be inside.
            # If intersection failed, it might be that they don't overlap.
            pass
            
    return footprints


def generate_point_block(curve, setback, depth):
    plane = rg.Plane.WorldXY
    footprints = []
    
    # 1. Offset for Setback
    if curve.ClosedCurveOrientation(plane) == rg.CurveOrientation.Clockwise:
        curve.Reverse()
        
    buildable_offsets = curve.Offset(plane, -setback, 1e-3, rg.CurveOffsetCornerStyle.Sharp)
    
    if not buildable_offsets:
        return []
        
    for buildable_curve in buildable_offsets:
        if not buildable_curve.IsClosed:
            buildable_curve.MakeClosed(1e-3)
            
        # Get OBB
        success, obb_plane = buildable_curve.TryGetPlane()
        if not success: obb_plane = rg.Plane.WorldXY
        
        xform = rg.Transform.PlaneToPlane(obb_plane, rg.Plane.WorldXY)
        c_local = buildable_curve.DuplicateCurve()
        c_local.Transform(xform)
        bbox = c_local.GetBoundingBox(True)
        
        center = bbox.Center
        
        # Point block size
        # Let's make it square, side = 1.5 * depth (or just depth?)
        # User asked for "point", usually implies a tower.
        side = depth * 1.5
        
        # Check if fits
        width = bbox.Max.X - bbox.Min.X
        height = bbox.Max.Y - bbox.Min.Y
        
        side = min(side, width, height)
        
        rect = rg.Rectangle3d(rg.Plane.WorldXY, 
                              rg.Point3d(center.X - side/2, center.Y - side/2, 0),
                              rg.Point3d(center.X + side/2, center.Y + side/2, 0))
                              
        point_curve = rect.ToNurbsCurve()
        
        xform_back = rg.Transform.PlaneToPlane(rg.Plane.WorldXY, obb_plane)
        point_curve.Transform(xform_back)
        
        # Intersection to ensure it stays inside boundary
        intersection = rg.Curve.CreateBooleanIntersection(buildable_curve, point_curve)
        
        if intersection:
            footprints.extend(intersection)
            
    return footprints


def generate_trees(curve, rng):
    trees = []
    
    # Calculate Area for density
    amp = rg.AreaMassProperties.Compute(curve)
    if not amp: return []
    area = amp.Area
    
    # Density: 1 tree per 50 m2
    num_trees = int(area / 50.0)
    if num_trees < 1: num_trees = 1
    
    # Get OBB for random point generation
    success, plane = curve.TryGetPlane()
    if not success: plane = rg.Plane.WorldXY
    
    xform = rg.Transform.PlaneToPlane(plane, rg.Plane.WorldXY)
    c_local = curve.DuplicateCurve()
    c_local.Transform(xform)
    bbox = c_local.GetBoundingBox(True)
    
    min_x, max_x = bbox.Min.X, bbox.Max.X
    min_y, max_y = bbox.Min.Y, bbox.Max.Y
    
    # Generate points
    # Attempt 2x num_trees to account for rejection sampling
    attempts = num_trees * 3
    count = 0
    
    for _ in range(attempts):
        if count >= num_trees:
            break
            
        x = rng.uniform(min_x, max_x)
        y = rng.uniform(min_y, max_y)
        pt_local = rg.Point3d(x, y, 0)
        
        # Check containment in local
        if c_local.Contains(pt_local, rg.Plane.WorldXY, 1e-3) == rg.PointContainment.Inside:
            # Transform back
            xform_back = rg.Transform.PlaneToPlane(rg.Plane.WorldXY, plane)
            pt_world = pt_local
            pt_world.Transform(xform_back)
            
            # Random radius 1.0 to 2.5 (Diameter 2-5)
            r = rng.uniform(1.0, 2.5)
            
            # Move center up by r so it sits on ground
            center = pt_world + rg.Vector3d(0, 0, r)
            
            sphere = rg.Sphere(center, r)
            trees.append(sphere.ToBrep())
            count += 1
            
    return trees


def generate_l_shape(curve, setback, depth, rng):
    # Direct construction approach - build L from two rectangles
    plane = rg.Plane.WorldXY
    
    # 1. Offset for Setback
    if curve.ClosedCurveOrientation(plane) == rg.CurveOrientation.Clockwise:
        curve.Reverse()
    
    buildable_offsets = curve.Offset(plane, -setback, 1e-3, rg.CurveOffsetCornerStyle.Sharp)
    if not buildable_offsets:
        return []
    
    buildable = buildable_offsets[0]
    if not buildable.IsClosed:
        buildable.MakeClosed(1e-3)
    
    # 2. Get OBB
    success, obb_plane = buildable.TryGetPlane()
    if not success: obb_plane = rg.Plane.WorldXY
    
    xform = rg.Transform.PlaneToPlane(obb_plane, rg.Plane.WorldXY)
    c_local = buildable.DuplicateCurve()
    c_local.Transform(xform)
    bbox = c_local.GetBoundingBox(True)
    
    min_x, max_x = bbox.Min.X, bbox.Max.X
    min_y, max_y = bbox.Min.Y, bbox.Max.Y
    
    # Use user's depth
    wing_depth = depth
    
    # 3. Create two rectangles forming an L
    config = rng.randint(0, 3)
    
    rects = []
    if config == 0:  # Bottom-Left
        # Horizontal wing (bottom)
        r1 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, min_y, 0),
            rg.Point3d(max_x, min_y + wing_depth, 0))
        # Vertical wing (left)
        r2 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, min_y, 0),
            rg.Point3d(min_x + wing_depth, max_y, 0))
        rects = [r1.ToNurbsCurve(), r2.ToNurbsCurve()]
    elif config == 1:  # Bottom-Right
        # Horizontal wing (bottom)
        r1 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, min_y, 0),
            rg.Point3d(max_x, min_y + wing_depth, 0))
        # Vertical wing (right)
        r2 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(max_x - wing_depth, min_y, 0),
            rg.Point3d(max_x, max_y, 0))
        rects = [r1.ToNurbsCurve(), r2.ToNurbsCurve()]
    elif config == 2:  # Top-Right
        # Horizontal wing (top)
        r1 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, max_y - wing_depth, 0),
            rg.Point3d(max_x, max_y, 0))
        # Vertical wing (right)
        r2 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(max_x - wing_depth, min_y, 0),
            rg.Point3d(max_x, max_y, 0))
        rects = [r1.ToNurbsCurve(), r2.ToNurbsCurve()]
    else:  # Top-Left
        # Horizontal wing (top)
        r1 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, max_y - wing_depth, 0),
            rg.Point3d(max_x, max_y, 0))
        # Vertical wing (left)
        r2 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, min_y, 0),
            rg.Point3d(min_x + wing_depth, max_y, 0))
        rects = [r1.ToNurbsCurve(), r2.ToNurbsCurve()]
    
    # Union rectangles to create single L-shaped footprint
    unified_rects = rg.Curve.CreateBooleanUnion(rects)
    if not unified_rects:
        return []
    
    # Transform back
    xform_back = rg.Transform.PlaneToPlane(rg.Plane.WorldXY, obb_plane)
    unified_world = []
    for u in unified_rects:
        u.Transform(xform_back)
        unified_world.append(u)
    
    # Intersect unified shape with buildable area
    final_footprints = []
    for u in unified_world:
        ints = rg.Curve.CreateBooleanIntersection(u, buildable)
        if ints:
            final_footprints.extend(ints)
    
    return final_footprints


def generate_u_shape(curve, setback, depth, rng):
    # Direct construction - build U from three rectangles
    plane = rg.Plane.WorldXY
    
    # 1. Offset for Setback
    if curve.ClosedCurveOrientation(plane) == rg.CurveOrientation.Clockwise:
        curve.Reverse()
    
    buildable_offsets = curve.Offset(plane, -setback, 1e-3, rg.CurveOffsetCornerStyle.Sharp)
    if not buildable_offsets:
        return []
    
    buildable = buildable_offsets[0]
    if not buildable.IsClosed:
        buildable.MakeClosed(1e-3)
    
    # 2. Check if parcel is large enough for U-shape
    # U-shape needs 3 wings, minimum dimensions needed
    success, obb_plane = buildable.TryGetPlane()
    if not success: obb_plane = rg.Plane.WorldXY
    
    xform = rg.Transform.PlaneToPlane(obb_plane, rg.Plane.WorldXY)
    c_local = buildable.DuplicateCurve()
    c_local.Transform(xform)
    bbox = c_local.GetBoundingBox(True)
    
    width = bbox.Max.X - bbox.Min.X
    height = bbox.Max.Y - bbox.Min.Y
    min_dimension = min(width, height)
    
    # U-shape needs room for 2 parallel wings plus middle opening
    # Minimum: 3*depth (wing + opening + wing)
    if min_dimension < 3 * depth:
        # Too small for U-shape - fall back to linear block
        return generate_linear_block(curve, setback, depth)
    
    min_x, max_x = bbox.Min.X, bbox.Max.X
    min_y, max_y = bbox.Min.Y, bbox.Max.Y
    
    wing_depth = depth
    
    # 3. Create three rectangles forming a U
    config = rng.randint(0, 3)
    
    rects = []
    if config == 0:  # Open Top (bottom + left + right)
        r1 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, min_y, 0),
            rg.Point3d(max_x, min_y + wing_depth, 0)).ToNurbsCurve() # bottom
        r2 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, min_y, 0),
            rg.Point3d(min_x + wing_depth, max_y, 0)).ToNurbsCurve() # left
        r3 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(max_x - wing_depth, min_y, 0),
            rg.Point3d(max_x, max_y, 0)).ToNurbsCurve() # right
        rects = [r1, r2, r3]
    elif config == 1:  # Open Right (top + bottom + left)
        r1 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, max_y - wing_depth, 0),
            rg.Point3d(max_x, max_y, 0)).ToNurbsCurve() # top
        r2 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, min_y, 0),
            rg.Point3d(max_x, min_y + wing_depth, 0)).ToNurbsCurve() # bottom
        r3 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, min_y, 0),
            rg.Point3d(min_x + wing_depth, max_y, 0)).ToNurbsCurve() # left
        rects = [r1, r2, r3]
    elif config == 2:  # Open Bottom (top + left + right)
        r1 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, max_y - wing_depth, 0),
            rg.Point3d(max_x, max_y, 0)).ToNurbsCurve() # top
        r2 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, min_y, 0),
            rg.Point3d(min_x + wing_depth, max_y, 0)).ToNurbsCurve() # left
        r3 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(max_x - wing_depth, min_y, 0),
            rg.Point3d(max_x, max_y, 0)).ToNurbsCurve() # right
        rects = [r1, r2, r3]
    else:  # Open Left (top + bottom + right)
        r1 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, max_y - wing_depth, 0),
            rg.Point3d(max_x, max_y, 0)).ToNurbsCurve() # top
        r2 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(min_x, min_y, 0),
            rg.Point3d(max_x, min_y + wing_depth, 0)).ToNurbsCurve() # bottom
        r3 = rg.Rectangle3d(rg.Plane.WorldXY,
            rg.Point3d(max_x - wing_depth, min_y, 0),
            rg.Point3d(max_x, max_y, 0)).ToNurbsCurve() # right
        rects = [r1, r2, r3]
    
    # Union rectangles to create single U-shaped footprint
    unified_rects = rg.Curve.CreateBooleanUnion(rects)
    if not unified_rects:
        return []
    
    # Transform back
    xform_back = rg.Transform.PlaneToPlane(rg.Plane.WorldXY, obb_plane)
    unified_world = []
    for u in unified_rects:
        u.Transform(xform_back)
        unified_world.append(u)
    
    # Intersect unified shape with buildable area
    final_footprints = []
    for u in unified_world:
        ints = rg.Curve.CreateBooleanIntersection(u, buildable)
        if ints:
            final_footprints.extend(ints)
    
    return final_footprints


def subdivide_parcel(curve, depth, min_area, street_width, rng):
    if depth <= 0:
        return [curve]

    amp = rg.AreaMassProperties.Compute(curve)
    if not amp or amp.Area < min_area:
        return [curve]

    # Oriented Bounding Box
    success, plane = curve.TryGetPlane()
    if not success:
        plane = rg.Plane.WorldXY
    
    # Get OBB in plane coordinates
    # We can use GetBoundingBox(Plane) if available, or transform to plane
    xform_to_plane = rg.Transform.PlaneToPlane(plane, rg.Plane.WorldXY)
    c_local = curve.DuplicateCurve()
    c_local.Transform(xform_to_plane)
    bbox = c_local.GetBoundingBox(True)
    
    width = bbox.Max.X - bbox.Min.X
    height = bbox.Max.Y - bbox.Min.Y
    
    # Decide split axis (split the longer side)
    split_horiz = width < height
    
    # Random split parameter (0.4 to 0.6)
    t = rng.uniform(0.4, 0.6)
    
    # Create cutter line in local coords
    if split_horiz:
        y_split = bbox.Min.Y + height * t
        p0 = rg.Point3d(bbox.Min.X - 10, y_split, 0)
        p1 = rg.Point3d(bbox.Max.X + 10, y_split, 0)
    else:
        x_split = bbox.Min.X + width * t
        p0 = rg.Point3d(x_split, bbox.Min.Y - 10, 0)
        p1 = rg.Point3d(x_split, bbox.Max.Y + 10, 0)
        
    cutter_line = rg.LineCurve(p0, p1)
    
    # Create street (offset cutter)
    street_half = street_width * 0.5
    if split_horiz:
        # Horizontal street
        rect = rg.Rectangle3d(rg.Plane.WorldXY, 
                              rg.Point3d(bbox.Min.X - 100, y_split - street_half, 0),
                              rg.Point3d(bbox.Max.X + 100, y_split + street_half, 0))
    else:
        # Vertical street
        rect = rg.Rectangle3d(rg.Plane.WorldXY, 
                              rg.Point3d(x_split - street_half, bbox.Min.Y - 100, 0),
                              rg.Point3d(x_split + street_half, bbox.Max.Y + 100, 0))
                              
    street_curve = rect.ToNurbsCurve()
    
    # Transform street back to world
    xform_to_world = rg.Transform.PlaneToPlane(rg.Plane.WorldXY, plane)
    street_curve.Transform(xform_to_world)
    
    # Boolean difference
    # CurveBooleanDifference takes lists
    diff = rg.Curve.CreateBooleanDifference(curve, street_curve)
    
    if not diff or len(diff) < 2:
        # Split failed or didn't create 2 parts, return original
        return [curve]
        
    # Recurse on children
    results = []
    for d in diff:
        results.extend(subdivide_parcel(d, depth - 1, min_area, street_width, rng))
        
    return results


# --- main -----------------------------------------------------------------

def main(Boundary, Floors_min, Floors_max, Seed, StreetWidth, MinFootprintArea, Divisions, Setback, BuildingDepth, GenerateFloorSlabs, BuildingTypes, NumParks, FloorHeight):
    Footprints = []
    Masses = []
    Heights = []
    Streets = []
    FloorSlabs = []
    Parks = []
    Trees = []
    Parcels = []  # Building parcels (excluding parks)
    Metrics = ""

    if Boundary is None:
        return Footprints, Masses, Heights, Metrics, Streets, FloorSlabs, Parks, Trees, Parcels

    boundary_curve = coerce_curve(Boundary)
    if boundary_curve is None:
        Metrics = "Input 'Boundary' is not a valid Curve."
        return Footprints, Masses, Heights, Metrics, Streets, FloorSlabs, Parks, Trees, Parcels

    rng = random.Random(Seed)
    
    # handle defaults for optional inputs
    if Divisions is None: Divisions = 0
    if StreetWidth is None: StreetWidth = 5.0
    if MinFootprintArea is None: MinFootprintArea = 100.0
    if Setback is None: Setback = 3.0
    if BuildingDepth is None: BuildingDepth = 12.0
    if GenerateFloorSlabs is None: GenerateFloorSlabs = False
    if BuildingTypes is None or len(BuildingTypes) == 0: BuildingTypes = ["courtyard"]
    if NumParks is None: NumParks = 0
    if FloorHeight is None: FloorHeight = 3.2
    
    # Normalize types to lowercase
    allowed_types = [t.lower() for t in BuildingTypes]

    # 1. Subdivide Parcel
    parcels = subdivide_parcel(boundary_curve, Divisions, MinFootprintArea, StreetWidth, rng)
    
    # Calculate Streets (Original - Parcels)
    # We use a tolerance slightly larger than model tolerance
    streets_diff = rg.Curve.CreateBooleanDifference(boundary_curve, parcels, 1e-3)
    if streets_diff:
        Streets.extend(streets_diff)
    
    # Select Park Indices
    num_parcels = len(parcels)
    park_indices = set()
    if NumParks > 0 and num_parcels > 0:
        # Ensure we don't ask for more parks than parcels
        n_parks = min(NumParks, num_parcels)
        park_indices = set(rng.sample(range(num_parcels), n_parks))
    
    total_generated_gfa = 0.0
    
    # 2. Generate Building for each parcel
    for i, p_curve in enumerate(parcels):
        # Check if Park
        if i in park_indices:
            Parks.append(p_curve)
            # Generate Trees
            park_trees = generate_trees(p_curve, rng)
            Trees.extend(park_trees)
            continue
        
        # Add to building parcels
        Parcels.append(p_curve)
        
        # Check minimum footprint area (buildable area after setback)
        # This is more accurate than checking raw parcel area
        plane = rg.Plane.WorldXY
        if p_curve.ClosedCurveOrientation(plane) == rg.CurveOrientation.Clockwise:
            p_curve.Reverse()
        
        buildable_offsets = p_curve.Offset(plane, -Setback, 1e-3, rg.CurveOffsetCornerStyle.Sharp)
        if not buildable_offsets:
            continue  # No buildable area after setback
        
        buildable_area = 0.0
        for bo in buildable_offsets:
            amp_b = rg.AreaMassProperties.Compute(bo)
            if amp_b:
                buildable_area += amp_b.Area
        
        if buildable_area < MinFootprintArea:
            continue  # Footprint too small
            
        # Pick a random type
        b_type = rng.choice(allowed_types)
        
        block_footprints = []
        
        if b_type == "linear":
            block_footprints = generate_linear_block(p_curve, Setback, BuildingDepth)
        elif b_type == "point":
            block_footprints = generate_point_block(p_curve, Setback, BuildingDepth)
        elif b_type == "l-shape":
            block_footprints = generate_l_shape(p_curve, Setback, BuildingDepth, rng)
        elif b_type == "u-shape":
            block_footprints = generate_u_shape(p_curve, Setback, BuildingDepth, rng)
        else:
            # Default to courtyard
            block_footprints = generate_perimeter_block(p_curve, Setback, BuildingDepth)
        
        if block_footprints:
            Footprints.extend(block_footprints)
            
            # Random Height (same for all parts of this block)
            floors_min = max(1.0, Floors_min)
            floors_max = max(floors_min, Floors_max)
            avg_floors = rng.uniform(floors_min, floors_max)
            height = avg_floors * FloorHeight
            
            Heights.extend([height] * len(block_footprints))

            # Process all footprints together for this parcel (handles courtyards with holes)
            # Ensure all curves are CCW oriented
            for fp in block_footprints:
                if fp.ClosedCurveOrientation(rg.Plane.WorldXY) == rg.CurveOrientation.Clockwise:
                    fp.Reverse()
            
            # Create planar Breps from all curves at once (preserves holes)
            if block_footprints:
                planar_breps = rg.Brep.CreatePlanarBreps(block_footprints, 1e-3)
                if planar_breps and len(planar_breps) > 0:
                    # Extrude each planar surface (usually just one for courtyard)
                    for base_brep in planar_breps:
                        vec = rg.Vector3d(0, 0, height)
                        extruded_brep = base_brep.Faces[0].CreateExtrusion(
                            rg.LineCurve(rg.Point3d.Origin, rg.Point3d.Origin + vec).ToNurbsCurve(), 
                            True
                        )
                        if extruded_brep:
                            Masses.append(extruded_brep)
                else:
                    # Fallback: extrude each curve individually
                    for fp in block_footprints:
                        extrusion = rg.Extrusion.Create(fp, height, True)
                        if extrusion:
                            Masses.append(extrusion.ToBrep(False))
                
                # Generate Floor Slabs (Optional)
                if GenerateFloorSlabs:
                    num_floors = int(round(avg_floors))
                    for i in range(num_floors):
                        z_level = i * floor_to_floor
                        # Move footprint to level
                        slab_crv = fp.DuplicateCurve()
                        slab_crv.Translate(rg.Vector3d(0, 0, z_level))
                        # Extrude up slightly to avoid going under the parcel
                        slab_ext = rg.Extrusion.Create(slab_crv, 0.3, True) # 30cm slab up
                        if slab_ext:
                            FloorSlabs.append(slab_ext.ToBrep(False))

                # Calculate GFA
                amp_fp = rg.AreaMassProperties.Compute(fp)
                fp_area = amp_fp.Area if amp_fp else 0.0
                total_generated_gfa += fp_area * avg_floors
    
    # Robustness: Attempt to Union Masses to prevent Z-fighting from duplicate inputs
    # REMOVED per user request to see subtractions clearly
    # if Masses:
    #     try:
    #         # CreateBooleanUnion returns None on failure, or a list of Breps
    #         merged_masses = rg.Brep.CreateBooleanUnion(Masses)
    #         if merged_masses:
    #             Masses = list(merged_masses)
    #     except:
    #         pass # Fallback to original masses if union fails

    # Robustness: Union Floor Slabs
    if FloorSlabs:
        try:
            merged_slabs = rg.Brep.CreateBooleanUnion(FloorSlabs)
            if merged_slabs:
                FloorSlabs = list(merged_slabs)
        except:
            pass

    # Metrics
    original_area = parcel_area_from_curve(boundary_curve)
    final_far = total_generated_gfa / original_area if original_area > 0 else 0
    
    # Advanced Metrics
    # Assumptions: GIA = 90% GFA, NIA = 80% GFA
    total_gia = total_generated_gfa * 0.90
    total_nia = total_generated_gfa * 0.80
    
    # Unit Mix Estimation (based on NIA)
    # Avg Unit Size ~ 75 m2
    total_units = int(total_nia / 75.0)
    
    # Mix: 15% Studio, 40% 1-Bed, 30% 2-Bed, 15% 3-Bed
    units_studio = int(total_units * 0.15)
    units_1bed = int(total_units * 0.40)
    units_2bed = int(total_units * 0.30)
    units_3bed = total_units - units_studio - units_1bed - units_2bed

    lines = []
    lines.append("--- Area Metrics ---")
    lines.append("Parcel Area   : {:,.0f} m²".format(original_area))
    lines.append("Total GFA     : {:,.0f} m²".format(total_generated_gfa))
    lines.append("Total GIA     : {:,.0f} m²".format(total_gia))
    lines.append("Total NIA     : {:,.0f} m²".format(total_nia))
    lines.append("FAR           : {:.2f}".format(final_far))
    lines.append("")
    lines.append("--- Quantities ---")
    lines.append("Parcels       : {}".format(len(parcels)))
    lines.append("Buildings     : {}".format(len(Masses)))
    lines.append("Parks         : {}".format(len(Parks)))
    lines.append("Trees         : {}".format(len(Trees)))
    lines.append("Total Units   : {}".format(total_units))
    lines.append("  Studios     : {}".format(units_studio))
    lines.append("  1-Bed       : {}".format(units_1bed))
    lines.append("  2-Bed       : {}".format(units_2bed))
    lines.append("  3-Bed       : {}".format(units_3bed))
    lines.append("")
    lines.append("NOTE: Masses have been automatically merged to prevent Z-fighting.")

    Metrics = "\n".join(lines)
    
    return Footprints, Masses, Heights, Metrics, Streets, FloorSlabs, Parks, Trees, Parcels

Footprints, Masses, Heights, Metrics, Streets, FloorSlabs, Parks, Trees, Parcels = main(Boundary, Floors_min, Floors_max, Seed, StreetWidth, MinFootprintArea, Divisions, Setback, BuildingDepth, GenerateFloorSlabs, BuildingTypes, NumParks, FloorHeight)
