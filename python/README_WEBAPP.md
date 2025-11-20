# 🏗️ Generative Design Tool - three.js Web Application

## 📋 Prerequisites

- Python 3.8 or higher
- pip (Python package manager)

## 🚀 Quick Start

### 1. Install Dependencies

```bash
cd python
pip install -r requirements.txt
```

### 2. Run the Server

```bash
python app.py
```

Or alternatively:

```bash
uvicorn app:app --reload --host 0.0.0.0 --port 8000
```

In terminal you should see:

```
🏗️  Generative Design API Server
==================================================
Starting server at http://localhost:8000
API docs available at http://localhost:8000/docs
Frontend at http://localhost:8000/index.html
==================================================
```

### 3. Open the Application

Open your web browser and navigate to:
**http://localhost:8000**

## 🎮 How to Use

### Setting Your Location

1. **Enter an address** or coordinates in the location field
2. **Click "Find Location"**
3. The map will update to show your location (if map tiles are enabled)

> See `static/MAP_SETUP.md` for enabling satellite imagery with Mapbox!


## 🔧 API Endpoints

The backend provides a RESTful API:
### `POST /generate`

Generate a building layout.

**Request Body:**

```json
{
  "parcel_vertices": [[0, 0], [50, 0], [50, 50], [0, 50]],
  "structure_type": "point",
  "n_buildings": 5,
  "far": 2.5,
  "floors_min": 4,
  "floors_max": 12,
  "floor_to_floor": 3.5,
  "seed": 42
}
```

**Response:**

```json
{
  "buildings": [
    {
      "footprint": [[10, 10], [20, 10], [20, 20], [10, 20]],
      "centroid": [15, 15],
      "length": 10.0,
      "width": 10.0,
      "floors": 8,
      "floor_height": 3.5,
      "total_height": 28.0
    }
  ],
  "metrics": {
    "parcel_area": 2500.0,
    "target_far": 2.5,
    "actual_far": 2.48,
    "target_gfa": 6250.0,
    "actual_gfa": 6200.0,
    "scr": 0.31,
    "n_buildings": 5,
    "density": 0.45,
    "avg_floors": 7.8
  },
  "typology": "point"
}
```

### `GET /typologies`

List available building typologies.

### `GET /docs`

Interactive API documentation (Swagger UI).


## 📊 Understanding the Metrics

### FAR (Floor Area Ratio)
- Ratio of total building floor area to parcel area
- Example: FAR of 2.0 means total building floor space is 2× the site area
- Higher FAR = denser development

### GFA (Gross Floor Area)
- Total floor area across all buildings and all floors
- Calculated as: sum of (footprint area × number of floors)

### SCR (Site Coverage Ratio)
- Percentage of site covered by building footprints
- Example: SCR of 0.30 = 30% of site is covered

### Density
- Metric representing building spacing and distribution
- Higher values = more tightly packed buildings

## 🎨 Customization

### Changing Colors

Edit `static/app.js` and modify the `colors` array in the `visualizeBuildings` function:

```javascript
const colors = [
    0x667eea, // Purple
    0x764ba2, // Dark purple
    0x49a09d, // Teal
    0xf86624, // Orange
    0xf9ca24, // Yellow
];
```

### Adjusting Camera

Modify initial camera position in `initThreeJS()`:

```javascript
camera.position.set(80, 100, 80); // x, y, z
```

### Grid Size

Change grid helper in `initThreeJS()`:

```javascript
const gridHelper = new THREE.GridHelper(200, 40, 0x444466, 0x333344);
// GridHelper(size, divisions, centerLineColor, gridColor)
```

## 🐛 Troubleshooting

### Port Already in Use

If port 8000 is taken, change it in `app.py`:

```python
uvicorn.run(app, host="0.0.0.0", port=8080)  # Use 8080 instead
```

### CORS Errors

If accessing from a different domain, update CORS settings in `app.py`:

```python
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://yourfrontend.com"],  # Specify your domain
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)
```

### Buildings Not Appearing

1. Check browser console for errors (F12)
2. Verify parcel has at least 3 points
3. Ensure floors_min ≤ floors_max
4. Try with example parcels first

### 3D View is Black

1. Check if WebGL is supported in your browser
2. Try a different browser (Chrome, Firefox, Edge)
3. Update graphics drivers

## 🌐 Deployment

### Local Network Access

To access from other devices on your network:

```bash
python app.py
# Server will be available at http://YOUR_LOCAL_IP:8000
```

Find your local IP:
- **Windows**: `ipconfig`
- **Mac/Linux**: `ifconfig` or `ip addr`

### Production Deployment

For production deployment (Heroku, AWS, Google Cloud, etc.):

1. Set proper CORS origins
2. Use environment variables for configuration
3. Add authentication if needed
4. Use production ASGI server settings
5. Enable HTTPS

Example with environment variables:

```python
import os
PORT = int(os.getenv("PORT", 8000))
DEBUG = os.getenv("DEBUG", "false").lower() == "true"
```

## 📁 Project Structure

```
python/
├── app.py                 # FastAPI backend server
├── requirements.txt       # Python dependencies
├── generator/            # Core generation logic
│   ├── __init__.py
│   ├── api.py           # Main API function
│   ├── types.py         # Data models
│   └── layouts/         # Layout algorithms
│       ├── point.py     # Point tower layout
│       ├── linear.py    # Linear/slab layout
│       └── courtyard.py # Courtyard layout
└── static/              # Frontend files
    ├── index.html       # Main HTML page
    └── app.js          # Three.js application
```

## 🤝 Contributing

Feel free to:
- Report bugs
- Suggest features
- Submit pull requests
- Improve documentation

## 📄 License

Apache License 2.0 - See LICENSE file for details

## 🙏 Acknowledgments

- **Three.js** - 3D visualization
- **FastAPI** - Backend framework
- **Shapely** - Geometric operations

---

**Built with ❤️ for urban designers and architects**

