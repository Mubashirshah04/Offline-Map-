using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

using Npgsql;
using Dapper;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient(); // Required for Tile Proxy fetching

// Configure PostgreSQL connection
var connectionString = builder.Configuration.GetConnectionString("PostgreSQLConnection");
builder.Services.AddScoped<NpgsqlConnection>(_ => new NpgsqlConnection(connectionString));

// Add Downloader Service
builder.Services.AddSingleton<PakistanMaps.Services.TileDownloaderService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<PakistanMaps.Services.TileDownloaderService>());

// Enable CORS for frontend development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

var app = builder.Build();

// Initialize PostgreSQL Database schema
using (var scope = app.Services.CreateScope())
{
    try
    {
        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();
        conn.Execute(@"
            CREATE TABLE IF NOT EXISTS downloads (
                city VARCHAR(255) PRIMARY KEY,
                status VARCHAR(50),
                size_mb REAL,
                completed_tiles BIGINT,
                total_tiles BIGINT,
                total_mb REAL,
                bbox_json TEXT
            )");
        // Migration: Rename bbox to bbox_json if it exists
        try {
            conn.Execute("ALTER TABLE downloads RENAME COLUMN bbox TO bbox_json");
            Console.WriteLine("✅ Database Migrated: bbox -> bbox_json");
        } catch { /* Column might already be renamed */ }

        Console.WriteLine("✅ PostgreSQL Database Engine Online");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ PostgreSQL Init Error (Is it running?): {ex.Message}");
    }
}


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngular");

// Important: Serve tiles from wwwroot/tiles with correct MIME type
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".pmtiles"] = "application/octet-stream";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider,
    OnPrepareResponse = ctx =>
    {
        // Cache tiles for 7 days to reduce server load and increase speed
        if (ctx.Context.Request.Path.Value?.Contains("/tiles/") == true)
        {
            ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=604800");
        }
    }
});
// app.UseHttpsRedirection(); // 🛠️ DISABLED for Local Dev Stability
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok("Tile server is running."));
app.Urls.Add("http://localhost:5000");
app.Run();
