namespace WebServerPolygonSelector.Models;

public class PolygonRequest
{
    /// <summary>
    /// GeoJSON polygon geometry string, e.g. {"type":"Polygon","coordinates":[...]}
    /// </summary>
    public string GeoJson { get; set; } = string.Empty;

    /// <summary>
    /// SRID of the coordinates in GeoJson. Defaults to 4326 (WGS84).
    /// Use 3006 for SWEREF 99 TM, etc.
    /// </summary>
    public int Srid { get; set; } = 4326;
}
