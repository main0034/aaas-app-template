// Tests run in CI with no database available.
//
// That constraint is deliberate: it forces the app to start and report healthy
// without Postgres, which is exactly the behaviour the container probes depend on.
// If you find yourself wanting a database to make a test pass, the app has
// probably grown a startup dependency it should not have.
//
// Migrations are the exception, and they are not tested here: CI applies them to
// a real, throwaway Postgres in a separate job.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Tests;

public sealed class HealthTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    // PGHOST is forced empty, so the tests behave the same on a laptop with a
    // local Postgres configured as they do in CI with none.
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(b => b.UseSetting("PGHOST", ""))
        .CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_returns_ok()
    {
        var response = await _client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_reports_unconfigured_database()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/ready", Ct);

        Assert.Contains(body.GetProperty("database").GetString(), new[] { "unconfigured", "unavailable" });
    }

    [Fact]
    public async Task Items_fails_cleanly_without_a_database()
    {
        // 503, not a stack trace.
        var response = await _client.GetAsync("/items", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Create_item_rejects_empty_title()
    {
        // Validation runs before the database is touched, so this is a 400 even
        // with no database configured.
        var response = await _client.PostAsJsonAsync("/items", new { title = "" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
