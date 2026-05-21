using System;
using System.Linq;

namespace PakistanMaps.Utils
{
    public static class TileCalculator
    {
        // 🏙️ PAKISTAN CITIES — Z21 coverage (UNCHANGED) — PUBLIC for TileDownloaderService
        public static readonly (double minLat, double maxLat, double minLon, double maxLon)[] PakistanCities =
        {
            // 🏙️ MAJOR CITIES (Large radius for Z21 coverage)
            (24.7, 25.5, 66.8, 67.5),   // Karachi
            (31.3, 31.7, 74.1, 74.5),   // Lahore
            (33.5, 33.8, 72.9, 73.2),   // Islamabad / Rawalpindi
            (25.3, 25.6, 68.2, 68.6),   // Hyderabad
            (33.9, 34.1, 71.4, 71.7),   // Peshawar
            (30.1, 30.3, 66.9, 67.1),   // Quetta
            (30.0, 30.3, 71.4, 71.7),   // Multan
            
            // 🏘️ SECONDARY CITIES
            (31.3, 31.6, 74.5, 74.7),   // Gujranwala
            (32.4, 32.6, 74.4, 74.7),   // Sialkot
            (32.5, 32.7, 74.0, 74.2),   // Gujrat
            (31.2, 31.6, 72.9, 73.3),   // Faisalabad
            (31.1, 31.3, 72.3, 72.5),   // Jhang
            (31.7, 31.9, 73.9, 74.1),   // Sheikhupura
            (29.3, 29.5, 71.6, 71.8),   // Bahawalpur
            (29.9, 30.1, 70.9, 71.1),   // Dera Ghazi Khan
            (27.6, 27.8, 68.8, 69.0),   // Sukkur
            (27.7, 27.9, 68.8, 69.0),   // Rohri
            (27.5, 27.7, 68.2, 68.4),   // Larkana
            (25.7, 25.9, 68.8, 69.0),   // Nawabshah
            (27.0, 27.2, 68.3, 68.5),   // Khairpur
            (28.4, 28.6, 70.2, 70.4),   // Rahim Yar Khan
            (34.7, 34.9, 72.3, 72.5),   // Mingora (Swat)
            (34.1, 34.3, 71.8, 72.1),   // Mardan
            (34.1, 34.3, 73.0, 73.2),   // Abbottabad
            (33.1, 33.3, 73.7, 73.9),   // Mirpur (AJK)
            (32.9, 33.1, 73.7, 73.9),   // Jhelum
            (33.1, 33.3, 71.9, 72.1),   // Kohat
            (32.5, 32.7, 71.5, 71.7),   // Mianwali
            (30.9, 31.1, 73.0, 73.2),   // Sahiwal
            (30.9, 31.1, 70.9, 71.1),   // Layyah
            (33.1, 33.3, 72.4, 72.6),   // Attock
            (32.5, 32.7, 71.5, 71.7),   // Bhakkar
            (30.1, 30.3, 67.9, 68.1),   // Sibi
            (29.4, 29.6, 66.4, 66.6),   // Turbat
            (25.1, 25.3, 62.2, 62.5),   // Gwadar
            (25.2, 25.4, 64.6, 64.8),   // Chaman
            (24.7, 24.9, 66.9, 67.1),   // Thatta
            (27.3, 27.5, 68.8, 69.0),   // Shikarpur
            (28.0, 28.2, 69.3, 69.5),   // Jacobabad
            (31.8, 32.0, 70.9, 71.1),   // DI Khan
            (32.0, 32.2, 74.9, 75.1),   // Wazirabad
            (31.5, 31.7, 74.4, 74.6),   // Kasur
            (31.1, 31.3, 73.7, 73.9),   // Okara
            (30.6, 30.8, 73.4, 73.6),   // Pakpattan
            (29.6, 29.8, 72.3, 72.5),   // Vehari
            (30.9, 31.1, 72.6, 72.8),   // Chichawatni
            (33.0, 33.2, 73.6, 73.8),   // Kotli (AJK)
            (34.5, 34.7, 73.8, 74.0),   // Muzaffarabad (AJK)
            (32.8, 33.0, 75.6, 75.8),   // Kotli Sattian
        };

        // 🌍 GENERIC URBAN ZONES for other countries (Z18-Z21 coverage)
        // Covers ~50 major world cities for non-Pakistan countries — PUBLIC for TileDownloaderService
        public static readonly (double minLat, double maxLat, double minLon, double maxLon)[] WorldCities =
        {
            // 🇮🇳 INDIA - Major cities
            (19.0, 19.3, 72.8, 73.0),   // Mumbai
            (28.5, 28.8, 77.0, 77.3),   // Delhi
            (12.9, 13.1, 77.5, 77.7),   // Bangalore
            (22.5, 22.7, 88.3, 88.5),   // Kolkata
            (17.3, 17.5, 78.4, 78.6),   // Hyderabad
            (13.0, 13.1, 80.2, 80.3),   // Chennai
            (23.0, 23.1, 72.5, 72.7),   // Ahmedabad
            (18.5, 18.7, 73.8, 74.0),   // Pune
            (26.8, 27.0, 75.7, 75.9),   // Jaipur
            (25.4, 25.5, 78.5, 78.7),   // Gwalior
            (27.1, 27.2, 77.9, 78.1),   // Agra
            (25.3, 25.5, 82.9, 83.1),   // Varanasi
            (30.8, 31.0, 75.8, 76.0),   // Ludhiana
            (34.1, 34.2, 74.8, 75.0),   // Srinagar
            (21.1, 21.3, 79.0, 79.2),   // Nagpur
            
            // 🇮🇷 IRAN - Major cities
            (35.6, 35.8, 51.2, 51.5),   // Tehran
            (36.3, 36.4, 59.5, 59.7),   // Mashhad
            (38.0, 38.2, 46.3, 46.5),   // Tabriz
            (32.6, 32.8, 51.6, 51.8),   // Isfahan
            (29.5, 29.7, 52.4, 52.7),   // Shiraz
            (31.8, 32.0, 54.3, 54.5),   // Yazd
            (36.8, 37.0, 54.4, 54.6),   // Gorgan
            (37.2, 37.4, 49.5, 49.8),   // Rasht
            (30.2, 30.4, 48.2, 48.5),   // Ahvaz
            (34.3, 34.5, 47.0, 47.2),   // Kermanshah
            
            // 🇦🇫 AFGHANISTAN - Major cities
            (34.4, 34.6, 69.1, 69.3),   // Kabul
            (31.5, 31.7, 65.6, 65.9),   // Kandahar
            (36.6, 36.8, 67.0, 67.2),   // Mazar-i-Sharif
            (34.2, 34.4, 62.1, 62.3),   // Herat
            (34.4, 34.6, 70.4, 70.6),   // Jalalabad
            
            // 🇸🇦 SAUDI ARABIA
            (24.6, 24.8, 46.6, 46.8),   // Riyadh
            (21.4, 21.6, 39.1, 39.3),   // Jeddah
            (26.3, 26.5, 50.1, 50.3),   // Dammam
            
            // 🇹🇷 TURKEY
            (41.0, 41.1, 28.8, 29.0),   // Istanbul
            (39.8, 40.0, 32.7, 32.9),   // Ankara
            (38.3, 38.5, 27.1, 27.3),   // Izmir
            (36.8, 37.0, 34.9, 35.1),   // Adana
            
            // 🇦🇪 UAE
            (25.0, 25.3, 55.0, 55.4),   // Dubai
            (24.3, 24.5, 54.3, 54.5),   // Abu Dhabi
            
            // 🇨🇳 CHINA - Major cities
            (39.8, 40.1, 116.2, 116.6), // Beijing
            (31.1, 31.3, 121.3, 121.6), // Shanghai
            (39.0, 39.2, 117.6, 117.9), // Tianjin
            (30.5, 30.7, 114.3, 114.5), // Wuhan
            (23.1, 23.3, 113.2, 113.4), // Guangzhou
            (22.5, 22.7, 113.9, 114.1), // Shenzhen
            (34.3, 34.5, 108.9, 109.1), // Xi'an
            
            // 🇷🇺 RUSSIA - European part
            (55.7, 56.0, 37.4, 37.8),   // Moscow
            (59.8, 60.0, 30.2, 30.5),   // St. Petersburg
            (56.8, 57.0, 60.5, 60.7),   // Yekaterinburg
            (55.0, 55.2, 82.8, 83.0),   // Novosibirsk
            
            // 🇺🇸 USA - Major cities
            (40.6, 40.8, -74.0, -73.9), // New York
            (34.0, 34.1, -118.3, -118.1), // Los Angeles
            (41.8, 42.0, -87.7, -87.5), // Chicago
            (29.7, 29.9, -95.5, -95.2), // Houston
            (33.4, 33.6, -112.1, -111.9), // Phoenix
            (39.9, 40.0, -75.2, -75.0), // Philadelphia
            (29.9, 30.1, -98.6, -98.4), // San Antonio
            (32.7, 32.8, -117.2, -117.0), // San Diego
            (37.7, 37.8, -122.5, -122.3), // San Francisco
        };

        // 🚀 3-LAYER CALC: Optimized for 8TB total storage - ALL COUNTRIES
        // 🌾 Rural: Z0-Z17 (full country) = 3 layers
        // 🏘️ Towns: Z18 (cities only) = 3 layers  
        // 🏙️ Cities Z19-Z21 (cities only) = 2 layers (no ArcGIS at Z20-Z21)
        // 8TB max limit for all countries
        public static long GetTotalTilesForBBox(double[] bbox, int minZoom, int maxZoom, string country = "")
        {
            if (bbox == null || bbox.Length < 4) return 0;
            long total = 0;
            
            // Select appropriate city zones based on country
            var cityZones = IsPakistan(country) ? PakistanCities : WorldCities;

            for (int z = minZoom; z <= maxZoom; z++)
            {
                if (z <= 17)
                {
                    // 🌾 Z0-Z17: Full country (3 layers: Google St, Google Sat, ArcGIS St)
                    int minX = LonToTileX(bbox[0], z);
                    int maxX = LonToTileX(bbox[2], z);
                    int minY = LatToTileY(bbox[3], z);
                    int maxY = LatToTileY(bbox[1], z);
                    total += (long)(maxX - minX + 1) * (maxY - minY + 1) * 3; // 3 layers
                }
                else if (z == 18)
                {
                    // 🏘️ Z18: Cities/towns only (3 layers) - NOT full country
                    long cityTiles = 0;
                    foreach (var zone in cityZones)
                    {
                        double iMinLon = Math.Max(bbox[0], zone.minLon);
                        double iMinLat = Math.Max(bbox[1], zone.minLat);
                        double iMaxLon = Math.Min(bbox[2], zone.maxLon);
                        double iMaxLat = Math.Min(bbox[3], zone.maxLat);

                        if (iMinLon >= iMaxLon || iMinLat >= iMaxLat) continue;

                        int minX = LonToTileX(iMinLon, z);
                        int maxX = LonToTileX(iMaxLon, z);
                        int minY = LatToTileY(iMaxLat, z);
                        int maxY = LatToTileY(iMinLat, z);
                        cityTiles += (long)(maxX - minX + 1) * (maxY - minY + 1);
                    }
                    total += cityTiles * 3; // 3 layers at Z18 (cities only)
                }
                else if (z == 19)
                {
                    // 🏙️ Z19: Cities only (3 layers: ArcGIS goes to Z19)
                    long cityTiles = 0;
                    foreach (var zone in cityZones)
                    {
                        double iMinLon = Math.Max(bbox[0], zone.minLon);
                        double iMinLat = Math.Max(bbox[1], zone.minLat);
                        double iMaxLon = Math.Min(bbox[2], zone.maxLon);
                        double iMaxLat = Math.Min(bbox[3], zone.maxLat);

                        if (iMinLon >= iMaxLon || iMinLat >= iMaxLat) continue;

                        int minX = LonToTileX(iMinLon, z);
                        int maxX = LonToTileX(iMaxLon, z);
                        int minY = LatToTileY(iMaxLat, z);
                        int maxY = LatToTileY(iMinLat, z);
                        cityTiles += (long)(maxX - minX + 1) * (maxY - minY + 1);
                    }
                    total += cityTiles * 3; // 3 layers at Z19
                }
                else
                {
                    // 🏙️ Z20-Z21: Cities only (2 layers: NO ArcGIS)
                    long cityTiles = 0;
                    foreach (var zone in cityZones)
                    {
                        double iMinLon = Math.Max(bbox[0], zone.minLon);
                        double iMinLat = Math.Max(bbox[1], zone.minLat);
                        double iMaxLon = Math.Min(bbox[2], zone.maxLon);
                        double iMaxLat = Math.Min(bbox[3], zone.maxLat);

                        if (iMinLon >= iMaxLon || iMinLat >= iMaxLat) continue;

                        int minX = LonToTileX(iMinLon, z);
                        int maxX = LonToTileX(iMaxLon, z);
                        int minY = LatToTileY(iMaxLat, z);
                        int maxY = LatToTileY(iMinLat, z);
                        cityTiles += (long)(maxX - minX + 1) * (maxY - minY + 1);
                    }
                    total += cityTiles * 2; // 2 layers at Z20-Z21 (NO ArcGIS)
                }
            }
            
            // Cap at 8TB worth of tiles (~1.3B tiles at 6KB each)
            long maxTiles = (long)(8000000 / 0.006); // 8TB in MB / 6KB per tile
            return Math.Min(total, maxTiles);
        }
        
        private static bool IsPakistan(string country)
        {
            if (string.IsNullOrEmpty(country)) return false;
            var pakNames = new[] { "pakistan", "all pakistan", "punjab", "sindh", "kpk", "balochistan", "gilgit", "ajk", "kashmir" };
            return pakNames.Any(p => country.ToLower().Contains(p));
        }

        private static int LonToTileX(double lon, int z) =>
            (int)Math.Floor((lon + 180.0) / 360.0 * (1 << z));

        private static int LatToTileY(double lat, int z)
        {
            double latRad = lat * Math.PI / 180.0;
            return (int)Math.Floor((1.0 - Math.Asinh(Math.Tan(latRad)) / Math.PI) / 2.0 * (1 << z));
        }
    }
}
