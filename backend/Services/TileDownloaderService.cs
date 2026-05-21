using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace PakistanMaps.Services
{
    public class TileDownloaderService : BackgroundService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<TileDownloaderService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ConcurrentQueue<DownloadTask> _queue = new();
        private readonly ConcurrentDictionary<string, (long completed, long total, double sizeMb, bool paused)> _progressTracker = new();
        private readonly SemaphoreSlim _ioSemaphore = new(64); // Prevent disk saturation

        public TileDownloaderService(IConfiguration config, ILogger<TileDownloaderService> logger, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public void EnqueueDownload(string city, double[] bbox)
        {
            _queue.Enqueue(new DownloadTask { City = city, Bbox = bbox });
        }

        public (long completed, long total, double sizeMb, bool paused)? GetProgress(string city)
        {
            if (_progressTracker.TryGetValue(city, out var progress)) return progress;
            return null;
        }

        public string? GetActiveCity()
        {
            return _progressTracker.Keys.FirstOrDefault();
        }

        public void ClearProgress(string city)
        {
            _progressTracker.TryRemove(city, out _);
        }

        private bool _isInternetAvailable = true;
        private int _consecutiveErrors = 0;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 🔄 STARTUP RECOVERY: Re-enqueue unfinished tasks from DB
            string pgConnString = _config.GetConnectionString("PostgreSQLConnection")!;
            try {
                using var conn = new NpgsqlConnection(pgConnString);
                // Optimization: Only select what's needed
                var unfinished = await conn.QueryAsync<DownloadTask>("SELECT city, bbox_json as BboxJson FROM downloads WHERE status IN ('Queued', 'Harvesting Raster Tiles', 'Harvesting', 'Paused')");
                foreach (var task in unfinished) {
                    if (!string.IsNullOrEmpty(task.BboxJson)) {
                        task.Bbox = JsonSerializer.Deserialize<double[]>(task.BboxJson) ?? Array.Empty<double>();
                        EnqueueDownload(task.City, task.Bbox);
                    }
                }
            } catch (Exception ex) { _logger.LogError($"[OMEGA SHIELD] Recovery Error: {ex.Message}"); }

            // 🚀 BATCH UPDATE ENGINE: Periodically sync progress to DB to reduce load
            _ = Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try {
                        await Task.Delay(5000, stoppingToken);
                        await SyncProgressToDbAsync();
                        
                        // Self-Healing: Reset error counter if DB is responsive
                        if (_consecutiveErrors > 100) _consecutiveErrors = 50; 
                    } catch { /* Silent */ }
                }
            }, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_queue.TryDequeue(out var task))
                {
                    try { await ProcessDownloadAsync(task, stoppingToken); }
                    catch (Exception ex) {
                        _logger.LogError(ex, $"[OMEGA GUARDIAN] Unhandled crash for {task.City}. Service protected.");
                        try {
                            using var ec = new NpgsqlConnection(_config.GetConnectionString("PostgreSQLConnection")!);
                            await ec.ExecuteAsync("UPDATE downloads SET status = 'Error' WHERE city = @City", new { task.City });
                        } catch { /* DB also unavailable — skip */ }
                    }
                }
                else
                {
                    await Task.Delay(5000, stoppingToken); // Slower idle polling to save CPU
                }
            }
        }

        private async Task SyncProgressToDbAsync()
        {
            string pgConnString = _config.GetConnectionString("PostgreSQLConnection")!;
            foreach (var entry in _progressTracker)
            {
                try
                {
                    using var conn = new NpgsqlConnection(pgConnString);
                    await conn.ExecuteAsync("UPDATE downloads SET completed_tiles = @Completed, size_mb = @SizeMb WHERE city = @City", 
                        new { Completed = entry.Value.completed, SizeMb = entry.Value.sizeMb, City = entry.Key });
                }
                catch (Exception ex) { _logger.LogWarning($"DB Sync Lag: {ex.Message}"); }
            }
        }

        private async Task ProcessDownloadAsync(DownloadTask task, CancellationToken stoppingToken)
        {
            string pgConnString = _config.GetConnectionString("PostgreSQLConnection")!;
            try
            {
                _logger.LogInformation($"[OMEGA] Starting RASTER Harvesting for {task.City}");

                using (var pgConn = new NpgsqlConnection(pgConnString))
                {
                    await pgConn.OpenAsync();
                    await pgConn.ExecuteAsync("UPDATE downloads SET status = 'Harvesting Raster Tiles' WHERE city = @City", new { task.City });
                }

                string wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "tiles");
                string cityPath = Path.Combine(wwwrootPath, task.City.Replace(" ", "_"));
                if (!Directory.Exists(cityPath)) Directory.CreateDirectory(cityPath);

                // 🚀 3-LAYER ARCHITECTURE - OPTIMIZED for 8TB - ALL COUNTRIES
                // 🌾 Z0-Z17: 3 layers full country (Google St, Google Sat, ArcGIS St)
                // 🏘️ Z18: 3 layers cities only
                // 🏙️ Z19-Z21: 2 layers cities only (NO ArcGIS at Z20-Z21)
                long totalTiles = PakistanMaps.Utils.TileCalculator.GetTotalTilesForBBox(task.Bbox, 0, 21, task.City);
                // totalTiles now includes layer multiplier
                // 8TB max limit for Full Pakistan
                double estimatedMb = Math.Min(8000000, totalTiles * 0.006); // 6KB per tile file, cap at 8TB 

                // 🚀 ATOMIC RECOVERY: Pre-scan folder (fast async, 30s timeout to prevent OOM)
                _logger.LogInformation($"[OMEGA] Scanning existing tiles for {task.City}...");
                long completed = 0;
                if (Directory.Exists(cityPath)) {
                    try {
                        using var scanCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        completed = await Task.Run(() => {
                            long count = 0;
                            foreach (var _ in Directory.EnumerateFiles(cityPath, "*.png", SearchOption.AllDirectories))
                            { if (scanCts.IsCancellationRequested) break; count++; }
                            return count;
                        }, scanCts.Token);
                    } catch { completed = 0; }
                }
                
                // 🚀 FORCE INITIAL UI SYNC: Let the UI know we are starting
                using (var pgConn = new NpgsqlConnection(pgConnString)) {
                    // completed is file count, totalTiles is file count (includes layers)
                    long completedFiles = Math.Min(completed, totalTiles);
                    await pgConn.ExecuteAsync("UPDATE downloads SET completed_tiles = @Completed, total_mb = @TotalMb WHERE city = @City", 
                        new { Completed = completedFiles, TotalMb = estimatedMb, task.City });
                    completed = completedFiles; // Keep as file count
                }

                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                // 🚀 DOWNLOAD ENGINE
                using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                long localCounter = 0;
                long localCompleted = completed;
                bool isPaused = false;
                _progressTracker[task.City] = (localCompleted, totalTiles, localCompleted * 0.006, false);

                // Pass the task-specific token to the iterator
                // 🧠 SMART GENERATOR: Z0-Z19 full bbox + Z20-Z21 only for cities
                var tileSource = GetTilesSmart(task.Bbox, task.City, taskCts.Token);

                _logger.LogInformation($"[OMEGA] 🚀 Download engine STARTED for {task.City}. Total={totalTiles}, AlreadyDone={localCompleted}");

                await Parallel.ForEachAsync(tileSource, new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = taskCts.Token }, async (tile, ct) =>
                {
                    // ⏸️ SMART PAUSE/RESUME ENGINE
                    while (isPaused && !ct.IsCancellationRequested) {
                        await Task.Delay(1000, ct);
                        
                        // 🛰️ MASTER SYNC: While paused, threads occasionally check if they should wake up
                        if (Interlocked.Increment(ref localCounter) % 50 == 0) {
                             try {
                                using var checkConn = new NpgsqlConnection(pgConnString);
                                var t = await checkConn.QueryFirstOrDefaultAsync<dynamic>("SELECT status FROM downloads WHERE city = @City", new { City = task.City ?? "" });
                                string? s = t?.status?.ToString();
                                isPaused = (s == "Paused");
                                
                                if (string.IsNullOrEmpty(s) || s == "Stopped") { taskCts.Cancel(); return; }
                            } catch { /* DB Busy */ }
                        }
                    }

                    // ⏸️ DB SYNC CHECK (During active work)
                    if (Math.Abs(Interlocked.Increment(ref localCounter)) % 100 == 0)
                    {
                        try {
                            using var checkConn = new NpgsqlConnection(pgConnString);
                            var currentTask = await checkConn.QueryFirstOrDefaultAsync<dynamic>("SELECT status FROM downloads WHERE city = @City", new { City = task.City ?? "" });
                            string? status = currentTask?.status?.ToString();
                            
                            isPaused = (status == "Paused");

                            // Update memory tracker for UI
                            if (task.City != null && _progressTracker.TryGetValue(task.City, out var currentProgress))
                            {
                                _progressTracker[task.City] = (currentProgress.completed, currentProgress.total, currentProgress.sizeMb, isPaused);
                            }

                            if (string.IsNullOrEmpty(status) || status == "Stopped" || status == "Error") {
                                taskCts.Cancel(); 
                                return;
                            }
                        } catch { /* DB Busy */ }
                    }

                    // 🛰️ 3-LAYER HARVESTING (Google Street, Google Satellite, ArcGIS Street ONLY)
                    // 🗺️ ARCGIS LIMIT: Z19 max (ArcGIS doesn't support Z20/Z21)
                    var layers = new List<(string Name, string Url)> {
                        ("street",    $"https://mt1.google.com/vt/lyrs=m&x={tile.X}&y={tile.Y}&z={tile.Z}"),
                        ("satellite", $"https://mt1.google.com/vt/lyrs=y&x={tile.X}&y={tile.Y}&z={tile.Z}")
                    };
                    
                    // Add ArcGIS Street ONLY for Z19 and below (NO ArcGIS Satellite)
                    if (tile.Z <= 19) {
                        layers.Add(("arcgis-street", $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{tile.Z}/{tile.Y}/{tile.X}"));
                    }


                    foreach (var layer in layers)
                    {
                        string layerPath = Path.Combine(cityPath, layer.Name, tile.Z.ToString(), tile.X.ToString());
                        string filePath = Path.Combine(layerPath, $"{tile.Y}.png");
                        string tmpPath = filePath + ".tmp";

                        if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
                        {
                            try {
                                // 🛡️ INTERNET AUTO-PAUSE: If internet is down, wait instead of failing
                                while (!_isInternetAvailable && !ct.IsCancellationRequested) {
                                    await Task.Delay(5000, ct);
                                    
                                    // 🛡️ CRITICAL FIX: Check if task was stopped/deleted while we were waiting
                                    if (Interlocked.CompareExchange(ref localCounter, 0, 0) % 50 == 0) {
                                        try {
                                            using var checkConn = new NpgsqlConnection(pgConnString);
                                            var currentTask = await checkConn.QueryFirstOrDefaultAsync<dynamic>("SELECT status FROM downloads WHERE city = @City", new { City = task.City ?? "" });
                                            string? status = currentTask?.status?.ToString();
                                            if (string.IsNullOrEmpty(status) || status == "Stopped" || status == "Error") {
                                                taskCts.Cancel(); 
                                                break;
                                            }
                                        } catch { /* DB Busy */ }
                                    }
                                }

                                if (ct.IsCancellationRequested) break;

                                var data = await client.GetByteArrayAsync(layer.Url, ct);
                                
                                // 🛡️ ATOMIC WRITE: Write to .tmp first then Move (Power-Off Proof)
                                await _ioSemaphore.WaitAsync(ct);
                                try {
                                    if (!Directory.Exists(layerPath)) Directory.CreateDirectory(layerPath);
                                    
                                    await File.WriteAllBytesAsync(tmpPath, data, ct);
                                    if (File.Exists(filePath)) File.Delete(filePath);
                                    File.Move(tmpPath, filePath);
                                    
                                    _consecutiveErrors = 0;
                                    _isInternetAvailable = true;
                                } finally { _ioSemaphore.Release(); }
                            } 
                            catch (Exception) { 
                                // Detect internet loss - Increased threshold for 256 threads
                                Interlocked.Increment(ref _consecutiveErrors);
                                if (_consecutiveErrors > 200) {
                                    _isInternetAvailable = false;
                                    _logger.LogWarning("[OMEGA SHIELD] Connectivity issues detected. Engine entering standby...");
                                }
                                
                                // Cleanup tmp if write failed
                                if (File.Exists(tmpPath)) File.Delete(tmpPath);
                            }
                        }
                    }

                    // 🛰️ PROGRESS UPDATE (In-Memory)
                    long current = Interlocked.Increment(ref localCompleted);
                    if (task.City != null && _progressTracker.TryGetValue(task.City, out var p))
                    {
                        // current and totalTiles are already file counts (include layers)
                        _progressTracker[task.City] = (current, totalTiles, current * 0.006, p.paused);
                    }
                });

                _progressTracker.TryRemove(task.City, out _);

                using (var pgConn = new NpgsqlConnection(pgConnString))
                {
                    await pgConn.ExecuteAsync("UPDATE downloads SET status = 'Completed', completed_tiles = @Total WHERE city = @City", 
                        new { Total = totalTiles, task.City });
                }
                _logger.LogInformation($"[OMEGA] Raster Harvesting complete for {task.City}!");
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException) return;
                _logger.LogError(ex, $"[OMEGA] Error harvesting {task.City}");
                try {
                    using var pgConn = new NpgsqlConnection(pgConnString);
                    await pgConn.ExecuteAsync("UPDATE downloads SET status = 'Error' WHERE city = @City", new { task.City });
                } catch { /* DB unavailable during error recovery — ignore */ }
            }
        }

        private async Task<bool> ConvertFolderToPMTilesAsync(string folderPath, string pmtilesPath, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"[PMTiles Pipeline] Converting Folder {folderPath} to {pmtilesPath}...");
                
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "pmtiles",
                    Arguments = $"convert \"{folderPath}\" \"{pmtilesPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process == null) return false;

                await process.WaitForExitAsync(cancellationToken);
                
                if (process.ExitCode == 0 && File.Exists(pmtilesPath))
                {
                    _logger.LogInformation($"[PMTiles Pipeline] Conversion successful!");
                    return true;
                }
                else
                {
                    string error = await process.StandardError.ReadToEndAsync(cancellationToken);
                    _logger.LogError($"[PMTiles Pipeline] Error: {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PMTiles Pipeline] Exception during conversion. Ensure 'pmtiles.exe' is installed and in system PATH.");
                return false;
            }
        }

        private IEnumerable<(int Z, int X, int Y)> GetTilesForBBox(double[] bbox, int minZoom, int maxZoom, CancellationToken ct)
        {
            if (bbox == null || bbox.Length < 4) yield break;

            double centerLon = (bbox[0] + bbox[2]) / 2.0;
            double centerLat = (bbox[1] + bbox[3]) / 2.0;

            for (int z = maxZoom; z >= minZoom; z--)
            {
                if (ct.IsCancellationRequested) yield break;

                int minX = LonToTileX(bbox[0], z);
                int maxX = LonToTileX(bbox[2], z);
                int minY = LatToTileY(bbox[3], z); 
                int maxY = LatToTileY(bbox[1], z);

                int midX = LonToTileX(centerLon, z);
                int midY = LatToTileY(centerLat, z);

                // For massive areas (Billion+ tiles), we skip sorting and stream directly to save RAM and CPU
                long zoomCount = (long)(maxX - minX + 1) * (maxY - minY + 1);
                
                if (zoomCount > 500000) 
                {
                    // 🚀 STREAMING MODE: No sorting for large zooms to prevent OOM
                    for (int x = minX; x <= maxX; x++)
                    {
                        for (int y = minY; y <= maxY; y++)
                        {
                            if (ct.IsCancellationRequested) yield break;
                            yield return (z, x, y);
                        }
                    }
                }
                else 
                {
                    // 🎯 PRECISION MODE: Sort by distance for smaller zooms
                    var zoomTiles = new List<(int X, int Y, double Dist)>();
                    for (int x = minX; x <= maxX; x++)
                    {
                        for (int y = minY; y <= maxY; y++)
                        {
                            double dist = Math.Sqrt(Math.Pow(x - midX, 2) + Math.Pow(y - midY, 2));
                            zoomTiles.Add((x, y, dist));
                        }
                    }
                    foreach (var t in zoomTiles.OrderBy(t => t.Dist))
                    {
                        if (ct.IsCancellationRequested) yield break;
                        yield return (z, t.X, t.Y);
                    }
                }
            }
        }

        private int LonToTileX(double lon, int z) =>
            (int)Math.Floor((lon + 180.0) / 360.0 * (1 << z));

        private int LatToTileY(double lat, int z)
        {
            double latRad = lat * Math.PI / 180.0;
            return (int)(Math.Floor((1.0 - Math.Asinh(Math.Tan(latRad)) / Math.PI) / 2.0 * (1 << z)));
        }

        // 🧠 SMART TILE GENERATOR - 8TB OPTIMIZED - ALL COUNTRIES
        // 🌾 Z0-Z17: Full country | 🏘️ Z18: Cities | 🏙️ Z19-Z21: Cities
        // 🗺️ ARCGIS: Z19 max | 3 layers: Z0-Z19 | 2 layers: Z20-Z21
        private IEnumerable<(int Z, int X, int Y)> GetTilesSmart(double[] bbox, string country, CancellationToken ct)
        {
            if (bbox == null || bbox.Length < 4) yield break;
            
            // Select appropriate city zones
            var cityZones = IsPakistan(country) 
                ? PakistanMaps.Utils.TileCalculator.PakistanCities 
                : PakistanMaps.Utils.TileCalculator.WorldCities;

            // Phase 1: 🌾 Z0-Z17 Full country (all areas — roads visible everywhere)
            foreach (var tile in GetTilesForBBox(bbox, 0, 17, ct))
                yield return tile;

            // Phase 2: 🏘️ Z18 only inside populated city bounding boxes (cities/towns)
            foreach (var zone in cityZones)
            {
                if (ct.IsCancellationRequested) yield break;
                double iMinLon = Math.Max(bbox[0], zone.minLon);
                double iMinLat = Math.Max(bbox[1], zone.minLat);
                double iMaxLon = Math.Min(bbox[2], zone.maxLon);
                double iMaxLat = Math.Min(bbox[3], zone.maxLat);
                if (iMinLon >= iMaxLon || iMinLat >= iMaxLat) continue;
                double[] zoneBbox = [iMinLon, iMinLat, iMaxLon, iMaxLat];
                foreach (var tile in GetTilesForBBox(zoneBbox, 18, 18, ct))
                    yield return tile;
            }

            // Phase 3: 🏙️ Z19-Z21 only inside populated city bounding boxes
            foreach (var zone in cityZones)
            {
                if (ct.IsCancellationRequested) yield break;
                double iMinLon = Math.Max(bbox[0], zone.minLon);
                double iMinLat = Math.Max(bbox[1], zone.minLat);
                double iMaxLon = Math.Min(bbox[2], zone.maxLon);
                double iMaxLat = Math.Min(bbox[3], zone.maxLat);
                if (iMinLon >= iMaxLon || iMinLat >= iMaxLat) continue;
                double[] zoneBbox = [iMinLon, iMinLat, iMaxLon, iMaxLat];
                foreach (var tile in GetTilesForBBox(zoneBbox, 19, 21, ct))
                    yield return tile;
            }
        }

        // � HELPER: Check if country is Pakistan
        private static bool IsPakistan(string country)
        {
            if (string.IsNullOrEmpty(country)) return false;
            var pakNames = new[] { "pakistan", "all pakistan", "punjab", "sindh", "kpk", "balochistan", "gilgit", "ajk", "kashmir" };
            return pakNames.Any(p => country.ToLower().Contains(p));
        }

        private static (double Lat, double Lon) TileToLatLon(int x, int y, int z)
        {
            double n   = Math.PI - 2.0 * Math.PI * y / Math.Pow(2.0, z);
            double lon = (double)x / Math.Pow(2.0, z) * 360.0 - 180.0;
            double lat = 180.0 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
            return (lat, lon);
        }

        private static bool IsInPopulatedZone(double lat, double lon, string country)
        {
            var cityZones = IsPakistan(country) 
                ? PakistanMaps.Utils.TileCalculator.PakistanCities 
                : PakistanMaps.Utils.TileCalculator.WorldCities;
            foreach (var z in cityZones)
                if (lat >= z.minLat && lat <= z.maxLat && lon >= z.minLon && lon <= z.maxLon)
                    return true;
            return false;
        }
    }

    public class DownloadTask
    {
        public string City { get; set; } = string.Empty;
        public double[] Bbox { get; set; } = Array.Empty<double>();
        public string? BboxJson { get; set; }
    }
}
