using System.Collections.Concurrent;
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
                    await ProcessDownloadAsync(task, stoppingToken);
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

                // 🚀 QUAD-LAYER ARCHITECTURE (Street, Satellite, ArcGIS, Night)
                long totalTiles = PakistanMaps.Utils.TileCalculator.GetTotalTilesForBBox(task.Bbox, 0, 21);
                // Size Estimation: 488MB for 129,240 files (32,310 locations * 4 layers) 
                // -> 488 / 129240 = ~0.00378 MB per file. 
                // Since totalTiles is unique locations, 4 layers per location = 0.00378 * 4 = 0.01512 MB per location set
                double estimatedMb = (totalTiles * 0.01512); 

                // 🚀 ATOMIC RECOVERY: Pre-scan folder to find already downloaded tiles
                _logger.LogInformation($"[OMEGA] Scanning existing tiles for {task.City}...");
                long completed = 0;
                if (Directory.Exists(cityPath)) {
                    // Use EnumerateFiles to avoid hanging on millions of files
                    completed = Directory.EnumerateFiles(cityPath, "*.png", SearchOption.AllDirectories).Count();
                }
                
                // 🚀 FORCE INITIAL UI SYNC: Let the UI know we are starting
                using (var pgConn = new NpgsqlConnection(pgConnString)) {
                    // Since totalTiles is based on unique locations, and we have 4 layers, 
                    // we divide 'completed' file count by 4 to get location-based progress.
                    long completedLocations = Math.Min(completed / 4, totalTiles);
                    await pgConn.ExecuteAsync("UPDATE downloads SET completed_tiles = @Completed, total_mb = @TotalMb WHERE city = @City", 
                        new { Completed = completedLocations, TotalMb = estimatedMb, task.City });
                    completed = completedLocations; // Reset local counter to start from unique location count
                }

                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                // 🚀 TURBO-CHARGED ENGINE (Adaptive Concurrency)
                using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                long localCounter = 0;
                long localCompleted = completed;
                bool isPaused = false;
                _progressTracker[task.City] = (localCompleted, totalTiles, localCompleted * 0.01512, false);

                // Pass the task-specific token to the iterator
                var tileSource = GetTilesForBBox(task.Bbox, 0, 21, taskCts.Token);

                await Parallel.ForEachAsync(tileSource, new ParallelOptions { MaxDegreeOfParallelism = 256, CancellationToken = taskCts.Token }, async (tile, ct) =>
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

                    // 🛰️ MULTI-LAYER HARVESTING
                    var layers = new[] {
                        (Name: "street", Url: $"https://mt1.google.com/vt/lyrs=m&x={tile.X}&y={tile.Y}&z={tile.Z}"),
                        (Name: "satellite", Url: $"https://mt1.google.com/vt/lyrs=y&x={tile.X}&y={tile.Y}&z={tile.Z}"),
                        (Name: "arcgis", Url: $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{tile.Z}/{tile.Y}/{tile.X}"),
                        (Name: "night", Url: $"https://mt1.google.com/vt/lyrs=m&x={tile.X}&y={tile.Y}&z={tile.Z}&apistyle=s.t:1|p.v:on,s.t:2|p.v:off,s.t:3|p.v:on|p.c:#ff242f3e,s.t:4|p.v:on|p.c:#ff1f2835,s.t:5|p.v:on|p.c:#ff1f2835,s.t:6|p.v:on|p.c:#ff3d5afe,s.t:7|p.v:on|p.c:#ff3d5afe,s.t:8|p.v:on|p.c:#ff3d5afe,s.t:9|p.v:on|p.c:#ff3d5afe,s.t:10|p.v:on|p.c:#ff3d5afe")
                    };

                    foreach (var layer in layers)
                    {
                        // 🛡️ PROVIDER LIMITS: ArcGIS stops at Z19. Don't waste space/bandwidth on 404s.
                        if (layer.Name == "arcgis" && tile.Z > 19) continue;

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
                        _progressTracker[task.City] = (current, totalTiles, current * 0.01512, p.paused);
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
                using var pgConn = new NpgsqlConnection(pgConnString);
                await pgConn.ExecuteAsync("UPDATE downloads SET status = 'Error' WHERE city = @City", new { task.City });
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

        private int LonToTileX(double lon, int z)
        {
            return (int)(Math.Floor((lon + 180.0) / 360.0 * (1 << z)));
        }

        private int LatToTileY(double lat, int z)
        {
            double latRad = lat * Math.PI / 180.0;
            return (int)(Math.Floor((1.0 - Math.Asinh(Math.Tan(latRad)) / Math.PI) / 2.0 * (1 << z)));
        }
    }

    public class DownloadTask
    {
        public string City { get; set; } = string.Empty;
        public double[] Bbox { get; set; } = Array.Empty<double>();
        public string? BboxJson { get; set; }
    }
}
