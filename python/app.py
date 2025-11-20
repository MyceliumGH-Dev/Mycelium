"""
FastAPI backend for the Generative Design Tool.
Exposes the layout generation API for web access.
"""
from typing import List, Tuple, Optional, Any, Dict
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from fastapi.responses import FileResponse
from pydantic import BaseModel, Field
import uvicorn
import os

from generator import Typology, generate_layout_from_location


# Request/Response Models
class GenerateRequest(BaseModel):
    """Request model for generating building layouts."""
    parcel_vertices: List[Tuple[float, float]] = Field(
        ..., 
        description="List of (x, y) coordinates defining the parcel boundary",
        example=[(0, 0), (50, 0), (50, 50), (0, 50)]
    )
    structure_type: str = Field(
        ..., 
        description="Building typology: 'point', 'linear', or 'courtyard'",
        example="point"
    )
    n_buildings: int = Field(
        default=5,
        ge=1,
        le=50,
        description="Number of buildings to place"
    )
    far: float = Field(
        default=2.0,
        ge=0.1,
        le=10.0,
        description="Floor area ratio (target)"
    )
    floors_min: float = Field(
        default=3.0,
        ge=1.0,
        description="Minimum number of floors"
    )
    floors_max: float = Field(
        default=10.0,
        ge=1.0,
        description="Maximum number of floors"
    )
    floor_to_floor: float = Field(
        default=3.5,
        ge=2.0,
        le=6.0,
        description="Height of each floor in meters"
    )
    seed: Optional[int] = Field(
        default=None,
        description="Random seed for reproducibility"
    )
    min_edge_buffer: Optional[float] = Field(
        default=None,
        ge=0.0,
        le=50.0,
        description="Parcel boundary setback in meters (default: 5.0m)"
    )
    min_building_buffer: Optional[float] = Field(
        default=None,
        ge=0.0,
        le=50.0,
        description="Minimum edge-to-edge separation between buildings in meters (linear: 3.0m, point: 14.0m)"
    )
    min_building_thickness: Optional[float] = Field(
        default=None,
        ge=0.0,
        le=50.0,
        description="Minimum building thickness in meters (courtyard only, default: 6.0m)"
    )


class BuildingResponse(BaseModel):
    """Response model for a single building."""
    footprint: List[Tuple[float, float]]
    centroid: Tuple[float, float]
    length: float
    width: float
    floors: int
    floor_height: float
    total_height: float


class GenerateResponse(BaseModel):
    """Response model for layout generation."""
    buildings: List[BuildingResponse]
    metrics: Dict[str, Any]
    typology: str


# Create FastAPI app
app = FastAPI(
    title="Generative Design API",
    description="Generate building massing alternatives for parcel boundaries",
    version="1.0.0"
)

# Configure CORS
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # In production, specify your frontend domain
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Mount static files
app.mount("/static", StaticFiles(directory="static"), name="static")


def serialize_polygon(polygon) -> List[Tuple[float, float]]:
    """Convert Shapely Polygon to list of coordinate tuples."""
    return list(polygon.exterior.coords[:-1])  # Exclude duplicate closing point


@app.get("/")
async def root():
    """Serve the frontend application."""
    static_path = os.path.join(os.path.dirname(__file__), "static", "index.html")
    if os.path.exists(static_path):
        return FileResponse(static_path)
    return {
        "message": "Generative Design API",
        "version": "1.0.0",
        "endpoints": {
            "/generate": "POST - Generate building layouts",
            "/typologies": "GET - List available typologies",
            "/docs": "API documentation"
        }
    }


@app.get("/typologies")
async def get_typologies():
    """Get list of available building typologies."""
    return {
        "typologies": [
            {
                "value": "point",
                "name": "Point Tower",
                "description": "One or more compact towers with nearly square footprints"
            },
            {
                "value": "linear",
                "name": "Linear/Slab",
                "description": "Bar buildings with elongated rectangular footprints"
            },
            {
                "value": "courtyard",
                "name": "Courtyard",
                "description": "Buildings arranged around a central courtyard space"
            }
        ]
    }


@app.post("/generate", response_model=GenerateResponse)
async def generate_layout(request: GenerateRequest):
    """
    Generate building layout for a given parcel.
    
    Returns a list of buildings with their footprints, heights, and metrics.
    """
    try:
        # Validate typology
        valid_typologies = ["point", "linear", "courtyard"]
        if request.structure_type.lower() not in valid_typologies:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid typology. Must be one of: {valid_typologies}"
            )
        
        # Validate parcel has at least 3 vertices
        if len(request.parcel_vertices) < 3:
            raise HTTPException(
                status_code=400,
                detail="Parcel must have at least 3 vertices"
            )
        
        # Validate floors_min <= floors_max
        if request.floors_min > request.floors_max:
            raise HTTPException(
                status_code=400,
                detail="floors_min must be less than or equal to floors_max"
            )
        
        # Generate layout
        result = generate_layout_from_location(
            parcel_vertices=request.parcel_vertices,
            structure_type=request.structure_type,
            n_buildings=request.n_buildings,
            far=request.far,
            floors_min=request.floors_min,
            floors_max=request.floors_max,
            floor_to_floor=request.floor_to_floor,
            seed=request.seed,
            min_edge_buffer=request.min_edge_buffer,
            min_building_buffer=request.min_building_buffer,
            min_building_thickness=request.min_building_thickness,
        )
        
        # Convert Shapely polygons to coordinate lists
        buildings_serialized = []
        for building in result["buildings"]:
            buildings_serialized.append({
                "footprint": serialize_polygon(building["footprint"]),
                "centroid": building["centroid"],
                "length": building["length"],
                "width": building["width"],
                "floors": building["floors"],
                "floor_height": building["floor_height"],
                "total_height": building["total_height"],
            })
        
        return {
            "buildings": buildings_serialized,
            "metrics": result["metrics"],
            "typology": result["typology"]
        }
        
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Internal error: {str(e)}")


if __name__ == "__main__":
    print("\n🏗️  Generative Design API Server")
    print("=" * 50)
    print("Starting server at http://localhost:8000")
    print("API docs available at http://localhost:8000/docs")
    print("Frontend at http://localhost:8000/index.html")
    print("=" * 50 + "\n")
    
    uvicorn.run(app, host="0.0.0.0", port=8000, log_level="info")

