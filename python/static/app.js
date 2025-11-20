// Generative Design Tool - Frontend Logic
// Handles 3D visualization, parcel drawing, and API communication

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

const API_URL = 'http://localhost:8000';

// MAPBOX CONFIGURATION
// Mapbox: https://account.mapbox.com/access-tokens/
const MAPBOX_TOKEN = 'pk.eyJ1IjoibWxpbTcwIiwiYSI6ImNtaTZvaWE5aDAwcTQyaXB5eHk4ZGRtMXUifQ.3eyAmH-7MWJaP5rjvRL8Fg'; // Matthew's public token
const USE_SATELLITE = true; // Set to false to use default ground

// Current location state
let currentLocation = {
    lat: 33.7756,
    lon: -84.3963,
    zoom: 18,
    name: 'Georgia Institute of Technology, Atlanta, GA'
};

// ============================================================================
// COORDINATE CONVERSION UTILITIES
// ============================================================================

// Get tile coordinates for lat/lon at zoom level
function latLonToTile(lat, lon, zoom) {
    const n = Math.pow(2, zoom);
    const x = Math.floor(((lon + 180) / 360) * n);
    const y = Math.floor(((1 - Math.log(Math.tan(lat * Math.PI / 180) + 1 / Math.cos(lat * Math.PI / 180)) / Math.PI) / 2) * n);
    return { x, y, zoom };
}

// ============================================================================
// LOCATION & GEOCODING
// ============================================================================

async function searchLocation() {
    const input = document.getElementById('location-input').value.trim();
    const statusDiv = document.getElementById('location-status');
    
    if (!input) {
        statusDiv.textContent = '❌ Please enter a location';
        statusDiv.style.color = '#ff6b6b';
        return;
    }
    
    statusDiv.textContent = '🔍 Searching...';
    statusDiv.style.color = '#667eea';
    
    try {
        // Check if input is lat,lon format
        const latLonMatch = input.match(/^(-?\d+\.?\d*)\s*,\s*(-?\d+\.?\d*)$/);
        
        if (latLonMatch) {
            // Direct lat/lon input
            const lat = parseFloat(latLonMatch[1]);
            const lon = parseFloat(latLonMatch[2]);
            
            if (lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180) {
                currentLocation = { lat, lon, zoom: 18, name: `${lat.toFixed(6)}, ${lon.toFixed(6)}` };
                statusDiv.textContent = `✓ Location set: ${currentLocation.name}`;
                statusDiv.style.color = '#00ff88';
                updateGroundWithMap();
            } else {
                throw new Error('Invalid coordinates');
            }
        } else {
            // Geocode address using Nominatim (free, no API key needed)
            const response = await fetch(
                `https://nominatim.openstreetmap.org/search?format=json&q=${encodeURIComponent(input)}&limit=1`
            );
            
            const data = await response.json();
            
            if (data && data.length > 0) {
                const result = data[0];
                currentLocation = {
                    lat: parseFloat(result.lat),
                    lon: parseFloat(result.lon),
                    zoom: 18,
                    name: result.display_name.split(',').slice(0, 2).join(',')
                };
                
                statusDiv.textContent = `✓ Found: ${currentLocation.name}`;
                statusDiv.style.color = '#00ff88';
                updateGroundWithMap();
            } else {
                throw new Error('Location not found');
            }
        }
    } catch (error) {
        console.error('Geocoding error:', error);
        statusDiv.textContent = `❌ ${error.message}`;
        statusDiv.style.color = '#ff6b6b';
    }
}

// ============================================================================
// THREE.JS SETUP
// ============================================================================

let scene, camera, renderer, controls;
let buildingGroup, groundPlane, parcelOutline;
let currentParcel = [];
let parcelMarkers = []; // 3D markers for parcel points
let parcelLines = null; // Lines connecting parcel points
let isDrawingParcel = true;
let raycaster, mouse;

function initThreeJS() {
    const container = document.getElementById('canvas3d');
    
    // Scene
    scene = new THREE.Scene();
    scene.background = new THREE.Color(0x87CEEB); // Sky blue background
    // Fog removed - interferes with zooming out
    
    // Camera
    const aspect = container.clientWidth / container.clientHeight;
    camera = new THREE.PerspectiveCamera(60, aspect, 0.1, 5000); // Increased far plane
    camera.position.set(80, 100, 80);
    camera.lookAt(0, 0, 0);
    
    // Renderer
    renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(container.clientWidth, container.clientHeight);
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    container.appendChild(renderer.domElement);
    
    // Controls
    controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.05;
    controls.maxPolarAngle = Math.PI / 2 - 0.1; // Can't look below horizon
    controls.minDistance = 10; // Minimum zoom distance
    controls.maxDistance = 2000; // Maximum zoom distance (can see large area)
    controls.target.set(0, 0, 0);
    
    // Lights
    const ambientLight = new THREE.AmbientLight(0xffffff, 0.6);
    scene.add(ambientLight);
    
    const sunLight = new THREE.DirectionalLight(0xffffff, 0.8);
    sunLight.position.set(50, 100, 50);
    sunLight.castShadow = true;
    sunLight.shadow.mapSize.width = 2048;
    sunLight.shadow.mapSize.height = 2048;
    sunLight.shadow.camera.near = 0.5;
    sunLight.shadow.camera.far = 500;
    sunLight.shadow.camera.left = -100;
    sunLight.shadow.camera.right = 100;
    sunLight.shadow.camera.top = 100;
    sunLight.shadow.camera.bottom = -100;
    scene.add(sunLight);
    
    const fillLight = new THREE.DirectionalLight(0x7795ff, 0.3);
    fillLight.position.set(-50, 50, -50);
    scene.add(fillLight);
    
    // Ground - will be updated with map texture
    const groundGeometry = new THREE.PlaneGeometry(2000, 2000); // Larger for zooming out
    const groundMaterial = new THREE.MeshStandardMaterial({ 
        color: 0x2a2a3e,
        roughness: 0.8,
        metalness: 0.2
    });
    groundPlane = new THREE.Mesh(groundGeometry, groundMaterial);
    groundPlane.rotation.x = -Math.PI / 2;
    groundPlane.receiveShadow = true;
    scene.add(groundPlane);
    
    // Load map texture if enabled
    if (USE_SATELLITE) {
        updateGroundWithMap();
    }
    
    // Grid
    const gridHelper = new THREE.GridHelper(800, 80, 0x444466, 0x333344); // Larger grid
    scene.add(gridHelper);
    
    // Building group
    buildingGroup = new THREE.Group();
    scene.add(buildingGroup);
    
    // Raycaster for click detection
    raycaster = new THREE.Raycaster();
    mouse = new THREE.Vector2();
    
    // Handle window resize
    window.addEventListener('resize', onWindowResize);
    
    // Handle clicks on 3D scene
    container.addEventListener('click', onSceneClick);
    
    // Start animation loop
    animate();
}

function animate() {
    requestAnimationFrame(animate);
    controls.update();
    renderer.render(scene, camera);
}

function onWindowResize() {
    const container = document.getElementById('canvas3d');
    camera.aspect = container.clientWidth / container.clientHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(container.clientWidth, container.clientHeight);
}

// ============================================================================
// MAP TILE LOADING & GROUND TEXTURE
// ============================================================================

async function updateGroundWithMap() {
    if (!groundPlane) return;
    
    const { lat, lon, zoom } = currentLocation;
    
    // Get tile coordinates for current location
    const tile = latLonToTile(lat, lon, zoom);
    
    // Construct tile URL - use Mapbox satellite if token provided, otherwise OSM
    let tileUrl;
    if (MAPBOX_TOKEN && USE_SATELLITE) {
        // Mapbox satellite imagery (high quality)
        tileUrl = `https://api.mapbox.com/v4/mapbox.satellite/${zoom}/${tile.x}/${tile.y}@2x.png?access_token=${MAPBOX_TOKEN}`;
        console.log('📡 Loading Mapbox satellite imagery...');
    } else {
        // OpenStreetMap (free, street map view) (fallback)
        tileUrl = `https://tile.openstreetmap.org/${zoom}/${tile.x}/${tile.y}.png`;
        console.log('🗺️ Loading OpenStreetMap tiles...');
    }
    
    try {
        const textureLoader = new THREE.TextureLoader();
        textureLoader.crossOrigin = 'anonymous';
        
        const texture = await new Promise((resolve, reject) => {
            textureLoader.load(
                tileUrl,
                resolve,
                undefined,
                reject
            );
        });
        
        texture.wrapS = THREE.RepeatWrapping;
        texture.wrapT = THREE.RepeatWrapping;
        
        // Update ground material with texture
        groundPlane.material = new THREE.MeshStandardMaterial({
            map: texture,
            roughness: 0.9,
            metalness: 0.1
        });
        
        console.log('✓ Map texture loaded successfully');
    } catch (error) {
        console.warn('Could not load map tiles, using default ground:', error);
        // Keep default material
    }
}

// ============================================================================
// 3D PARCEL DRAWING - Click on Map
// ============================================================================

function onSceneClick(event) {
    if (!isDrawingParcel) return;
    
    // Don't interfere with orbit controls right-click
    if (event.button === 2) return;
    
    const container = document.getElementById('canvas3d');
    const rect = container.getBoundingClientRect();
    
    // Calculate mouse position in normalized device coordinates (-1 to +1)
    mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
    
    // Cast ray from camera through mouse position
    raycaster.setFromCamera(mouse, camera);
    
    // Check intersection with ground plane
    const intersects = raycaster.intersectObject(groundPlane);
    
    if (intersects.length > 0) {
        const point = intersects[0].point;
        
        // Check if clicking near first point to close shape
        if (currentParcel.length >= 3) {
            const first = currentParcel[0];
            // Distance calculation in world coordinates
            const dist = Math.sqrt(
                Math.pow(point.x - first[0], 2) + 
                Math.pow(point.z - first[1], 2)
            );
            
            if (dist < 5) { // 5 meter tolerance
                // Close the parcel
                isDrawingParcel = false;
                updateParcelOutline3D();
                
                // Update cursor and status
                const canvas = document.getElementById('canvas3d');
                if (canvas) canvas.classList.remove('drawing-mode');
                updateParcelStatus();
                
                console.log('✓ Parcel closed');
                return;
            }
        }
        
        // Add point to parcel
        // Store parcel in true world-plane coords: [x, z]
        currentParcel.push([point.x, point.z]);
        addParcelMarker(point.x, point.z);
        updateParcelLines();
        updateParcelStatus();
        
        console.log(`Point added: World(${point.x.toFixed(2)}, ${point.z.toFixed(2)})`);
    }
}

function addParcelMarker(x, z) {
    const isFirst = parcelMarkers.length === 0;
    
    // Create sphere marker
    const geometry = new THREE.SphereGeometry(isFirst ? 1.5 : 1, 16, 16);
    const material = new THREE.MeshStandardMaterial({
        color: isFirst ? 0xff6b6b : 0x00ff88,
        emissive: isFirst ? 0xff6b6b : 0x00ff88,
        emissiveIntensity: 0.5
    });
    
    const marker = new THREE.Mesh(geometry, material);
    marker.position.set(x, 0.5, z);
    marker.castShadow = true;
    
    scene.add(marker);
    parcelMarkers.push(marker);
}

function updateParcelLines() {
    // Remove old lines
    if (parcelLines) {
        scene.remove(parcelLines);
    }
    
    if (currentParcel.length < 2) return;
    
    // Create line geometry from world coordinates [x, z]
    const points = currentParcel.map(p => new THREE.Vector3(p[0], 0.2, p[1]));
    if (!isDrawingParcel) {
        points.push(new THREE.Vector3(currentParcel[0][0], 0.2, currentParcel[0][1]));
    }
    
    const geometry = new THREE.BufferGeometry().setFromPoints(points);
    const material = new THREE.LineBasicMaterial({ 
        color: 0x00ff88,
        linewidth: 3
    });
    
    parcelLines = new THREE.Line(geometry, material);
    scene.add(parcelLines);
}

function updateParcelStatus() {
    const statusDiv = document.getElementById('parcel-status');
    if (!statusDiv) return;
    
    const numPoints = currentParcel.length;
    
    if (isDrawingParcel) {
        if (numPoints === 0) {
            statusDiv.innerHTML = `
                <div style="font-size: 13px; color: #555; margin-bottom: 8px;">
                    <strong>🎯 Click on the map</strong> to add points
                </div>
                <div style="font-size: 12px; color: #888;">
                    Need at least 3 points to make a parcel
                </div>
            `;
            statusDiv.style.background = '#f8f9fa';
        } else if (numPoints < 3) {
            statusDiv.innerHTML = `
                <div style="font-size: 13px; color: #667eea; margin-bottom: 8px;">
                    <strong>📍 ${numPoints} point${numPoints > 1 ? 's' : ''} added</strong>
                </div>
                <div style="font-size: 12px; color: #888;">
                    Add ${3 - numPoints} more to close parcel
                </div>
            `;
            statusDiv.style.background = '#e8eaff';
        } else {
            statusDiv.innerHTML = `
                <div style="font-size: 13px; color: #667eea; margin-bottom: 8px;">
                    <strong>📍 ${numPoints} points added</strong>
                </div>
                <div style="font-size: 12px; color: #00ff88;">
                    Click near the first (red) point to close
                </div>
            `;
            statusDiv.style.background = '#e8fff5';
        }
    } else {
        statusDiv.innerHTML = `
            <div style="font-size: 13px; color: #00ff88; margin-bottom: 8px;">
                <strong>✓ Parcel ready</strong> (${numPoints} points)
            </div>
            <div style="font-size: 12px; color: #888;">
                Click "Clear Parcel" to draw a new one
            </div>
        `;
        statusDiv.style.background = '#e8fff5';
    }
}

function clearParcel() {
    // Clear markers
    parcelMarkers.forEach(marker => scene.remove(marker));
    parcelMarkers = [];
    
    // Clear lines
    if (parcelLines) {
        scene.remove(parcelLines);
        parcelLines = null;
    }
    
    // Clear outline and parcel data
    currentParcel = [];
    isDrawingParcel = true;
    clearBuildings();
    
    if (parcelOutline) {
        scene.remove(parcelOutline);
        parcelOutline = null;
    }
    
    // Update cursor and status
    const canvas = document.getElementById('canvas3d');
    if (canvas) canvas.classList.add('drawing-mode');
    updateParcelStatus();
    
    console.log('Parcel cleared - click on map to start drawing');
}

function loadExampleParcel(type) {
    clearParcel();
    
    const size = 40; // meters
    
    if (type === 'square') {
        currentParcel = [
            [-size/2, -size/2],
            [size/2, -size/2],
            [size/2, size/2],
            [-size/2, size/2]
        ];
    } else if (type === 'rectangle') {
        currentParcel = [
            [-size*0.8, -size*0.5],
            [size*0.8, -size*0.5],
            [size*0.8, size*0.5],
            [-size*0.8, size*0.5]
        ];
    } else if (type === 'l-shape') {
        const s = size * 0.6;
        currentParcel = [
            [-s, -s],
            [0, -s],
            [0, 0],
            [s, 0],
            [s, s],
            [-s, s]
        ];
    }
    
    isDrawingParcel = false;
    
    // Add markers for each point in world coordinates
    currentParcel.forEach((p) => {
        addParcelMarker(p[0], p[1]);
    });
    
    updateParcelLines();
    updateParcelOutline3D();
    
    // Update cursor and status
    const canvas = document.getElementById('canvas3d');
    if (canvas) canvas.classList.remove('drawing-mode');
    updateParcelStatus();
    
    console.log(`✓ Loaded ${type} parcel example`);
}

// ============================================================================
// 3D PARCEL OUTLINE
// ============================================================================

function updateParcelOutline3D() {
    // Remove old outline
    if (parcelOutline) {
        scene.remove(parcelOutline);
    }
    
    if (currentParcel.length < 3) return;
    
    // Add parcel ground plane with semi-transparent fill
    // currentParcel is [x, z]; for the shape, use (x, -z)
    const shape = new THREE.Shape();
    shape.moveTo(currentParcel[0][0], -currentParcel[0][1]);
    for (let i = 1; i < currentParcel.length; i++) {
        shape.lineTo(currentParcel[i][0], -currentParcel[i][1]);
    }
    shape.lineTo(currentParcel[0][0], -currentParcel[0][1]);
    
    const parcelGeometry = new THREE.ShapeGeometry(shape);
    const parcelMaterial = new THREE.MeshBasicMaterial({ 
        color: 0x00ff88, 
        transparent: true, 
        opacity: 0.15,
        side: THREE.DoubleSide
    });
    parcelOutline = new THREE.Mesh(parcelGeometry, parcelMaterial);
    parcelOutline.rotation.x = -Math.PI / 2;
    parcelOutline.position.y = 0.1; // Slightly above ground
    scene.add(parcelOutline);
    
    // Center camera on parcel
    const center = getCentroid(currentParcel);
    controls.target.set(center[0], 0, center[1]);
    
    console.log(`✓ Parcel outline updated (${currentParcel.length} points)`);
}

function getCentroid(points) {
    const sum = points.reduce((acc, p) => [acc[0] + p[0], acc[1] + p[1]], [0, 0]);
    return [sum[0] / points.length, sum[1] / points.length];
}

// ============================================================================
// 3D BUILDING VISUALIZATION
// ============================================================================

function clearBuildings() {
    while (buildingGroup.children.length > 0) {
        buildingGroup.remove(buildingGroup.children[0]);
    }
}

function visualizeBuildings(buildings) {
    clearBuildings();
    
    const colors = [
        0x667eea, // Purple
        0x764ba2, // Dark purple
        0x49a09d, // Teal
        0xf86624, // Orange
        0xf9ca24, // Yellow
    ];
    
    buildings.forEach((building, index) => {
        const footprint = building.footprint;
        const height = building.total_height;
        const exteriorCoords = footprint.exterior;
        const holeRings = footprint.holes || [];
        
        if (!exteriorCoords || exteriorCoords.length < 3) {
            return; // Skip invalid footprints
        }
        
        // Create main shape from exterior ring
        // Backend returns [x, y] where y == world z from your parcel
        // Use (x, -y) for shape to compensate for rotateX(-π/2) negation
        // After rotation: z_world = -y_shape = -(-y) = y (correct!)
        const shape = new THREE.Shape();
        shape.moveTo(exteriorCoords[0][0], -exteriorCoords[0][1]);
        for (let i = 1; i < exteriorCoords.length; i++) {
            shape.lineTo(exteriorCoords[i][0], -exteriorCoords[i][1]);
        }
        shape.lineTo(exteriorCoords[0][0], -exteriorCoords[0][1]);
        
        // Add holes (courtyard voids)
        holeRings.forEach(ring => {
            if (!ring || ring.length < 3) return;
            const holePath = new THREE.Path();
            holePath.moveTo(ring[0][0], -ring[0][1]);
            for (let i = 1; i < ring.length; i++) {
                holePath.lineTo(ring[i][0], -ring[i][1]);
            }
            holePath.lineTo(ring[0][0], -ring[0][1]);
            shape.holes.push(holePath);
        });
        
        // Extrude settings
        const extrudeSettings = {
            depth: height,
            bevelEnabled: false
        };
        
        const geometry = new THREE.ExtrudeGeometry(shape, extrudeSettings);
        
        // Rotate to stand upright
        geometry.rotateX(-Math.PI / 2);
        
        // Material with random color
        const color = colors[index % colors.length];
        const material = new THREE.MeshStandardMaterial({
            color: color,
            roughness: 0.7,
            metalness: 0.3,
            flatShading: false
        });
        
        const mesh = new THREE.Mesh(geometry, material);
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        
        buildingGroup.add(mesh);
        
        // Add wireframe outline
        const edges = new THREE.EdgesGeometry(geometry);
        const lineMaterial = new THREE.LineBasicMaterial({ 
            color: 0x000000, 
            linewidth: 1,
            transparent: true,
            opacity: 0.2
        });
        const wireframe = new THREE.LineSegments(edges, lineMaterial);
        mesh.add(wireframe);
    });
}

// ============================================================================
// API COMMUNICATION
// ============================================================================

async function generateLayout() {
    if (currentParcel.length < 3) {
        alert('Please draw a parcel boundary first!');
        return;
    }
    
    // currentParcel is already in world coordinates [x, z]
    // Send directly to backend as [x, y] where backend y = world z
    const parcelForBackend = currentParcel.map(([x, z]) => [x, z]);
    
    // Get parameters from form
    const typology = document.getElementById('typology').value;
    const n_buildings = parseInt(document.getElementById('n_buildings').value);
    const far = parseFloat(document.getElementById('far').value);
    const floors_min = parseFloat(document.getElementById('floors_min').value);
    const floors_max = parseFloat(document.getElementById('floors_max').value);
    const floor_to_floor = parseFloat(document.getElementById('floor_to_floor').value);
    const seedInput = document.getElementById('seed').value;
    const seed = seedInput ? parseInt(seedInput) : null;
    
    // Get advanced parameters (optional)
    const minEdgeBufferInput = document.getElementById('min_edge_buffer').value;
    const minBuildingBufferInput = document.getElementById('min_building_buffer').value;
    const minBuildingThicknessInput = document.getElementById('min_building_thickness').value;
    
    const min_edge_buffer = minEdgeBufferInput ? parseFloat(minEdgeBufferInput) : null;
    const min_building_buffer = minBuildingBufferInput ? parseFloat(minBuildingBufferInput) : null;
    const min_building_thickness = minBuildingThicknessInput ? parseFloat(minBuildingThicknessInput) : null;
    
    // Validate
    if (floors_min > floors_max) {
        alert('Minimum floors must be less than or equal to maximum floors!');
        return;
    }
    
    // Show loading
    document.getElementById('loading').classList.add('active');
    
    try {
        const response = await fetch(`${API_URL}/generate`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({
                parcel_vertices: parcelForBackend,
                structure_type: typology,
                n_buildings: n_buildings,
                far: far,
                floors_min: floors_min,
                floors_max: floors_max,
                floor_to_floor: floor_to_floor,
                seed: seed,
                min_edge_buffer: min_edge_buffer,
                min_building_buffer: min_building_buffer,
                min_building_thickness: min_building_thickness
            })
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.detail || 'Failed to generate layout');
        }
        
        const data = await response.json();
        
        // Visualize buildings
        visualizeBuildings(data.buildings);
        
        // Update metrics
        updateMetrics(data.metrics);
        
    } catch (error) {
        console.error('Error:', error);
        alert('Error generating layout: ' + error.message);
    } finally {
        document.getElementById('loading').classList.remove('active');
    }
}

function updateMetrics(metrics) {
    const metricsDiv = document.getElementById('metrics');
    
    metricsDiv.innerHTML = `
        <div class="metric-row">
            <span class="metric-label">Parcel Area</span>
            <span class="metric-value">${metrics.parcel_area.toFixed(1)} m²</span>
        </div>
        <div class="metric-row">
            <span class="metric-label">Buildings</span>
            <span class="metric-value">${metrics.n_buildings}</span>
        </div>
        <div class="metric-row">
            <span class="metric-label">Target FAR</span>
            <span class="metric-value">${metrics.target_far.toFixed(2)}</span>
        </div>
        <div class="metric-row">
            <span class="metric-label">Actual FAR</span>
            <span class="metric-value">${metrics.actual_far.toFixed(2)}</span>
        </div>
        <div class="metric-row">
            <span class="metric-label">Target GFA</span>
            <span class="metric-value">${metrics.target_gfa.toFixed(1)} m²</span>
        </div>
        <div class="metric-row">
            <span class="metric-label">Actual GFA</span>
            <span class="metric-value">${metrics.actual_gfa.toFixed(1)} m²</span>
        </div>
        <div class="metric-row">
            <span class="metric-label">Site Coverage</span>
            <span class="metric-value">${(metrics.scr * 100).toFixed(1)}%</span>
        </div>
        <div class="metric-row">
            <span class="metric-label">Avg Floors</span>
            <span class="metric-value">${metrics.avg_floors.toFixed(1)}</span>
        </div>
        <div class="metric-row">
            <span class="metric-label">Density</span>
            <span class="metric-value">${metrics.density.toFixed(2)}</span>
        </div>
    `;
}

// ============================================================================
// INITIALIZATION
// ============================================================================

// ============================================================================
// ADVANCED SETTINGS MANAGEMENT
// ============================================================================

function toggleAdvancedSettings() {
    const advancedSettings = document.getElementById('advanced-settings');
    const toggleIcon = document.getElementById('advanced-toggle');
    
    if (advancedSettings.style.display === 'none') {
        advancedSettings.style.display = 'block';
        toggleIcon.textContent = '▲';
    } else {
        advancedSettings.style.display = 'none';
        toggleIcon.textContent = '▼';
    }
}

function updateAdvancedFieldsForTypology() {
    const typology = document.getElementById('typology').value;
    const buildingBufferGroup = document.getElementById('min_building_buffer_group');
    const buildingThicknessGroup = document.getElementById('min_building_thickness_group');
    const buildingBufferHint = document.getElementById('building_buffer_hint');
    
    // Show/hide fields based on typology
    if (typology === 'courtyard') {
        buildingBufferGroup.style.display = 'none';
        buildingThicknessGroup.style.display = 'block';
    } else {
        buildingBufferGroup.style.display = 'block';
        buildingThicknessGroup.style.display = 'none';
        
        // Update hint based on typology
        if (typology === 'linear') {
            buildingBufferHint.textContent = 'Spacing between linear slabs (default: 3.0m)';
        } else if (typology === 'point') {
            buildingBufferHint.textContent = 'Spacing between point towers (default: 14.0m)';
        }
    }
}

// ============================================================================
// MAKE FUNCTIONS AVAILABLE GLOBALLY (for onclick handlers)
// ============================================================================

window.searchLocation = searchLocation;
window.clearParcel = clearParcel;
window.loadExampleParcel = loadExampleParcel;
window.generateLayout = generateLayout;
window.toggleAdvancedSettings = toggleAdvancedSettings;
window.updateAdvancedFieldsForTypology = updateAdvancedFieldsForTypology;

// ============================================================================
// INITIALIZATION
// ============================================================================

window.addEventListener('DOMContentLoaded', () => {
    initThreeJS();
    
    // Set initial location display
    const statusDiv = document.getElementById('location-status');
    statusDiv.textContent = `📍 ${currentLocation.name}`;
    statusDiv.style.color = '#667eea';
    
    // Add event listener to typology selector
    const typologySelect = document.getElementById('typology');
    typologySelect.addEventListener('change', updateAdvancedFieldsForTypology);
    
    // Initialize advanced fields visibility
    updateAdvancedFieldsForTypology();
    
    // Load example parcel on startup
    setTimeout(() => {
        loadExampleParcel('rectangle');
        console.log('');
        console.log('🏗️ GENERATIVE DESIGN TOOL');
        console.log('========================');
        console.log('✓ Ready! Example parcel loaded');
        console.log('💡 Click "Clear Parcel" and click on the map to draw your own');
        console.log('💡 Or try different examples');
        console.log('');
    }, 500);
});

