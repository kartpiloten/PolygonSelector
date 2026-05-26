using System.Text;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;
using WebServerPolygonSelector.Models;
using WebServerPolygonSelector.Services;
using STJ = System.Text.Json;

namespace WebServerPolygonSelector.Endpoints;

public static class SearchEndpoint
{
    public static void MapSearchEndpoint(this WebApplication app)
    {
        app.MapPost("/search", async (
            HttpContext ctx,
            IConfiguration config,
            PolygonRequest request,
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

            // Parse the input GeoJSON polygon
            // Coordinates are interpreted using the SRID specified in the request (defaults to 4326)
            Geometry polygon;
            try
            {
                var serializer = GeoJsonSerializer.Create();
                using var stringReader = new System.IO.StringReader(request.GeoJson);
                using var jsonReader = new JsonTextReader(stringReader);
                polygon = serializer.Deserialize<Geometry>(jsonReader)
                    ?? throw new Exception("Null geometry");
                polygon.SRID = request.Srid;
            }
            catch (Exception)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Invalid GeoJSON polygon.", ct);
                return;
            }

            // Response is streamed as Server-Sent Events (SSE).
            // All returned GeoJSON geometries are in SRID 4326 (WGS 84), as required by RFC 7946.
            // Set SSE response headers
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            await foreach (var (title, featureCollection) in geoSearch.SearchAsync(polygon, ct))
            {
                var payload = STJ.JsonSerializer.Serialize(new
                {
                    title,
                    features = STJ.JsonDocument.Parse(featureCollection).RootElement
                });

                await ctx.Response.WriteAsync($"data: {payload}\n\n", Encoding.UTF8, ct);
                await ctx.Response.Body.FlushAsync(ct);
            }

            // Signal end of stream
            await ctx.Response.WriteAsync("event: done\ndata: {}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        });
    }
}
