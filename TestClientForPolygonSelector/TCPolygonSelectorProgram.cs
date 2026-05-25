using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

const string serverUrl = "http://localhost:5080/search";

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

var requestBody = JsonSerializer.Serialize(new { GeoJson = polygonGeoJson, Srid = 4326 });

Console.WriteLine("=== GeoJSON sent to server ===");
Console.WriteLine(polygonGeoJson);
Console.WriteLine("==============================\n");

using var client = new HttpClient();
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

var request = new HttpRequestMessage(HttpMethod.Post, serverUrl)
{
    Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
};

Console.WriteLine("Sending polygon search request...\n");

using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

if (!response.IsSuccessStatusCode)
{
    Console.WriteLine($"Error: {response.StatusCode}");
    return;
}

await using var stream = await response.Content.ReadAsStreamAsync();
using var reader = new StreamReader(stream);

while (!reader.EndOfStream)
{
    var line = await reader.ReadLineAsync();
    if (string.IsNullOrWhiteSpace(line)) continue;

    if (line.StartsWith("event: done")) break;

    if (line.StartsWith("data: "))
    {
        var json = line.Substring("data: ".Length);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var title = root.GetProperty("title").GetString();
            var features = root.GetProperty("features").GetProperty("features");
            var count = features.GetArrayLength();

            Console.WriteLine($"[{title}] — {count} feature(s) found");

            foreach (var feature in features.EnumerateArray())
            {
                var props = feature.GetProperty("properties");
                Console.WriteLine($"  {props}");
            }
        }
        catch
        {
            Console.WriteLine($"  [raw] {json}");
        }
    }
}

Console.WriteLine("\nSearch complete.");
