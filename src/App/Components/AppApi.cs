// How a page reaches data: through this application's own routes, over HTTP,
// never through AppDbContext.
//
// The routes are what the tests and the spec-testers' acceptance tests prove. A
// page that wrote to the database itself would carry its own copy of the rules
// (trimming, limits, what may be cleared) and nothing would test that copy. A page
// that calls the route gets exactly the behaviour that was accepted. See AGENT.md,
// "Screens". scripts/check-pages.py keeps database access out of Components/.
//
// A call to a path or method that no route serves throws, so a page that calls a
// route which does not exist fails its render test instead of showing an empty list.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace App.Components;

public sealed class AppApi(HttpClient http)
{
    // Set by the middleware in Program.cs on any response no route matched.
    public const string UnmatchedHeader = "X-App-Unmatched-Route";

    // GET a route that must answer 200.
    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default) =>
        await TryGetAsync<T>(path, ct)
        ?? throw new InvalidOperationException($"GET {path} answered 404.");

    // GET a route that may answer 404 (an id that does not exist): null then.
    public async Task<T?> TryGetAsync<T>(string path, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(path, ct);
        EnsureRouteExists(response, HttpMethod.Get, path);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }

    public Task<ApiResult> PostAsync(string path, object body, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, path, body, ct);

    public Task<ApiResult> PutAsync(string path, object body, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, path, body, ct);

    public Task<ApiResult> PatchAsync(string path, object body, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, path, body, ct);

    public Task<ApiResult> DeleteAsync(string path, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, path, null, ct);

    private async Task<ApiResult> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        using var response = await http.SendAsync(request, ct);
        EnsureRouteExists(response, method, path);

        if (response.IsSuccessStatusCode)
        {
            return new ApiResult(response.StatusCode, []);
        }
        if ((int)response.StatusCode >= 500)
        {
            response.EnsureSuccessStatusCode();
        }
        return new ApiResult(response.StatusCode, await MessagesAsync(response, ct));
    }

    private static void EnsureRouteExists(
        HttpResponseMessage response,
        HttpMethod method,
        string path
    )
    {
        if (
            response.StatusCode == HttpStatusCode.MethodNotAllowed
            || response.Headers.Contains(UnmatchedHeader)
        )
        {
            throw new InvalidOperationException(
                $"A page called {method} {path}, and no route serves it. Pages may only call "
                    + "routes that exist (AGENT.md, \"Screens\")."
            );
        }
    }

    // The route's own reasons, from its problem details: validation errors per
    // field, or the title. A user reads these, so no status codes.
    private static async Task<IReadOnlyList<string>> MessagesAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        var messages = new List<string>();
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (
                problem.ValueKind == JsonValueKind.Object
                && problem.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Object
            )
            {
                foreach (var field in errors.EnumerateObject())
                {
                    messages.AddRange(
                        field.Value.EnumerateArray().Select(e => e.GetString()).OfType<string>()
                    );
                }
            }
        }
        catch (JsonException)
        {
            // No problem details: fall through to the generic message.
        }

        if (messages.Count == 0)
        {
            messages.Add(
                response.StatusCode == HttpStatusCode.NotFound
                    ? "That no longer exists."
                    : "That was not accepted. Check the fields and try again."
            );
        }
        return messages;
    }
}

public sealed record ApiResult(HttpStatusCode Status, IReadOnlyList<string> Messages)
{
    public bool Succeeded => (int)Status < 300;
}
