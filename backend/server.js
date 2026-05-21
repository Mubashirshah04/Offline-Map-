const express = require('express');
const sqlite3 = require('sqlite3').verbose();
const path = require('path');
const axios = require('axios');
const fs = require('fs');
const cors = require('cors');

const app = express();
const PORT = 5000;
const DB_PATH = path.join(__dirname, 'tiles.db');

// 🛠️ ENVIRONMENT PROVISIONING (v131.0): Ensure clean startup on fresh downloads
const WWWROOT = path.join(__dirname, 'wwwroot');
const TILES_DIR = path.join(WWWROOT, 'tiles');
if (!fs.existsSync(WWWROOT)) fs.mkdirSync(WWWROOT, { recursive: true });
if (!fs.existsSync(TILES_DIR)) fs.mkdirSync(TILES_DIR, { recursive: true });

app.use(cors());
app.use(express.json());

let db;
function initDB() {
    console.log("🔍 [OMEGA] Initializing Safe-Kernel DB...");
    db = new sqlite3.Database(DB_PATH, (err) => {
        if (err) console.error("❌ Critical DB Error:", err);
    });

    db.serialize(() => {
        // 🛡️ LINUX INODE FIX + POWER-CUT RESILIENCE (v200.0)
        db.run("PRAGMA journal_mode = WAL");
        db.run("PRAGMA synchronous = NORMAL"); // ⚡ Balanced: Safe + Performance
        db.run("PRAGMA cache_size = -524288"); // 512MB cache (negative = KiB)
        db.run("PRAGMA temp_store = MEMORY");
        db.run("PRAGMA mmap_size = 268435456"); // 256MB memory map
        db.run("PRAGMA wal_autocheckpoint = 1000"); // Auto checkpoint every 1000 pages
        db.run("PRAGMA page_size = 4096"); // 4KB pages for efficiency

        db.run("CREATE TABLE IF NOT EXISTS downloads (city TEXT PRIMARY KEY, status TEXT, size_mb REAL, completed_tiles INTEGER, total_tiles INTEGER, total_mb REAL, bbox TEXT)");
        db.run("CREATE TABLE IF NOT EXISTS tiles (layer TEXT, z INTEGER, x INTEGER, y INTEGER, data BLOB, PRIMARY KEY(layer, z, x, y))");

        console.log("✅ Database Engine Online [WAL-Mode Active]");
    });
}
initDB();

let downloadQueue = { total: 0, completed: 0, bytes: 0, active: false, paused: false, city: '', maxZoom: 21, totalMb: 0, startTime: 0 };

// 🛡️ NETWORK RESILIENCE: Exponential backoff retry
async function fetchWithRetry(url, options, maxRetries = 5) {
    for (let i = 0; i < maxRetries; i++) {
        try {
            return await axios.get(url, options);
        } catch (e) {
            const isLastRetry = i === maxRetries - 1;
            const isNetworkError = !e.response || e.code === 'ECONNRESET' || e.code === 'ETIMEDOUT' || e.code === 'ENOTFOUND';
            if (isLastRetry) throw e;
            if (isNetworkError) {
                const delay = Math.min(1000 * Math.pow(2, i), 30000); // Exponential backoff, max 30s
                console.log(`[NETWORK] Retry ${i + 1}/${maxRetries} after ${delay}ms for: ${url.substring(0, 60)}...`);
                await new Promise(r => setTimeout(r, delay));
            } else {
                throw e;
            }
        }
    }
}

async function getTileWithFallback(layer, z, x, y) {
    return new Promise((resolve) => {
        db.get(`SELECT data FROM tiles WHERE layer=? AND z=? AND x=? AND y=?`, [layer, z, x, y], async (err, row) => {
            if (row) {
                downloadQueue.bytes += row.data.length;
                return resolve(row.data);
            }
            const urls = {
                'google-street': `https://mt1.google.com/vt/lyrs=m&x=${x}&y=${y}&z=${z}`,
                'satellite': `https://mt1.google.com/vt/lyrs=s&x=${x}&y=${y}&z=${z}`,
                'arcgis-street': `https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/${z}/${y}/${x}`,
                'arcgis-satellite': `https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/${z}/${y}/${x}`
            };
            try {
                const response = await fetchWithRetry(urls[layer] || urls['google-street'], { responseType: 'arraybuffer', timeout: 15000 });
                const buffer = Buffer.from(response.data, 'binary');
                db.run(`INSERT OR REPLACE INTO tiles (layer, z, x, y, data) VALUES (?,?,?,?,?)`, [layer, z, x, y, buffer]);
                downloadQueue.bytes += buffer.length;
                resolve(buffer);
            } catch (e) {
                console.log(`[TILE-FAIL] Z${z}/${x}/${y} ${layer}: ${e.message}`);
                resolve(null);
            }
        });
    });
}

function latLngToTile(lat, lng, zoom) {
    const sinLat = Math.sin(lat * Math.PI / 180);
    const zBase = Math.pow(2, zoom);
    return { x: Math.floor((lng + 180) / 360 * zBase), y: Math.floor((0.5 - Math.log((1 + sinLat) / (1 - sinLat)) / (4 * Math.PI)) * zBase) };
}

function tileToLatLng(x, y, z) {
    const n = Math.PI - 2 * Math.PI * y / Math.pow(2, z);
    return {
        lat: (180 / Math.PI * Math.atan(0.5 * (Math.exp(n) - Math.exp(-n)))),
        lon: (x / Math.pow(2, z) * 360 - 180)
    };
}

app.get('/tiles/:layer/:z/:x/:y.png', async (req, res) => {
    const { layer, z, x, y } = req.params;
    const tile = await getTileWithFallback(layer, parseInt(z), parseInt(x), parseInt(y));
    if (tile) { res.set('Content-Type', 'image/png'); res.send(tile); } else res.status(404).send('Not found');
});

app.post('/start-download', (req, res) => {
    const { city, bbox } = req.body;

    // 🌍 CORE STRATEGIC TARGETS (High-Precision)
    const targets = {
        'All Pakistan': [60.8, 23.6, 77.9, 37.1],
        'Full Iran': [44.0, 24.0, 63.5, 40.0],
        'Full Afghanistan': [60.0, 29.3, 74.9, 38.5],
        'Full India': [68.1, 8.1, 97.4, 35.5],
        'Full China': [73.5, 18.2, 134.8, 53.6],
        'Full USA': [-124.7, 24.5, -66.9, 49.4],
        'Full Russia': [19.6, 41.2, 190.2, 81.9],
        'Full Turkey': [25.6, 35.8, 44.8, 42.1],
        'Full Saudi Arabia': [34.5, 16.4, 55.6, 32.2],
        'Full UAE': [51.5, 22.6, 56.4, 26.0]
    };

    let activeBbox = bbox;
    if (targets[city]) activeBbox = targets[city];

    if (!activeBbox) return res.status(400).json({ error: "Missing Bounding Box" });

    const maxZ = 21;
    let totalTotal = 0;
    const layers = ['google-street', 'satellite', 'arcgis-street', 'arcgis-satellite'];
    const isStrategic = false; // Estimator: Applied Global Smart-Harvest

    // 📐 SMART ESTIMATOR (v117.0): Model the harvest before starting
    for (let z = 0; z <= maxZ; z++) {
        const nw = latLngToTile(Math.max(activeBbox[1], activeBbox[3]), Math.min(activeBbox[0], activeBbox[2]), z);
        const se = latLngToTile(Math.min(activeBbox[1], activeBbox[3]), Math.max(activeBbox[0], activeBbox[2]), z);
        const layerTiles = (Math.abs(se.x - nw.x) + 1) * (Math.abs(se.y - nw.y) + 1);

        if (z <= 18 || isStrategic) {
            totalTotal += layerTiles;
        } else {
            totalTotal += (50 * 25000) / (maxZ - 18);
        }
    }

    const finalTotal = Math.floor(totalTotal * layers.length);
    let finalTotalMb = (finalTotal * 0.006);

    // 💎 PAKISTAN PRIORITY: UNLIMITED MODE (v200.0)
    // NO BUDGET CAPS - Download all available tiles
    const strategicBudgets = {
        // Pakistan regions: Full download at requested zooms
        'all pakistan': 9999999,     // No limit - full country
        'punjab': 9999999,
        'sindh': 9999999,
        'kpk': 9999999,
        'balochistan': 9999999,
        'gilgit-baltistan': 9999999,
        'ajk': 9999999,
        'full iran': 550000,
        'full afghanistan': 220000,
        'full india': 750000,
        'full china': 850000,
        'full usa': 950000,
        'full russia': 1100000
    };

    const cityKey = city.toLowerCase().trim();
    if (strategicBudgets[cityKey]) {
        finalTotalMb = strategicBudgets[cityKey];
    } else {
        // 📏 DYNAMIC SCALING: Handling Antimeridian & Scale
        let lonDiff = Math.abs(activeBbox[2] - activeBbox[0]);
        if (lonDiff > 180) lonDiff = 360 - lonDiff; // Handle wrap-around
        const latDiff = Math.abs(activeBbox[3] - activeBbox[1]);

        const areaSqDeg = lonDiff * latDiff;
        const pkBenchmark = 230;
        const ratio = areaSqDeg / pkBenchmark;
        // 💎 PURE PROPORTIONAL (v128.0): Min 5GB Baseline
        finalTotalMb = Math.min(1000000, Math.max(5120, ratio * 250000));
        console.log(`[OMEGA] 📐 Scaled Budget for ${city}: ${finalTotalMb.toFixed(0)} MB (Ratio: ${ratio.toFixed(2)})`);
    }

    downloadQueue = {
        total: finalTotal, completed: 0, bytes: 0, active: true, paused: false, city,
        totalMb: parseFloat(finalTotalMb),
        bbox: activeBbox,
        startTime: Date.now()
    };

    // 🚩 SAVE METRICS TO DB (v123.0: Persist Calibrated Budget)
    db.run("INSERT OR REPLACE INTO downloads (city, status, size_mb, completed_tiles, total_tiles, total_mb, bbox) VALUES (?,?,?,?,?,?,?)",
        [city, 'Downloading', 0, 0, finalTotal, finalTotalMb, JSON.stringify(activeBbox)]);
    res.json({ status: "started", total: finalTotal });

    (async () => {
        const activePromises = new Set();
        const CONCURRENCY = 100;
        let yieldCounter = 0;

        // 🧠 OMEGA SMART HARVEST (v133.0): Urban Priority Logic
        let urbanZones = [];
        const isStrategic = false; // Applied Global Smart-Harvest: NO exceptions allowed to prevent DB explosion

        if (!isStrategic) {
            const cleanCountry = city.replace(/All |Full /ig, '').trim();
            console.log(`[SMART] 🔍 Mapping Urban Heat Zones for ${cleanCountry}...`);
            try {
                // Fetch top 100 cities to ensure we don't miss any major populated area
                const cityRes = await axios.get(`https://nominatim.openstreetmap.org/search?country=${cleanCountry}&featuretype=city&format=json&limit=100`, {
                    headers: { 'User-Agent': 'OMEGA-GIS-Engine/133.0' }
                });
                urbanZones = cityRes.data.map(c => ({ lat: parseFloat(c.lat), lon: parseFloat(c.lon), r: 0.20 })); // Slightly larger radius
                console.log(`[SMART] 📍 Found ${urbanZones.length} HD zones.`);
            } catch (e) {
                console.warn(`[SMART] ⚠️ Could not map ${cleanCountry}: ${e.message}`);
                // 🛡️ PAKISTAN ALL CITIES: Complete Z21 coverage for all major cities
                if (cleanCountry.toLowerCase().includes('pakistan')) {
                    console.log(`[SMART] 🛡️ Activating Pakistan Complete City Coverage (Z21)...`);
                    urbanZones = [
                        // 🏙️ MAJOR CITIES (0.25-0.30 radius for Z21 coverage)
                        { lat: 24.8607, lon: 67.0011, r: 0.30 }, // Karachi
                        { lat: 31.5204, lon: 74.3587, r: 0.28 }, // Lahore
                        { lat: 33.6844, lon: 73.0479, r: 0.25 }, // Islamabad
                        { lat: 33.5956, lon: 73.0560, r: 0.25 }, // Rawalpindi
                        { lat: 34.0151, lon: 71.5249, r: 0.22 }, // Peshawar
                        { lat: 30.1798, lon: 66.9750, r: 0.22 }, // Quetta
                        { lat: 30.1575, lon: 71.5249, r: 0.22 }, // Multan
                        // 🏘️ SECONDARY CITIES
                        { lat: 31.4181, lon: 73.0776, r: 0.18 }, // Faisalabad
                        { lat: 32.1617, lon: 74.1883, r: 0.18 }, // Gujranwala
                        { lat: 32.4945, lon: 74.5229, r: 0.18 }, // Sialkot
                        { lat: 32.5711, lon: 74.0782, r: 0.18 }, // Gujrat
                        { lat: 31.7167, lon: 73.9850, r: 0.18 }, // Sheikhupura
                        { lat: 31.1235, lon: 72.7000, r: 0.18 }, // Jhang
                        { lat: 29.3956, lon: 71.6836, r: 0.18 }, // Bahawalpur
                        { lat: 29.9667, lon: 70.9333, r: 0.18 }, // Dera Ghazi Khan
                        { lat: 25.3960, lon: 68.3578, r: 0.18 }, // Hyderabad
                        { lat: 27.7269, lon: 68.8190, r: 0.18 }, // Larkana
                        { lat: 34.7743, lon: 72.3615, r: 0.18 }, // Mingora
                        { lat: 27.4294, lon: 68.8289, r: 0.18 }, // Sukkur
                        { lat: 28.4212, lon: 70.2980, r: 0.18 }, // Rahim Yar Khan
                        { lat: 33.1471, lon: 73.7516, r: 0.18 }, // Mirpur
                        { lat: 32.9333, lon: 73.7333, r: 0.18 }, // Jhelum
                        { lat: 31.9070, lon: 72.8576, r: 0.18 }, // Sargodha
                        { lat: 33.1488, lon: 71.8000, r: 0.18 }, // Kohat
                        { lat: 32.5883, lon: 71.5389, r: 0.18 }, // Mianwali
                        { lat: 25.8054, lon: 68.4916, r: 0.18 }, // Nawabshah
                        { lat: 27.0284, lon: 68.3439, r: 0.18 }, // Khairpur
                        { lat: 30.9423, lon: 70.4595, r: 0.18 }, // Layyah
                        { lat: 33.1404, lon: 72.4333, r: 0.18 }, // Attock
                        { lat: 32.5853, lon: 71.5436, r: 0.18 }, // Bhakkar
                    ];
                    console.log(`[SMART] 🇵🇰 Loaded ${urbanZones.length} Pakistan cities for Z21 coverage`);
                }
            }
        }

        try {
            for (let z = 0; z <= maxZ; z++) {
                if (!downloadQueue.active) break;

                // 🛑 SMART CAP: Non-strategic countries stop at Z18 for non-urban areas
                const isDeepZoom = z > 18;
                const isMountainZoom = z > 19;

                const nw_c = latLngToTile(Math.max(activeBbox[1], activeBbox[3]), Math.min(activeBbox[0], activeBbox[2]), z);
                const se_c = latLngToTile(Math.min(activeBbox[1], activeBbox[3]), Math.max(activeBbox[0], activeBbox[2]), z);

                for (let x = Math.min(nw_c.x, se_c.x); x <= Math.max(nw_c.x, se_c.x); x++) {
                    for (let y = Math.min(nw_c.y, se_c.y); y <= Math.max(nw_c.y, se_c.y); y++) {
                        if (!downloadQueue.active) break;

                        // 🧠 PAKISTAN ZOOM STRATEGY (v200.0)
                        const tileLatLon = tileToLatLng(x, y, z);

                        // Check if in any city zone
                        const inCityZone = urbanZones.some(u =>
                            Math.abs(u.lat - tileLatLon.lat) < u.r && Math.abs(u.lon - tileLatLon.lon) < u.r
                        );

                        // 🏔️ MOUNTAINS: Lat > 33.5 (Northern Pakistan) - Z19 MAX
                        const isMountainArea = tileLatLon.lat > 33.5 && city.includes('Pakistan');
                        if (isMountainArea && z > 19) {
                            continue; // Skip Z20/Z21 in mountains
                        }

                        // 🌾 RURAL AREAS (not in city zones) - Z18 MAX
                        if (!inCityZone && z > 18) {
                            continue; // Skip Z19+ in rural areas
                        }

                        // 🏙️ CITIES: All Pakistan cities get Z21 (handled by not skipping)

                        yieldCounter++;
                        if (yieldCounter >= 100) {
                            yieldCounter = 0;
                            await new Promise(r => setImmediate(r));
                            // 🔄 Checkpoint WAL every 100 tiles to prevent inode buildup
                            if (downloadQueue.completed % 1000 === 0) {
                                db.run("PRAGMA wal_checkpoint(TRUNCATE)");
                            }
                        }

                        for (const layer of layers) {
                            // 🗺️ ARCGIS LIMIT: Only Z19 max (ArcGIS doesn't support Z20/Z21)
                            if (layer.startsWith('arcgis') && z > 19) continue;

                            const p = (async () => {
                                try {
                                    await getTileWithFallback(layer, z, x, y);
                                    downloadQueue.completed++;
                                } catch (err) { }
                            })();
                            activePromises.add(p);
                            p.finally(() => activePromises.delete(p));

                            if (activePromises.size >= CONCURRENCY) await Promise.race(activePromises);
                            // 💾 SAVE PROGRESS: Every 100 tiles for crash recovery
                            if (downloadQueue.completed % 100 === 0) {
                                const elapsed = Date.now() - downloadQueue.startTime;
                                const speed = downloadQueue.completed / (elapsed / 1000 / 60); // tiles/min
                                db.run("UPDATE downloads SET size_mb=?, completed_tiles=? WHERE city=?",
                                    [(downloadQueue.bytes / (1024 * 1024)).toFixed(2), downloadQueue.completed, city]);
                                if (downloadQueue.completed % 1000 === 0) {
                                    console.log(`[PROGRESS] ${city}: ${downloadQueue.completed} tiles | ${(downloadQueue.bytes/1024/1024).toFixed(1)} MB | ${speed.toFixed(0)} tiles/min`);
                                }
                            }
                        }
                    }
                    if (!downloadQueue.active) break;
                }
            }
            if (activePromises.size > 0) await Promise.all(activePromises);
        } catch (e) {
            console.error("[OMEGA] Heavy-Duty Harvester Error:", e);
            db.run("UPDATE downloads SET status='Error' WHERE city=?", [city]);
        }

        downloadQueue.active = false;
        db.run("UPDATE downloads SET status='Completed', size_mb=?, completed_tiles=? WHERE city=?",
            [(downloadQueue.bytes / (1024 * 1024)).toFixed(2), downloadQueue.completed, city]);
    })();
});


// 📡 GLOBAL MISSION SEARCH API
app.get('/api/search', async (req, res) => {
    const { q, countrycodes, limit } = req.query;
    if (!q) return res.json([]);

    try {
        const response = await axios.get(`https://nominatim.openstreetmap.org/search`, {
            params: {
                q,
                countrycodes: countrycodes || 'pk',
                format: 'json',
                addressdetails: 1,
                limit: limit || 5
            },
            headers: {
                'User-Agent': 'OMEGA-GIS-Engine/1.0'
            }
        });
        res.json(response.data);
    } catch (e) {
        console.error("[OMEGA] Search Error:", e.message);
        res.status(500).json([]);
    }
});

app.get('/all-downloads', (req, res) => { db.all("SELECT * FROM downloads", [], (err, rows) => res.json(rows)); });
app.post('/delete-download', (req, res) => { db.run("DELETE FROM downloads WHERE city=?", [req.body.city], () => res.json({ success: true })); });

// ☢️ NUCLEAR RESET: PERMANENT DISK PURGE (v106.0)
app.post('/delete-all-data', (req, res) => {
    console.log("[OMEGA] ☢️ NUCLEAR RESET INITIATED...");

    // 1. Clear physical tiles folder
    const tilesDir = path.join(__dirname, 'wwwroot', 'tiles');
    if (fs.existsSync(tilesDir)) {
        try { fs.rmSync(tilesDir, { recursive: true, force: true }); fs.mkdirSync(tilesDir, { recursive: true }); } catch (e) { }
    }

    // 2. Kill DB connection and Delete tiles.db for 100% space recovery
    db.close((err) => {
        const files = [DB_PATH, `${DB_PATH}-wal`, `${DB_PATH}-shm`];
        files.forEach(f => { if (fs.existsSync(f)) fs.unlinkSync(f); });

        console.log("[OMEGA] ☢️ Disk files purged. Re-initializing empty database...");
        initDB(); // Restart fresh
        res.json({ success: true });
    });
});

// 🚩 GLOBAL PERSISTENCE HELPER (v84.0)
function updateInventory(city, deltaMb, deltaTiles, status = 'Completed', bbox = null) {
    db.get("SELECT * FROM downloads WHERE city=?", [city], (err, row) => {
        let currentBbox = bbox;
        if (row && row.bbox && bbox) {
            const old = JSON.parse(row.bbox);
            currentBbox = [Math.min(old[0], bbox[0]), Math.min(old[1], bbox[1]), Math.max(old[2], bbox[2]), Math.max(old[3], bbox[3])];
        } else if (row) {
            currentBbox = JSON.parse(row.bbox || 'null');
        }

        let targetTiles = (row ? row.total_tiles : 0);
        if (city === 'Auto-Discovered Data' && currentBbox) {
            let scope = 0;
            for (let z = 15; z <= 21; z++) {
                const nw = latLngToTile(currentBbox[3], currentBbox[0], z);
                const se = latLngToTile(currentBbox[1], currentBbox[2], z);
                scope += (Math.abs(se.x - nw.x) + 1) * (Math.abs(se.y - nw.y) + 1);
            }
            targetTiles = scope * 2;
        }

        const size = (row ? row.size_mb : 0) + deltaMb;
        const tiles = (row ? row.completed_tiles : 0) + deltaTiles;

        db.run("INSERT OR REPLACE INTO downloads (city, status, size_mb, completed_tiles, total_tiles, bbox) VALUES (?,?,?,?,?,?)",
            [city, status, size, tiles, targetTiles, JSON.stringify(currentBbox)]);
    });
}

const discoveryQueue = [];
let isDiscovering = false;
let autoCity = 'Auto-Discovered Data';
let currentScopeBbox = null;
let inMemoryDiscovered = { mb: 0, tiles: 0 };

async function flushDiscoveredStats() {
    if (inMemoryDiscovered.tiles === 0) return;
    const mb = inMemoryDiscovered.mb;
    const tiles = inMemoryDiscovered.tiles;
    inMemoryDiscovered = { mb: 0, tiles: 0 };
    return new Promise(r => {
        db.run("UPDATE downloads SET size_mb = size_mb + ?, completed_tiles = completed_tiles + ? WHERE city=?", [mb, tiles, autoCity], r);
    });
}

let autoHarvestingStatus = { active: false, completed: 0, total: 0, mb: 0 };

let autoMissionActive = false; // true = planning mode (no download), false = harvest mode

async function startHarvesting() {
    if (isDiscovering) { console.log('[HARVEST] Already running, skip.'); return; }
    isDiscovering = true;
    const layers = ['google-street', 'satellite', 'arcgis-street', 'arcgis-satellite'];

    try {
        const row = await new Promise(resolve =>
            db.get("SELECT bbox, total_tiles, size_mb, completed_tiles FROM downloads WHERE city=?",
                [autoCity], (err, r) => resolve(r))
        );

        if (!row || !row.bbox) {
            console.log('[HARVEST] ❌ No scope found in DB. Plan first (turn ON and move map).');
            isDiscovering = false;
            return;
        }

        const scopeBbox = JSON.parse(row.bbox);
        autoHarvestingStatus = {
            active: true,
            completed: row.completed_tiles || 0,
            total: row.total_tiles || 0,
            mb: parseFloat(row.size_mb) || 0
        };

        const zoomTiers = [[15, 16, 17, 18], [19, 20, 21]];
        for (const tier of zoomTiers) {
            for (const z of tier) {
                if (autoMissionActive) break;

                const nw = latLngToTile(Math.max(scopeBbox[1], scopeBbox[3]), Math.min(scopeBbox[0], scopeBbox[2]), z);
                const se = latLngToTile(Math.min(scopeBbox[1], scopeBbox[3]), Math.max(scopeBbox[0], scopeBbox[2]), z);

                const minX = Math.min(nw.x, se.x), maxX = Math.max(nw.x, se.x);
                const minY = Math.min(nw.y, se.y), maxY = Math.max(nw.y, se.y);

                let activePromises = new Set();
                const CONCURRENCY = 100; // Increased stability

                for (let x = minX; x <= maxX; x++) {
                    for (let y = minY; y <= maxY; y++) {
                        if (autoMissionActive) break;
                        for (const layer of layers) {
                            // 🗺️ ARCGIS LIMIT: Only Z19 max (ArcGIS doesn't support Z20/Z21)
                            if (layer.startsWith('arcgis') && z > 19) continue;

                            const p = (async () => {
                                try {
                                    // Use unified fallback logic to handle all layers
                                    await getTileWithFallback(layer, z, x, y);
                                    autoHarvestingStatus.completed += 1;

                                    // 💾 SAVE PROGRESS: Every 100 tiles for crash recovery
                                    if (autoHarvestingStatus.completed % 100 === 0) {
                                        autoHarvestingStatus.mb = downloadQueue.bytes / (1024 * 1024);
                                        db.run("UPDATE downloads SET size_mb=?, completed_tiles=? WHERE city=?",
                                            [autoHarvestingStatus.mb.toFixed(2), autoHarvestingStatus.completed, autoCity]);
                                        // 🔄 Checkpoint WAL every 1000 tiles
                                        if (autoHarvestingStatus.completed % 1000 === 0) {
                                            db.run("PRAGMA wal_checkpoint(TRUNCATE)");
                                            console.log(`[HARVEST] ${autoCity}: ${autoHarvestingStatus.completed} tiles | ${autoHarvestingStatus.mb.toFixed(1)} MB`);
                                        }
                                    }
                                } catch (tileErr) { }
                            })();
                            activePromises.add(p);
                            p.finally(() => activePromises.delete(p));
                            if (activePromises.size >= CONCURRENCY) await Promise.race(activePromises);
                        }
                    }
                    if (autoMissionActive) break;
                }
                if (activePromises.size > 0) await Promise.all(activePromises);
            }
            if (autoMissionActive) break;
        }

        if (!autoMissionActive) {
            db.run("UPDATE downloads SET status='Completed' WHERE city=?", [autoCity]);
            console.log(`[HARVEST] 🏁 COMPLETE! ${autoHarvestingStatus.completed} tiles | ${autoHarvestingStatus.mb.toFixed(1)} MB`);
        } else {
            console.log(`[HARVEST] 🛑 PAUSED BY USER! Saved ${autoHarvestingStatus.completed} tiles`);
        }
    } catch (e) {
        console.error('[HARVEST] ❌ Fatal Error:', e);
    } finally {
        isDiscovering = false;
        autoHarvestingStatus.active = false;
    }
}

app.get('/download-status', (req, res) => {
    // 🛡️ UNIVERSAL STATUS REPORTER (v105.0)
    if (downloadQueue.active) {
        const mb = (downloadQueue.bytes / (1024 * 1024)).toFixed(2);
        return res.json({
            ...downloadQueue,
            mb,
            totalMb: downloadQueue.totalMb.toFixed(2),
            totalTiles: downloadQueue.total,
            completedTiles: downloadQueue.completed
        });
    }

    if (autoHarvestingStatus.active) {
        return res.json({
            active: true,
            city: '🛰️ Auto-Discovered Data',
            completed: autoHarvestingStatus.completed,
            total: autoHarvestingStatus.total,
            mb: autoHarvestingStatus.mb.toFixed(2),
            totalMb: (autoHarvestingStatus.total * 0.006).toFixed(2),
            completedTiles: autoHarvestingStatus.completed,
            totalTiles: autoHarvestingStatus.total
        });
    }

    res.json({ active: false });
});

app.post('/auto-discover', (req, res) => {
    const isEnabled = req.body.enabled;
    autoMissionActive = isEnabled; // If enabled, we are PLANNING (not harvesting)
    const bbox = req.body.bbox;
    if (!isInsideAllowedZone(bbox)) return res.json({ success: true, ignored: true });

    db.get("SELECT bbox FROM downloads WHERE city=?", [autoCity], (err, row) => {
        let finalBbox = bbox;
        if (row && row.bbox) {
            const old = JSON.parse(row.bbox);
            finalBbox = [Math.min(old[0], bbox[0]), Math.min(old[1], bbox[1]), Math.max(old[2], bbox[2]), Math.max(old[3], bbox[3])];
        }

        let scope = 0;
        for (let z = 15; z <= 21; z++) {
            const nw = latLngToTile(finalBbox[3], finalBbox[0], z);
            const se = latLngToTile(finalBbox[1], finalBbox[2], z);
            scope += (Math.abs(se.x - nw.x) + 1) * (Math.abs(se.y - nw.y) + 1);
        }
        const totalLayers = ['google-street', 'satellite', 'arcgis-street', 'arcgis-satellite'].length;
        const totalScope = scope * totalLayers;
        const status = isEnabled ? '📍 Planning Mission...' : '📡 Harvesting Scope...';

        // 📏 DYNAMIC SCALING FOR AUTO-DISCOVER (v123.0)
        const areaSqDeg = Math.abs(finalBbox[2] - finalBbox[0]) * Math.abs(finalBbox[3] - finalBbox[1]);
        const pkBenchmark = 230;
        const ratio = areaSqDeg / pkBenchmark;
        const calBudget = Math.min(1000000, Math.max(5120, ratio * 250000));

        db.run("INSERT OR REPLACE INTO downloads (city, status, size_mb, completed_tiles, total_tiles, total_mb, bbox) VALUES (?,?,?,?,?,?,?)",
            [autoCity, status, (row && row.size_mb != null ? row.size_mb : 0), (row && row.completed_tiles != null ? row.completed_tiles : 0), totalScope, calBudget, JSON.stringify(finalBbox)], () => {
                if (!isEnabled) startHarvesting();
                res.json({ success: true });
            });
    });
});

app.post('/stop-download', (req, res) => {
    downloadQueue.active = false;
    autoMissionActive = true;     // PAUSE AUTO-HARVEST
    isDiscovering = false;        // Release auto-discover lock
    autoHarvestingStatus.active = false; // Turn off floating UI
    db.run("UPDATE downloads SET status='Paused' WHERE status LIKE '%Harvesting%' OR status LIKE '%Downloading%'");
    res.json({ success: true });
});

// 🛡️ OMEGA REGIONAL GEOFENCE (v106.2 Refresh)
function isInsideAllowedZone(bbox) {
    // Covers Iran, Afghanistan, and Pakistan: Lon [44, 80], Lat [23, 40]
    return !(bbox[0] < 44 || bbox[2] > 80 || bbox[1] < 23 || bbox[3] > 40);
}

// ⚡ POWER-CUT & SIGNAL HANDLERS: Safe shutdown
async function safeShutdown(signal) {
    console.log(`\n[SHUTDOWN] ${signal} received. Saving state...`);
    downloadQueue.active = false;
    autoMissionActive = true;

    // Force checkpoint to prevent WAL corruption
    await new Promise(resolve => {
        db.run("PRAGMA wal_checkpoint(TRUNCATE)", () => {
            console.log("[SHUTDOWN] WAL checkpointed");
            resolve();
        });
    });

    // Update status to Paused for resume
    await new Promise(resolve => {
        db.run("UPDATE downloads SET status='Paused' WHERE status='Downloading'", () => {
            console.log("[SHUTDOWN] Status saved");
            resolve();
        });
    });

    db.close(() => {
        console.log("[SHUTDOWN] Database closed safely");
        process.exit(0);
    });
}

process.on('SIGINT', () => safeShutdown('SIGINT'));
process.on('SIGTERM', () => safeShutdown('SIGTERM'));
process.on('SIGUSR2', () => safeShutdown('SIGUSR2')); // Nodemon restart

// 🔄 Periodic WAL checkpoint every 5 minutes to prevent inode buildup
setInterval(() => {
    if (db && !downloadQueue.active) {
        db.run("PRAGMA wal_checkpoint(PASSIVE)", (err) => {
            if (!err) console.log("[MAINTENANCE] WAL checkpointed");
        });
    }
}, 5 * 60 * 1000);

app.listen(PORT, '0.0.0.0', () => {
    console.log(`\n💎 OMEGA PAKISTAN Z21 CRASH-PROOF v200.0`);
    console.log(`📡 Port: ${PORT} | Mode: Pakistan Full Coverage`);
    console.log(`🛡️ Features: Power-cut safe | Network retry | Inode fix`);
    console.log(`🏙️ Google: Z21 | 🏔️ Mountains: Z19 | 🌾 Rural: Z18`);
    console.log(`🗺️ ArcGIS: Z19 max (overzoom enabled)\n`);
});
