using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace PakistanMaps.Controllers
{
    [ApiController]
    [Route("tiles")]
    public class TilesController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _tilesRoot;

        public TilesController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
            _tilesRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "tiles");
            if (!Directory.Exists(_tilesRoot)) Directory.CreateDirectory(_tilesRoot);
        }

        [HttpGet("{city}/{z}/{x}/{y}.png")]
        public async Task<IActionResult> GetTile(string city, int z, int x, int y)
        {
            // 1. Try local file first
            string localPath = Path.Combine(_tilesRoot, city, z.ToString(), x.ToString(), $"{y}.png");
            if (System.IO.File.Exists(localPath))
            {
                return PhysicalFile(localPath, "image/png");
            }

            // 2. Fallback to Google/ArcGIS and AUTO-CACHE
            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                
                string url;
                if (city.Contains("satellite")) {
                    // 🛰️ GOOGLE HYBRID (Satellite + Labels) Z21
                    url = $"https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}";
                } else if (city.Contains("arcgis-dark") || city.Contains("night")) {
                    // 🌙 GOOGLE ULTIMATE NIGHT STYLE (Z21 Support)
                    url = $"https://mt1.google.com/vt/lyrs=m&x={x}&y={y}&z={z}&apistyle=s.t:1|p.v:on,s.t:2|p.v:off,s.t:3|p.v:on|p.c:#ff242f3e,s.t:4|p.v:on|p.c:#ff1f2835,s.t:5|p.v:on|p.c:#ff1f2835,s.t:6|p.v:on|p.c:#ff3d5afe,s.t:7|p.v:on|p.c:#ff3d5afe,s.t:8|p.v:on|p.c:#ff3d5afe,s.t:9|p.v:on|p.c:#ff3d5afe,s.t:10|p.v:on|p.c:#ff3d5afe";
                } else if (city.Contains("arcgis")) {
                    url = $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}";
                } else {
                    url = $"https://mt1.google.com/vt/lyrs=m&x={x}&y={y}&z={z}";
                }

                var data = await client.GetByteArrayAsync(url);

                // Save to disk for future offline use
                string dir = Path.GetDirectoryName(localPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                await System.IO.File.WriteAllBytesAsync(localPath, data);

                return base.File(data, "image/png");
            }
            catch
            {
                return NotFound();
            }
        }
    }
}
