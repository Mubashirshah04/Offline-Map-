namespace PakistanMaps.Utils
{
    public static class TileCalculator
    {
        public static long GetTotalTilesForBBox(double[] bbox, int minZoom, int maxZoom)
        {
            if (bbox == null || bbox.Length < 4) return 0;
            long total = 0;
            
            for (int z = minZoom; z <= maxZoom; z++)
            {
                int minX = LonToTileX(bbox[0], z);
                int maxX = LonToTileX(bbox[2], z);
                
                int minY = LatToTileY(bbox[3], z); // Max Lat gives Min Y
                int maxY = LatToTileY(bbox[1], z); // Min Lat gives Max Y

                long width = (maxX - minX) + 1;
                long height = (maxY - minY) + 1;
                total += (width * height);
            }
            return total;
        }

        private static int LonToTileX(double lon, int z)
        {
            return (int)(Math.Floor((lon + 180.0) / 360.0 * (1 << z)));
        }

        private static int LatToTileY(double lat, int z)
        {
            double latRad = lat * Math.PI / 180.0;
            return (int)(Math.Floor((1.0 - Math.Asinh(Math.Tan(latRad)) / Math.PI) / 2.0 * (1 << z)));
        }
    }
}
