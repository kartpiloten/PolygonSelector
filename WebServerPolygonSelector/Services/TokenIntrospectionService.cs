using System.Net.Http.Headers;
using System.Text.Json;

namespace WebServerPolygonSelector.Services;

public class TokenIntrospectionService(IConfiguration config, HttpClient httpClient)
{
    private readonly string _endpoint = config["OAuth:IntrospectionEndpoint"]
        ?? throw new InvalidOperationException("OAuth IntrospectionEndpoint is missing.");
    private readonly string _clientId = config["OAuth:ClientId"] ?? string.Empty;
    private readonly string _clientSecret = config["OAuth:ClientSecret"] ?? string.Empty;

    public async Task<bool> IsActiveAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"))
        );
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["token_type_hint"] = "access_token"
        });

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return false;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("active", out var active) && active.GetBoolean();
    }
}
