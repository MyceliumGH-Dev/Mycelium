""" DOCUMENTATION: https://github.com/kunifujiwara/VoxCity?tab=readme-ov-file """

""" SETUP (required once per machine)
1. In terminal:
>>> pip install earthengine-api

2. Make sure you have a Google Earth Engine account: https://earthengine.google.com/signup

3. In terminal:
>>> earthengine authenticate
    - Sign in w/ Google account
    - If a verification code is shown, copy it and paste it back into the terminal.

Run script
"""

import ee
from voxcity.generator import get_voxcity

ee.Initialize(project='generative-design-478717')

# Optional arguments
kwargs = {
    "output_dir": "voxcity_output",   # Directory to save output files
    "dem_interpolation": False # Enable DEM interpolation
}

# Corner coordinates of grid (required)
rectangle_vertices = [
    (-122.33587348582083, 47.59830044521263),  # Southwest corner (longitude, latitude)
    (-122.33587348582083, 47.60279755390168),  # Northwest corner (longitude, latitude) 
    (-122.32922451417917, 47.60279755390168),  # Northeast corner (longitude, latitude)
    (-122.32922451417917, 47.59830044521263)   # Southeast corner (longitude, latitude)
]

meshsize = 5  # Grid cell size in meters (higher is coarser) (required)

# Get voxcity object (output)
voxcity = get_voxcity(
    rectangle_vertices,
    meshsize,
    building_source='OpenStreetMap',
    land_cover_source='OpenStreetMap',
    dem_source='DeltaDTM',
    **kwargs
)