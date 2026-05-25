using System.Runtime.CompilerServices;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Npgsql;
using WebServerPolygonSelector.Models;

namespace WebServerPolygonSelector.Services;

public class GeoSearchService(IConfiguration config, ILogger<GeoSearchService> logger)
{
    private readonly string _connectionString = config.GetConnectionString("PostGIS")
        ?? throw new InvalidOperationException("PostGIS connection string is missing.");

    private readonly List<TableConfig> _tables =
        config.GetSection("Tables").Get<List<TableConfig>>() ?? [];

    public async IAsyncEnumerable<(string Title, string GeoJsonFeatureCollection)> SearchAsync(
        Geometry polygon,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var wktWriter = new WKTWriter { OutputOrdinates = Ordinates.XY };
        var wkt = wktWriter.Write(polygon);
        int srid = polygon.SRID == 0 ? 4326 : polygon.SRID;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        foreach (var table in _tables)
        {
            ct.ThrowIfCancellationRequested();

            var attrCols = string.Join(", ", table.AttributeColumns.Select(c => $"\"{c}\""));
            var sql = $"""
                SELECT ST_AsGeoJSON(ST_Transform("{table.GeometryColumn}", 4326)) AS geom_json,
                       {attrCols}
                FROM {table.TableName}
                WHERE ST_Intersects(
                    "{table.GeometryColumn}",
                    ST_Transform(ST_GeomFromText('{wkt}', {srid}),
                                 ST_SRID("{table.GeometryColumn}"))
                )
                """;

            var features = new List<object>();
            try
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync(ct);

                while (await reader.ReadAsync(ct))
                {
                    var geomJson = reader.GetString(0);
                    var props = new Dictionary<string, object?>();
                    for (int i = 0; i < table.AttributeColumns.Count; i++)
                        props[table.AttributeColumns[i]] = reader.IsDBNull(i + 1) ? null : reader.GetValue(i + 1);

                    features.Add(new
                    {
                        type = "Feature",
                        geometry = JsonDocument.Parse(geomJson).RootElement,
                        properties = props
                    });
                }

                }
                catch (Exception ex)
            {
                logger.LogError(ex, "Error querying table {Table}", table.TableName);
            }

            var featureCollection = JsonSerializer.Serialize(new
            {
                type = "FeatureCollection",
                features
            });

            yield return (table.Title, featureCollection);
        }
    }
}
