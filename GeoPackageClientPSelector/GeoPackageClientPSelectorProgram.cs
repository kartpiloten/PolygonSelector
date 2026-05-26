using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

const string serverUrl = "http://localhost:5080/search/geopackage";

// During local development, the server has Dev:SkipAuth = true in appsettings.json,
// so any token value (or even an empty string) is accepted.
// When you have a real OAuth server, replace this with a valid Bearer token.
const string bearerToken = "dev-token";

// Example polygon — central Gotland area in WGS84 (EPSG:4326, lon/lat)
var polygonGeoJson = """
    { "type": "Polygon", "coordinates": [ [ [ 18.20, 57.46 ], 
    [ 18.42, 57.46 ], 
    [ 18.42, 57.30 ], 
    [ 18.20, 57.30 ], 
    [ 18.20, 57.46 ] ] ] } 
    """;

// InputSrid  — the SRID of the polygon coordinates above (4326 = WGS84)
// OutputSrid — the SRID for geometries stored in the returned GeoPackage
//              (3006 = SWEREF 99 TM, the Swedish national grid)
var requestBody = JsonSerializer.Serialize(new
{
    GeoJson    = polygonGeoJson,
    InputSrid  = 4326,
    OutputSrid = 3006
});

Console.WriteLine("=== GeoJSON sent to server ===");
Console.WriteLine(polygonGeoJson);
Console.WriteLine("==============================\n");
Console.WriteLine("Requesting GeoPackage (output SRID: 3006 / SWEREF 99 TM)...\n");

using var client = new HttpClient();
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

var request = new HttpRequestMessage(HttpMethod.Post, serverUrl)
{
    Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
};

using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

if (!response.IsSuccessStatusCode)
{
    Console.WriteLine($"Error: {response.StatusCode}");
    var error = await response.Content.ReadAsStringAsync();
    Console.WriteLine(error);
    return;
}

var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "search_results.gpkg");

await using var fileStream = File.Create(outputPath);
await using var responseStream = await response.Content.ReadAsStreamAsync();
await responseStream.CopyToAsync(fileStream);

Console.WriteLine($"GeoPackage saved to: {outputPath}");
Console.WriteLine("Open search_results.gpkg in QGIS or ArcGIS to inspect the results.");
