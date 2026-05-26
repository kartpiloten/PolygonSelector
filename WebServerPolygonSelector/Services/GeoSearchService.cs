using System.Runtime.CompilerServices;
using System.Text.Json;
using MapPiloteGeopackageHelper;
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

    /// <summary>
    /// Searches each configured table and yields results as FeatureRecords
    /// with geometries transformed to <paramref name="outputSrid"/>.
    /// Intended for GeoPackage export.
    /// </summary>
    public async IAsyncEnumerable<(string LayerName, string GeometryType, List<FeatureRecord> Features)> SearchForGeoPackageAsync(
        Geometry polygon,
        int outputSrid,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var wktWriter = new WKTWriter { OutputOrdinates = Ordinates.XY };
        var wkt = wktWriter.Write(polygon);
        int inputSrid = polygon.SRID == 0 ? 4326 : polygon.SRID;
        var wkbReader = new WKBReader();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        foreach (var table in _tables)
        {
            ct.ThrowIfCancellationRequested();

            var attrCols = string.Join(", ", table.AttributeColumns.Select(c => $"\"{c}\""));
            var sql = $"""
                SELECT ST_AsEWKB(ST_Transform("{table.GeometryColumn}", {outputSrid})) AS geom_wkb,
                       ST_GeometryType("{table.GeometryColumn}") AS geom_type,
                       {attrCols}
                FROM {table.TableName}
                WHERE ST_Intersects(
                    "{table.GeometryColumn}",
                    ST_Transform(ST_GeomFromText('{wkt}', {inputSrid}),
                                 ST_SRID("{table.GeometryColumn}"))
                )
                """;

            var features = new List<FeatureRecord>();
            string geometryType = "GEOMETRY";
            try
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync(ct);

                while (await reader.ReadAsync(ct))
                {
                    if (reader.IsDBNull(0))
                        continue;

                    var wkbBytes = (byte[])reader.GetValue(0);
                    var geom = wkbReader.Read(wkbBytes);
                    geom.SRID = outputSrid;

                    // Capture geometry type from first row (e.g. "ST_Polygon" -> "POLYGON")
                    if (features.Count == 0 && !reader.IsDBNull(1))
                        geometryType = reader.GetString(1).Replace("ST_", "").ToUpperInvariant();

                    var attrs = new Dictionary<string, string>();
                    for (int i = 0; i < table.AttributeColumns.Count; i++)
                    {
                        attrs[table.AttributeColumns[i]] = reader.IsDBNull(i + 2)
                            ? string.Empty
                            : reader.GetValue(i + 2)?.ToString() ?? string.Empty;
                    }

                    features.Add(new FeatureRecord(geom, attrs));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error querying table {Table} for GeoPackage export", table.TableName);
            }

            yield return (table.Title, geometryType, features);
        }
    }
}
