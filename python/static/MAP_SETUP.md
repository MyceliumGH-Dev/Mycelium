# 🗺️ Map Integration Setup Guide

Your app now supports real-world locations with map overlay!

## Current Setup

By default, the app uses:
- **OpenStreetMap tiles** (street map view)
- **Free geocoding** via Nominatim
- **Real coordinates** support

## 🛰️ Enabling Satellite Imagery (Recommended)

For the professional look like Autodesk Forma, upgrade to Mapbox satellite tiles:

### Step 1: Get a Free Mapbox Token

1. Go to https://account.mapbox.com/auth/signup/
2. Sign up for a free account (no credit card needed)
3. Go to https://account.mapbox.com/access-tokens/
4. Copy your **default public token** (starts with `pk.`)

**Free tier includes:**
- 50,000 map loads per month
- 100,000 geocoding requests
- No credit card required

### Step 2: Add Token to Your App

Open `static/app.js` and replace the placeholder token:

```javascript
// Line ~8
const MAPBOX_TOKEN = 'pk.YOUR_ACTUAL_TOKEN_HERE';
```

### Step 3: Enable Satellite Tiles

In the same file, update the tile URL in `updateGroundWithMap()`:

```javascript
// Replace this line (around line 173):
const tileUrl = `https://tile.openstreetmap.org/${zoom}/${tile.x}/${tile.y}.png`;

// With this:
const tileUrl = `https://api.mapbox.com/v4/mapbox.satellite/${zoom}/${tile.x}/${tile.y}@2x.png?access_token=${MAPBOX_TOKEN}`;
```

Save and refresh your browser - you'll now see satellite imagery! 🛰️

## 🌍 How to Use

### Search by Address
```
Times Square, New York
1600 Pennsylvania Avenue, Washington DC
Eiffel Tower, Paris
```

### Search by Coordinates
```
40.758,-73.985
51.5074,-0.1278
48.8584,2.2945
```

### Drawing Parcels on Real Locations

1. **Find your location** using the search
2. **Draw parcel boundaries** on the 2D canvas
3. **Generate buildings** - they'll appear on the map!

The parcel you draw will be positioned at your searched location.

## 🎨 Map Styles

### Available Mapbox Styles

Replace `mapbox.satellite` in the URL with:

- `mapbox.satellite` - Satellite imagery (recommended)
- `mapbox.streets-v11` - Street map
- `mapbox.outdoors-v11` - Topographic style
- `mapbox.light-v10` - Minimal light theme
- `mapbox.dark-v10` - Dark theme

Example:
```javascript
const tileUrl = `https://api.mapbox.com/v4/mapbox.outdoors-v11/${zoom}/${tile.x}/${tile.y}@2x.png?access_token=${MAPBOX_TOKEN}`;
```

## 🌐 Alternative Map Providers

### Google Maps (Requires API Key)
```javascript
const tileUrl = `https://mt1.google.com/vt/lyrs=s&x=${tile.x}&y=${tile.y}&z=${zoom}`;
```

Styles:
- `s` - Satellite
- `m` - Roadmap
- `h` - Hybrid (satellite + labels)
- `t` - Terrain

### Esri Satellite
```javascript
const tileUrl = `https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/${zoom}/${tile.y}/${tile.x}`;
```

## 📊 Coordinate Systems

The app automatically handles conversions:

- **Input**: Latitude/Longitude (WGS84)
- **Internal**: Web Mercator projection (meters)
- **Output**: Buildings positioned correctly on map

Your buildings are generated in real-world coordinates!

## 🐛 Troubleshooting

### No Map Showing
- Check browser console (F12) for errors
- Verify your Mapbox token is correct
- Check internet connection

### Map Offset/Wrong Location
- Ensure coordinates are in correct order: `lat, lon`
- Check zoom level (18 is good for buildings)

### Tiles Not Loading
- Some tile servers require attribution
- Check CORS policy
- Try a different tile provider

## 🎯 Pro Tips

1. **Zoom level 18** is ideal for building-scale work
2. **Search first** before drawing parcels
3. **Use satellite view** for context
4. **Draw accurately** - buildings will match real-world scale
5. **Export coordinates** for GIS integration

## 📱 Mobile Support

The geocoding and map tiles work on mobile browsers too!

## ⚖️ Legal Notes

- **OpenStreetMap**: Attribution required (© OpenStreetMap contributors)
- **Mapbox**: Free tier available, attribution required
- **Google Maps**: Requires valid API key and billing account
- **Esri**: Free for non-commercial use

Always check the terms of service for your chosen provider.

---

**You now have a professional GIS-integrated generative design tool!** 🎉

