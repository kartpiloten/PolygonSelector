# How to Use the PolygonSelector API

## Overview

The PolygonSelector server exposes two HTTP POST endpoints that perform a **spatial intersection search**: you send a polygon geometry and the server queries 
all configured PostGIS tables, returning every feature whose geometry intersects that polygon.

Note that the sent polygon format is flexible and can use any SRID (this breaks the strict GeoJSON specification), 
but the returned geometries are always in WGS 84 (SRID 4326) for maximum compatibility with GeoJSON consumers.

| Endpoint | Response format | Use case |
|---|---|---|
| `POST /search` | Server-Sent Events (SSE) stream of GeoJSON FeatureCollections | Live/streaming map display |
| `POST /search/geopackage` | Binary `.gpkg` file download | Offline GIS analysis in QGIS, ArcGIS, etc. |

---

## Authentication

Both endpoints are protected by **OAuth 2.0 Token Introspection** (RFC 7662). You must obtain a Bearer access token from your OAuth server and include it 
in every request.

```
Authorization: Bearer <your-access-token>
```

The server validates the token by calling the configured introspection endpoint with its own client credentials. 
If the token is missing or inactive the server returns `401 Unauthorized`.

> **Development shortcut**: Set `"Dev": { "SkipAuth": true }` in `appsettings.json` to bypass authentication during local development.

---

## Request Body

Both endpoints accept a JSON body. The fields differ slightly between the two endpoints.

### `/search`

```json
{
  "geoJson": "{\"type\":\"Polygon\",\"coordinates\":[[[17.9,59.2],[18.2,59.2],[18.2,59.4],[17.9,59.4],[17.9,59.2]]]}",
  "srid": 4326
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `geoJson` | string | — | GeoJSON geometry string for the search polygon |
| `srid` | int | `4326` | SRID of the coordinates in `geoJson` (e.g. `3006` for SWEREF 99 TM) |

### `/search/geopackage`

```json
{
  "geoJson": "{\"type\":\"Polygon\",\"coordinates\":[[[17.9,59.2],[18.2,59.2],[18.2,59.4],[17.9,59.4],[17.9,59.2]]]}",
  "inputSrid": 4326,
  "outputSrid": 3006
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `geoJson` | string | — | GeoJSON geometry string for the search polygon |
| `inputSrid` | int | `4326` | SRID of the coordinates in `geoJson` |
| `outputSrid` | int | `4326` | SRID for the geometries stored in the returned GeoPackage |

---

## How the `/search` Endpoint Works

1. The server parses the GeoJSON polygon and assigns the given SRID.
2. For each configured table it runs a PostGIS query:
   - `ST_Intersects` filters rows whose geometry overlaps the search polygon.
   - `ST_Transform(..., 4326)` reprojects the result geometry to WGS 84, as required by the GeoJSON specification (RFC 7946).
3. Each table result is serialised as a GeoJSON `FeatureCollection` and pushed as an SSE `data:` event.
4. When all tables are processed the server sends a final `event: done` event.

### SSE Response format

```
data: {"title":"DeSO Areas 2025","features":{"type":"FeatureCollection","features":[...]}}

data: {"title":"Tätorter 2023","features":{"type":"FeatureCollection","features":[...]}}

event: done
data: {}
```

> **Note**: All returned GeoJSON geometries are always in SRID 4326 (WGS 84), regardless of the SRID used for the input polygon.

---

## How the `/search/geopackage` Endpoint Works

The flow is the same as `/search` but instead of streaming GeoJSON the server:

1. Queries PostGIS and reprojects result geometries to `outputSrid` using `ST_AsEWKB(ST_Transform(..., outputSrid))`.
2. Builds a GeoPackage (`.gpkg`) file in a temporary location, creating one layer per configured table.
3. Streams the completed file back as `application/geopackage+sqlite3` with the filename `search_results.gpkg`.

---

## Code Examples

### C#

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// ── /search (SSE) ──────────────────────────────────────────────────────────

var client = new HttpClient();
client.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", "<your-access-token>");

var polygon = new
{
    geoJson = """{"type":"Polygon","coordinates":[[[17.9,59.2],[18.2,59.2],[18.2,59.4],[17.9,59.4],[17.9,59.2]]]}""",
    srid = 4326
};

var request = new HttpRequestMessage(HttpMethod.Post, "https://your-server/search")
{
    Content = new StringContent(
        JsonSerializer.Serialize(polygon),
        Encoding.UTF8,
        "application/json")
};

using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
response.EnsureSuccessStatusCode();

await using var stream = await response.Content.ReadAsStreamAsync();
using var reader = new StreamReader(stream);

while (!reader.EndOfStream)
{
    var line = await reader.ReadLineAsync();
    if (line is null) continue;

    if (line.StartsWith("event: done")) break;

    if (line.StartsWith("data: "))
    {
        var json = line["data: ".Length..];
        using var doc = JsonDocument.Parse(json);
        var title = doc.RootElement.GetProperty("title").GetString();
        Console.WriteLine($"Layer: {title}");
    }
}

// ── /search/geopackage ─────────────────────────────────────────────────────

var gpkgRequest = new
{
    geoJson = """{"type":"Polygon","coordinates":[[[17.9,59.2],[18.2,59.2],[18.2,59.4],[17.9,59.4],[17.9,59.2]]]}""",
    inputSrid = 4326,
    outputSrid = 3006   // SWEREF 99 TM
};

var gpkgResponse = await client.PostAsJsonAsync(
    "https://your-server/search/geopackage", gpkgRequest);
gpkgResponse.EnsureSuccessStatusCode();

var bytes = await gpkgResponse.Content.ReadAsByteArrayAsync();
await File.WriteAllBytesAsync("search_results.gpkg", bytes);
Console.WriteLine("GeoPackage saved.");
```

---

### Python

```python
import requests
import json

BASE_URL = "https://your-server"
TOKEN    = "<your-access-token>"

headers = {
    "Authorization": f"Bearer {TOKEN}",
    "Content-Type": "application/json",
}

polygon_geojson = json.dumps({
    "type": "Polygon",
    "coordinates": [[[17.9, 59.2], [18.2, 59.2], [18.2, 59.4], [17.9, 59.4], [17.9, 59.2]]]
})

# ── /search (SSE) ──────────────────────────────────────────────────────────

payload = {"geoJson": polygon_geojson, "srid": 4326}

with requests.post(
    f"{BASE_URL}/search",
    headers=headers,
    json=payload,
    stream=True,
    timeout=60,
) as resp:
    resp.raise_for_status()
    for raw_line in resp.iter_lines(decode_unicode=True):
        if not raw_line:
            continue
        if raw_line == "event: done":
            break
        if raw_line.startswith("data: "):
            data = json.loads(raw_line[len("data: "):])
            print(f"Layer: {data['title']}, features: {len(data['features']['features'])}")

# ── /search/geopackage ─────────────────────────────────────────────────────

gpkg_payload = {
    "geoJson": polygon_geojson,
    "inputSrid": 4326,
    "outputSrid": 3006,   # SWEREF 99 TM
}

resp = requests.post(
    f"{BASE_URL}/search/geopackage",
    headers=headers,
    json=gpkg_payload,
    timeout=60,
)
resp.raise_for_status()

with open("search_results.gpkg", "wb") as f:
    f.write(resp.content)

print("GeoPackage saved.")
```

---

### JavaScript

```javascript
const BASE_URL = "https://your-server";
const TOKEN    = "<your-access-token>";

const polygonGeoJson = JSON.stringify({
  type: "Polygon",
  coordinates: [[[17.9, 59.2], [18.2, 59.2], [18.2, 59.4], [17.9, 59.4], [17.9, 59.2]]]
});

// ── /search (SSE) ──────────────────────────────────────────────────────────

const searchResponse = await fetch(`${BASE_URL}/search`, {
  method: "POST",
  headers: {
    "Authorization": `Bearer ${TOKEN}`,
    "Content-Type": "application/json",
  },
  body: JSON.stringify({ geoJson: polygonGeoJson, srid: 4326 }),
});

if (!searchResponse.ok) throw new Error(`HTTP ${searchResponse.status}`);

const reader = searchResponse.body.getReader();
const decoder = new TextDecoder();
let buffer = "";

while (true) {
  const { value, done } = await reader.read();
  if (done) break;

  buffer += decoder.decode(value, { stream: true });
  const lines = buffer.split("\n");
  buffer = lines.pop(); // keep incomplete last line

  for (const line of lines) {
    if (line.startsWith("event: done")) { reader.cancel(); break; }
    if (line.startsWith("data: ")) {
      const data = JSON.parse(line.slice("data: ".length));
      console.log(`Layer: ${data.title}`, data.features);
    }
  }
}

// ── /search/geopackage ─────────────────────────────────────────────────────

const gpkgResponse = await fetch(`${BASE_URL}/search/geopackage`, {
  method: "POST",
  headers: {
    "Authorization": `Bearer ${TOKEN}`,
    "Content-Type": "application/json",
  },
  body: JSON.stringify({ geoJson: polygonGeoJson, inputSrid: 4326, outputSrid: 3006 }),
});

if (!gpkgResponse.ok) throw new Error(`HTTP ${gpkgResponse.status}`);

const blob = await gpkgResponse.blob();
const url  = URL.createObjectURL(blob);
const a    = document.createElement("a");
a.href     = url;
a.download = "search_results.gpkg";
a.click();
URL.revokeObjectURL(url);
```

---

### TypeScript

```typescript
const BASE_URL = "https://your-server";
const TOKEN    = "<your-access-token>";

interface SearchLayer {
  title: string;
  features: GeoJSON.FeatureCollection;
}

interface SearchRequest {
  geoJson: string;
  srid: number;
}

interface GeoPackageRequest {
  geoJson: string;
  inputSrid: number;
  outputSrid: number;
}

const polygonGeoJson = JSON.stringify({
  type: "Polygon",
  coordinates: [[[17.9, 59.2], [18.2, 59.2], [18.2, 59.4], [17.9, 59.4], [17.9, 59.2]]]
} satisfies GeoJSON.Polygon);

// ── /search (SSE) ──────────────────────────────────────────────────────────

async function searchPolygon(request: SearchRequest): Promise<SearchLayer[]> {
  const response = await fetch(`${BASE_URL}/search`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${TOKEN}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) throw new Error(`HTTP ${response.status}`);

  const layers: SearchLayer[] = [];
  const reader  = response.body!.getReader();
  const decoder = new TextDecoder();
  let buffer    = "";

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split("\n");
    buffer = lines.pop()!;

    for (const line of lines) {
      if (line.startsWith("event: done")) { await reader.cancel(); return layers; }
      if (line.startsWith("data: ")) {
        layers.push(JSON.parse(line.slice("data: ".length)) as SearchLayer);
      }
    }
  }

  return layers;
}

const layers = await searchPolygon({ geoJson: polygonGeoJson, srid: 4326 });
layers.forEach(l => console.log(`Layer: ${l.title}, features: ${l.features.features.length}`));

// ── /search/geopackage ─────────────────────────────────────────────────────

async function downloadGeoPackage(request: GeoPackageRequest): Promise<void> {
  const response = await fetch(`${BASE_URL}/search/geopackage`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${TOKEN}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) throw new Error(`HTTP ${response.status}`);

  const blob = await response.blob();
  const url  = URL.createObjectURL(blob);
  const a    = document.createElement("a");
  a.href     = url;
  a.download = "search_results.gpkg";
  a.click();
  URL.revokeObjectURL(url);
}

await downloadGeoPackage({ geoJson: polygonGeoJson, inputSrid: 4326, outputSrid: 3006 });
```

---

## Configuration Reference

The tables that are searched and the attributes returned are defined in `appsettings.json`:

```json
"Tables": [
  {
    "TableName": "example_polygon.deso_2025",
    "Title":     "DeSO Areas 2025",
    "GeometryColumn": "geom",
    "AttributeColumns": ["desokod", "kommunnamn", "lanskod"]
  }
]
```

| Field | Description |
|---|---|
| `TableName` | Fully qualified PostGIS table name (`schema.table`) |
| `Title` | Human-readable layer name returned in the response |
| `GeometryColumn` | Name of the PostGIS geometry column |
| `AttributeColumns` | List of non-geometry columns to include in the response |
