The endpoint must be authorized using an Authorization token verified through OAuth Token Introspection.

Can you explain how this should be implemented in c# in a webserver with a main server that will handle the POST endpoint and SSE updates, while the test client will be used to send requests and receive updates.

Yes. A clean way to implement this in C#/.NET is to separate the solution into three logical parts:

Authentication/authorization layer
Search processing layer
Real-time progress updates via SSE

A good architecture is:

[Test Client]
    |
    |  POST /search
    |  Authorization: Bearer eyJ...
    |
    v
[ASP.NET Core Web API]
    |
    |-- OAuth Token Introspection
    |
    |-- Search Service
    |     |-- Reads config
    |     |-- Searches PostGIS tables
    |     |-- Streams progress events
    |
    |-- SSE Endpoint
    |
    v
[PostgreSQL/PostGIS]
1. OAuth Token Introspection

The idea is:

Client sends:
Authorization: Bearer <access_token>
Your server DOES NOT validate the JWT itself.
Instead, it calls the OAuth Authorization Server introspection endpoint.

Typical flow:

Client --> Your API --> OAuth Server
                    --> "Is this token valid?"
                    <-- active:true

This is common when:

Keycloak
IdentityServer
Auth0
Azure AD gateway
Custom OAuth provider

are used.

2. Typical ASP.NET Core Setup

In .NET 8 minimal API or controller API.

Install package:

dotnet add package IdentityModel.AspNetCore.OAuth2Introspection

Then:

builder.Services
    .AddAuthentication("Bearer")
    .AddOAuth2Introspection("Bearer", options =>
    {
        options.Authority = "https://auth.mycompany.com";

        options.ClientId = "geo-search-api";
        options.ClientSecret = "super-secret";

        options.IntrospectionEndpoint =
            "https://auth.mycompany.com/connect/introspect";
    });

builder.Services.AddAuthorization();

Then:

app.UseAuthentication();
app.UseAuthorization();

Now every request with:

Authorization: Bearer xxxx

will automatically be introspected.

3. Protecting the POST Endpoint

Example:

app.MapPost("/search", async (
    SearchRequest request,
    HttpContext context,
    SearchService service) =>
{
    return await service.StartSearch(request, context);
})
.RequireAuthorization();

If token is invalid:

401 Unauthorized
4. The Hard Part: POST + SSE Together

SSE is:

Content-Type: text/event-stream

But POST endpoints usually return JSON once.

You have two main design options.

OPTION A (Recommended)
Two endpoints
1. POST starts the search
POST /search

Returns:

{
  "searchId": "abc-123"
}
2. SSE endpoint streams updates
GET /search/abc-123/events

This is MUCH cleaner.

Why This Is Better

Because:

POST starts work
SSE streams progress
easier scaling
easier retries
works behind proxies/load balancers
browser-friendly
easier testing

This is how many enterprise systems do it.

5. Suggested Flow
Client
Step 1
POST /search
Authorization: Bearer xxx

Body:

{
  "polygonWkt": "POLYGON((...))"
}

Response:

{
  "searchId": "6d2d..."
}
Step 2

Client connects:

GET /search/6d2d/events
Authorization: Bearer xxx
Accept: text/event-stream
6. SSE Implementation in ASP.NET Core

Example endpoint:

app.MapGet("/search/{id}/events",
async (
    string id,
    HttpContext context,
    SearchStateService stateService) =>
{
    context.Response.Headers.Append("Content-Type", "text/event-stream");

    await foreach (var update in stateService.Stream(id))
    {
        var json = JsonSerializer.Serialize(update);

        await context.Response.WriteAsync(
            $"data: {json}\n\n");

        await context.Response.Body.FlushAsync();
    }
})
.RequireAuthorization();
7. What the Client Receives

SSE format:

data: {"table":"Buildings","hits":12}

data: {"table":"Roads","hits":3}

data: {"table":"Rivers","hits":0}

data: {"completed":true}
8. Search Service Design

Your search service:

Read config
   |
Loop tables
   |
Run PostGIS intersection query
   |
Send SSE update
   |
Collect GeoJSON features
   |
Return final FeatureCollection
9. Example Table Config
{
  "tables": [
    {
      "tableName": "buildings",
      "title": "Buildings",
      "geometryColumn": "geom",
      "attributeColumns": [
        "id",
        "name",
        "height"
      ]
    }
  ]
}
10. Example PostGIS Query

Typical query:

SELECT
    id,
    name,
    ST_AsGeoJSON(geom) AS geometry
FROM buildings
WHERE ST_Intersects(
    geom,
    ST_GeomFromText(@wkt, 3006)
)
11. Recommended Internal Model
Search Job
public class SearchJob
{
    public string Id { get; set; }

    public Channel<SearchUpdate> Updates { get; set; }

    public FeatureCollection Results { get; set; }
}

Using:

System.Threading.Channels

is excellent for SSE streaming.

12. Recommended SSE Update Object
public class SearchUpdate
{
    public string TableTitle { get; set; }

    public int HitCount { get; set; }

    public object[] Features { get; set; }

    public bool Completed { get; set; }
}
13. Important Production Considerations
Reverse Proxy

If using:

NGINX
IIS
Traefik

disable buffering for SSE.

NGINX:

proxy_buffering off;
Timeouts

SSE connections can live long.

Increase:

Kestrel timeout
IIS timeout
Proxy timeout
Scalability

If multiple server instances exist:

Use:

Redis pub/sub
RabbitMQ
SignalR backplane

Otherwise SSE events may go to wrong instance.

14. Why SSE Is Good Here

SSE is ideal because:

one-way updates only
lightweight
easier than WebSockets
native browser support
perfect for progress reporting

You do NOT need WebSockets here.

15. Suggested Technology Stack

For your scenario I would strongly recommend:

ASP.NET Core 8
PostgreSQL/PostGIS
Npgsql
NetTopologySuite
SSE
OAuth2 Introspection
Docker deployment

Very aligned with your existing .NET + GIS background.

16. Suggested Client Test App

Simple HTML/JS:

const evt = new EventSource(
    "/search/123/events",
    {
        headers: {
            Authorization: "Bearer xxx"
        }
    });

OR a Blazor test client.

Blazor Server or WASM would fit nicely with your other projects like PostgreSQL + PostGIS + your existing .NET mapping stack.