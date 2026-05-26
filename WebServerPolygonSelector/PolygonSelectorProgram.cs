using WebServerPolygonSelector.Endpoints;
using WebServerPolygonSelector.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<GeoSearchService>();
builder.Services.AddHttpClient<TokenIntrospectionService>();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
    options.AddPolicy("BlazorClient", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors("BlazorClient");
app.MapSearchEndpoint();
app.MapGeoPackageEndpoint();

app.Run();
