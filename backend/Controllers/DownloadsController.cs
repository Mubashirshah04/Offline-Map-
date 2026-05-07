using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Dapper;
using PakistanMaps.Services;

namespace PakistanMaps.Controllers
{
    [ApiController]
    public class DownloadsController : ControllerBase
    {
        private readonly NpgsqlConnection _db;
        private readonly TileDownloaderService _downloader;

        public DownloadsController(NpgsqlConnection db, TileDownloaderService downloader)
        {
            _db = db;
            _downloader = downloader;
        }

        [HttpGet("/all-downloads")]
        public async Task<IActionResult> GetAllDownloads()
        {
            var downloads = await _db.QueryAsync("SELECT * FROM downloads");
            return Ok(downloads);
        }

        [HttpGet("/download-status")]
        public async Task<IActionResult> GetDownloadStatus()
        {
            var activeDownload = await _db.QueryFirstOrDefaultAsync("SELECT * FROM downloads WHERE status IN ('Queued', 'Harvesting Raster Tiles', 'Harvesting', 'Paused') LIMIT 1");
            if (activeDownload != null)
            {
                return Ok(new {
                    active = true,
                    city = activeDownload.city,
                    completed = activeDownload.completed_tiles,
                    total = activeDownload.total_tiles,
                    mb = activeDownload.size_mb,
                    totalMb = activeDownload.total_mb,
                    status = activeDownload.status,
                    paused = activeDownload.status == "Paused"
                });
            }
            return Ok(new { active = false });
        }

        [HttpPost("/start-download")]
        public async Task<IActionResult> StartDownload([FromBody] DownloadRequest req)
        {
            double[] bbox = req.Bbox ?? Array.Empty<double>();
            
            // Ultimate Quality Architecture: Z0-Z21 Globally
            long exactTotalTiles = PakistanMaps.Utils.TileCalculator.GetTotalTilesForBBox(bbox, 0, 21);
            double estimatedMb = (exactTotalTiles * 0.015); // Assume 15KB per raster tile avg

            var query = @"
                INSERT INTO downloads (city, status, size_mb, completed_tiles, total_tiles, total_mb, bbox_json) 
                VALUES (@City, 'Queued', 0, 0, @TotalTiles, @TotalMb, @Bbox)
                ON CONFLICT (city) DO UPDATE SET status = 'Queued', total_tiles = @TotalTiles, total_mb = @TotalMb, bbox_json = @Bbox;";;
                
            await _db.ExecuteAsync(query, new { City = req.City, TotalTiles = exactTotalTiles, TotalMb = estimatedMb, Bbox = System.Text.Json.JsonSerializer.Serialize(bbox) });
            
            _downloader.EnqueueDownload(req.City, bbox);
            
            return Ok(new { status = "started", message = "Raster harvesting initiated." });
        }

        [HttpPost("/stop-download")]
        public async Task<IActionResult> StopDownload()
        {
            await _db.ExecuteAsync("UPDATE downloads SET status='Stopped' WHERE status IN ('Queued', 'Harvesting Raster Tiles', 'Harvesting', 'Paused')");
            return Ok(new { success = true });
        }

        [HttpPost("/pause-download")]
        public async Task<IActionResult> PauseDownload()
        {
            await _db.ExecuteAsync("UPDATE downloads SET status='Paused' WHERE status IN ('Queued', 'Harvesting Raster Tiles', 'Harvesting')");
            return Ok(new { success = true });
        }

        [HttpPost("/resume-download")]
        public async Task<IActionResult> ResumeDownload()
        {
            await _db.ExecuteAsync("UPDATE downloads SET status='Harvesting Raster Tiles' WHERE status = 'Paused'");
            return Ok(new { success = true });
        }

        [HttpPost("/resume-specific")]
        public async Task<IActionResult> ResumeSpecific([FromBody] ResumeRequest req)
        {
            var task = await _db.QueryFirstOrDefaultAsync("SELECT * FROM downloads WHERE city = @City", new { City = req.City });
            if (task == null) return NotFound();

            await _db.ExecuteAsync("UPDATE downloads SET status='Queued' WHERE city = @City", new { City = req.City });
            
            // Re-enqueue in the background service
            double[] bbox = System.Text.Json.JsonSerializer.Deserialize<double[]>(task.bbox_json);
            _downloader.EnqueueDownload(req.City, bbox);

            return Ok(new { success = true });
        }
        
        [HttpPost("/delete-download")]
        public async Task<IActionResult> DeleteDownload([FromBody] DeleteRequest req)
        {
            // 🛑 SIGNAL STOP: First set status to 'Stopped' so the worker sees it and exits immediately
            await _db.ExecuteAsync("UPDATE downloads SET status = 'Stopped' WHERE city = @City", new { City = req.City });
            await Task.Delay(500); // Give worker threads a small window to see the 'Stopped' signal

            // 🗑️ PERMANENT DELETE: Now that workers are terminating, safely remove the record
            await _db.ExecuteAsync("DELETE FROM downloads WHERE city = @City", new { City = req.City });
            return Ok(new { success = true });
        }
        [HttpPost("/delete-all-data")]
        public async Task<IActionResult> DeleteAllData()
        {
            // 🛑 FORCE STOP: Update all to Stopped so worker stops accessing files
            await _db.ExecuteAsync("UPDATE downloads SET status = 'Stopped'");
            await Task.Delay(1000); // Wait for threads to notice

            await _db.ExecuteAsync("TRUNCATE TABLE downloads");
            
            string tilesPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "tiles");
            if (Directory.Exists(tilesPath))
            {
                // 🔄 RETRY LOOP: Files might be locked by background threads
                for (int i = 0; i < 3; i++) {
                    try {
                        Directory.Delete(tilesPath, true);
                        Directory.CreateDirectory(tilesPath);
                        break;
                    } catch {
                        await Task.Delay(1000); // Wait and retry
                    }
                }
            }
            return Ok(new { success = true });
        }
        [HttpGet("/api/search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrEmpty(q)) return Ok(new List<object>());
            try {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5); // ⏱️ TIMEOUT TO PREVENT HANGS
                client.DefaultRequestHeaders.Add("User-Agent", "OMEGA-GIS-Engine");
                var response = await client.GetAsync($"https://nominatim.openstreetmap.org/search?q={q}&format=json&limit=5");
                
                if (!response.IsSuccessStatusCode) return Ok(new List<object>()); 
                
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            } catch {
                return Ok(new List<object>());
            }
        }
    }

    public class DownloadRequest
    {
        public string City { get; set; } = string.Empty;
        public double[]? Bbox { get; set; }
    }
    
    public class DeleteRequest
    {
        public string City { get; set; } = string.Empty;
    }
    
    public class ResumeRequest
    {
        public string City { get; set; } = string.Empty;
    }
}
