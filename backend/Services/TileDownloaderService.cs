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
        private readonly ConcurrentQueue<DownloadTask> _queue = new();
        private readonly IHttpClientFactory _httpClientFactory;

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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 🔄 STARTUP RECOVERY: Re-enqueue unfinished tasks from DB
            string pgConnString = _config.GetConnectionString("PostgreSQLConnection")!;
            try {
                using var conn = new NpgsqlConnection(pgConnString);
                var unfinished = await conn.QueryAsync<DownloadTask>("SELECT city, bbox_json as BboxJson FROM downloads WHERE status IN ('Queued', 'Harvesting Raster Tiles', 'Harvesting', 'Paused')");
                foreach (var task in unfinished) {
                    if (!string.IsNullOrEmpty(task.BboxJson)) {
                        task.Bbox = JsonSerializer.Deserialize<double[]>(task.BboxJson) ?? Array.Empty<double>();
                        EnqueueDownload(task.City, task.Bbox);
                    }
                }
            } catch (Exception ex) { _logger.LogError($"Recovery Error: {ex.Message}"); }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_queue.TryDequeue(out var task))
                {
                    await ProcessDownloadAsync(task, stoppingToken);
                }
                else
                {
                    await Task.Delay(1000, stoppingToken);
                }
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
                var tiles = GetTilesForBBox(task.Bbox, 0, 21);
                long singleLayerTiles = PakistanMaps.Utils.TileCalculator.GetTotalTilesForBBox(task.Bbox, 0, 21);
                long totalTiles = singleLayerTiles * 4; // 4 layers per tile location
                double estimatedMb = (totalTiles * 0.015);

                // 🚀 ATOMIC RECOVERY: Pre-scan folder to find already downloaded tiles
                _logger.LogInformation($"[OMEGA] Scanning existing tiles for {task.City}...");
                long completed = 0;
                if (Directory.Exists(cityPath)) {
                    // Use EnumerateFiles to avoid hanging on millions of files
                    completed = Directory.EnumerateFiles(cityPath, "*.png", SearchOption.AllDirectories).Count();
                }
                
                // 🚀 FORCE INITIAL UI SYNC: Let the UI know we are starting
                using (var pgConn = new NpgsqlConnection(pgConnString)) {
                    await pgConn.ExecuteAsync("UPDATE downloads SET completed_tiles = @Completed WHERE city = @City", new { Completed = completed, task.City });
                }

                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                // 🚀 TURBO-CHARGED ENGINE (32 Parallel Threads for Extreme Speed)
                // Using SemaphoreSlim to prevent DB Connection Exhaustion under heavy load
                var dbSemaphore = new SemaphoreSlim(5); 

                // 🚀 INDIVIDUAL KILL-SWITCH: Per-task cancellation
                using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                await Parallel.ForEachAsync(tiles, new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = taskCts.Token }, async (tile, ct) =>
                {
                    // ⏸️ FAST STATUS CHECK: Kill threads if task is gone from DB
                    if (completed % 5 == 0) 
                    {
                        await dbSemaphore.WaitAsync(ct);
                        try {
                            using var checkConn = new NpgsqlConnection(pgConnString);
                            var currentTask = await checkConn.QueryFirstOrDefaultAsync<dynamic>("SELECT status FROM downloads WHERE city = @City", new { task.City });
                            
                            if (currentTask == null || currentTask.status == "Stopped" || currentTask.status == "Error") {
                                _logger.LogInformation($"[OMEGA] KILL SIGNAL RECEIVED for {task.City}. Aborting all threads.");
                                taskCts.Cancel(); // 🛑 This kills ALL 32 threads for this task instantly
                                return;
                            }

                            string currentStatus = currentTask.status;
                            while (currentStatus == "Paused")
                            {
                                await Task.Delay(2000, ct); 
                                currentTask = await checkConn.QueryFirstOrDefaultAsync<dynamic>("SELECT status FROM downloads WHERE city = @City", new { task.City });
                                if (currentTask == null || currentTask.status == "Stopped") { taskCts.Cancel(); return; }
                                currentStatus = currentTask.status;
                            }
                        } finally { dbSemaphore.Release(); }
                    }

                    // 🛰️ MULTI-LAYER HARVESTING (Street, Satellite, ArcGIS, Night)
                    var layers = new[] {
                        (Name: "street", Url: $"https://mt1.google.com/vt/lyrs=m&x={tile.X}&y={tile.Y}&z={tile.Z}"),
                        (Name: "satellite", Url: $"https://mt1.google.com/vt/lyrs=y&x={tile.X}&y={tile.Y}&z={tile.Z}"),
                        (Name: "arcgis", Url: $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{tile.Z}/{tile.Y}/{tile.X}"),
                        (Name: "night", Url: $"https://mt1.google.com/vt/lyrs=m&x={tile.X}&y={tile.Y}&z={tile.Z}&apistyle=s.t:1|p.v:on,s.t:2|p.v:off,s.t:3|p.v:on|p.c:#ff242f3e,s.t:4|p.v:on|p.c:#ff1f2835,s.t:5|p.v:on|p.c:#ff1f2835,s.t:6|p.v:on|p.c:#ff3d5afe,s.t:7|p.v:on|p.c:#ff3d5afe,s.t:8|p.v:on|p.c:#ff3d5afe,s.t:9|p.v:on|p.c:#ff3d5afe,s.t:10|p.v:on|p.c:#ff3d5afe")
                    };

                    foreach (var layer in layers)
                    {
                        string layerPath = Path.Combine(cityPath, layer.Name, tile.Z.ToString(), tile.X.ToString());
                        if (!Directory.Exists(layerPath)) Directory.CreateDirectory(layerPath);
                        string filePath = Path.Combine(layerPath, $"{tile.Y}.png");

                        if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
                        {
                            bool success = false;
                            int retryCount = 0;
                            while (!success && retryCount < 3)
                            {
                                try {
                                    var data = await client.GetByteArrayAsync(layer.Url, ct);
                                    string tmpPath = filePath + ".tmp";
                                    await File.WriteAllBytesAsync(tmpPath, data, ct);
                                    File.Move(tmpPath, filePath, true);
                                    success = true;
                                } catch { retryCount++; await Task.Delay(500 * retryCount, ct); }
                            }
                        }
                    }

                    // 🛰️ COMPLETED COUNT: Count each of the 4 layers downloaded
                    Interlocked.Add(ref completed, 4);
                    
                    // 🚀 INSTANT SYNC: Update DB every 4 tiles (1 full set) so UI moves IMMEDIATELY
                    if (completed % 4 == 0)
                    {
                        long reportCompleted = Math.Min(completed, totalTiles);
                        using var pgConn = new NpgsqlConnection(pgConnString);
                        await pgConn.ExecuteAsync("UPDATE downloads SET completed_tiles = @Completed, size_mb = @SizeMb WHERE city = @City", 
                            new { Completed = reportCompleted, SizeMb = (reportCompleted * 0.015), task.City });
                    }
                });

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

        private IEnumerable<(int Z, int X, int Y)> GetTilesForBBox(double[] bbox, int minZoom, int maxZoom)
        {
            if (bbox == null || bbox.Length < 4) yield break;

            // 🚀 SMART STREAMING: For huge areas, we process zoom by zoom to save RAM
            // We still prioritize tiles closer to the center for immediate HD usability.
            double centerLon = (bbox[0] + bbox[2]) / 2.0;
            double centerLat = (bbox[1] + bbox[3]) / 2.0;

            // We iterate through zooms
            for (int z = maxZoom; z >= minZoom; z--)
            {
                int minX = LonToTileX(bbox[0], z);
                int maxX = LonToTileX(bbox[2], z);
                int minY = LatToTileY(bbox[3], z); 
                int maxY = LatToTileY(bbox[1], z);

                int midX = LonToTileX(centerLon, z);
                int midY = LatToTileY(centerLat, z);

                // Create a small list for just THIS zoom to sort it by distance
                // This prevents OutOfMemory because we only store one zoom's tiles at a time
                var zoomTiles = new List<(int X, int Y, double Dist)>();
                
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        double dist = Math.Sqrt(Math.Pow(x - midX, 2) + Math.Pow(y - midY, 2));
                        zoomTiles.Add((x, y, dist));
                        
                        // 🛡️ MEMORY SAFETY VALVE: If a single zoom is TOO big (e.g. Z21 for Pakistan), 
                        // we stream it immediately without sorting to prevent crash.
                        if (zoomTiles.Count > 1000000) 
                        {
                             foreach(var t in zoomTiles) yield return (z, t.X, t.Y);
                             zoomTiles.Clear();
                        }
                    }
                }

                // Sort and yield tiles for this zoom
                foreach (var t in zoomTiles.OrderBy(t => t.Dist))
                {
                    yield return (z, t.X, t.Y);
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
