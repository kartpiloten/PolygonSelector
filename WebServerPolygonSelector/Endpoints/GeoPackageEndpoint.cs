using MapPiloteGeopackageHelper;
using Microsoft.Data.Sqlite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;
using WebServerPolygonSelector.Models;
using WebServerPolygonSelector.Services;

namespace WebServerPolygonSelector.Endpoints;

public static class GeoPackageEndpoint
{
    public static void MapGeoPackageEndpoint(this WebApplication app)
    {
        app.MapPost("/search/geopackage", async (
            HttpContext ctx,
            IConfiguration config,
            GeoPackageRequest request,
            GeoSearchService geoSearch,
            TokenIntrospectionService tokenService,
            CancellationToken ct) =>
        {
            // Authorization via OAuth Token Introspection
            // Set Dev:SkipAuth = true in appsettings.json to bypass this check during development
            var skipAuth = config.GetValue<bool>("Dev:SkipAuth");
            if (!skipAuth)
            {
                var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
                var token = authHeader?.StartsWith("Bearer ") == true
                    ? authHeader["Bearer ".Length..]
                    : null;

                if (token is null || !await tokenService.IsActiveAsync(token))
                {
                    ctx.Response.StatusCode = 401;
                    await ctx.Response.WriteAsync("Unauthorized", ct);
                    return;
                }
            }

            // Parse the input GeoJSON polygon.
            // Coordinates are interpreted using InputSrid specified in the request (defaults to 4326).
            Geometry polygon;
            try
            {
                var serializer = GeoJsonSerializer.Create();
                using var stringReader = new System.IO.StringReader(request.GeoJson);
                using var jsonReader = new JsonTextReader(stringReader);
                polygon = serializer.Deserialize<Geometry>(jsonReader)
                    ?? throw new Exception("Null geometry");
                polygon.SRID = request.InputSrid;
            }
            catch (Exception)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Invalid GeoJSON polygon.", ct);
                return;
            }

            // Build the GeoPackage in a temporary file.
            // All layer geometries are stored in OutputSrid (defaults to 4326).
            var tempFile = Path.Combine(Path.GetTempPath(), $"search_{Guid.NewGuid():N}.gpkg");
            try
            {
                await using (var gpkg = await GeoPackage.OpenAsync(tempFile, request.OutputSrid, ct))
                {
                    await foreach (var (layerName, geometryType, features) in
                        geoSearch.SearchForGeoPackageAsync(polygon, request.OutputSrid, ct))
                    {
                        if (features.Count == 0)
                            continue;

                        // GeoPackage layer names must be valid SQL identifiers.
                        // Convert the human-readable title to a safe name, e.g.
                        // "DeSO Areas 2025" → "DeSO_Areas_2025"
                        var safeLayerName = ToSafeIdentifier(layerName);

                        // Build attribute column schema from the first feature's keys.
                        // All values come through as strings from PostGIS, so TEXT is correct.
                        var attributeColumns = features[0].Attributes.Keys
                            .ToDictionary(k => k, _ => "TEXT");

                        var layer = await gpkg.EnsureLayerAsync(
                            safeLayerName,
                            attributeColumns: attributeColumns,
                            srid: request.OutputSrid,
                            geometryType: geometryType,
                            ct: ct);

                        await layer.BulkInsertAsync(
                            features,
                            new BulkInsertOptions(Srid: request.OutputSrid),
                            progress: null,
                            ct);
                    }
                }

                // The GeoPackage library uses SQLite connection pooling internally.
                // ClearAllPools() forces all pooled connections to close so the file
                // handle is fully released before we read or delete the temp file.
                SqliteConnection.ClearAllPools();

                // Stream the GeoPackage file back to the client.
                ctx.Response.ContentType = "application/geopackage+sqlite3";
                ctx.Response.Headers.ContentDisposition = "attachment; filename=\"search_results.gpkg\"";

                await using (var fs = File.OpenRead(tempFile))
                {
                    await fs.CopyToAsync(ctx.Response.Body, ct);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        });
    }

    /// <summary>
    /// Converts a human-readable title to a valid SQL identifier by replacing
    /// any character that is not an ASCII letter, digit, or underscore with an underscore,
    /// and prepending an underscore if the name starts with a digit.
    /// Example: "DeSO Areas 2025" → "DeSO_Areas_2025"
    /// Example: "Tätorter 2023"   → "T_torter_2023"
    /// </summary>
    private static string ToSafeIdentifier(string name)
    {
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                chars[i] = '_';
        }
        var result = new string(chars);
        return char.IsDigit(result[0]) ? "_" + result : result;
    }
}
