namespace WebServerPolygonSelector.Models;

public class GeoPackageRequest
{
    /// <summary>
    /// GeoJSON polygon geometry string, e.g. {"type":"Polygon","coordinates":[...]}
    /// </summary>
    public string GeoJson { get; set; } = string.Empty;

    /// <summary>
    /// SRID of the coordinates in GeoJson. Defaults to 4326 (WGS84).
    /// </summary>
    public int InputSrid { get; set; } = 4326;

    /// <summary>
    /// SRID for the geometries stored in the returned GeoPackage. Defaults to 4326 (WGS84).
    /// Use e.g. 3006 for SWEREF 99 TM.
    /// </summary>
    public int OutputSrid { get; set; } = 4326;
}
