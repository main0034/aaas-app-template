// Base class for endpoint tests. Derive from it, seed rows, call the endpoint
// through Client, and assert exactly which rows come back. Each test starts with
// an empty, fully migrated database of its own. See AGENT.md for a worked example.

using App.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit.v3;

namespace App.Tests.Postgres;

[EndpointCategory]
public abstract class EndpointTest(TemplateDatabase template) : IAsyncLifetime
{
    private readonly string _database = $"app_test_{Guid.NewGuid():N}";
    private WebApplicationFactory<Program>? _app;

    protected HttpClient Client { get; private set; } = null!;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(TestServer.Settings is null, TestServer.SkipReason);
        await TestServer.ExecuteAsync($"CREATE DATABASE \"{_database}\" TEMPLATE \"{template.Name}\"");
        _app = TestServer.AppFor(_database);
        Client = _app.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_app is null)
        {
            return;
        }

        Client.Dispose();
        await _app.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await TestServer.ExecuteAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");
    }

    // Arrange rows directly, so a test controls exactly what is in the table
    // (dates in the past, values the API would refuse) before calling the endpoint.
    protected async Task SeedAsync(params object[] rows)
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.AddRange(rows);
        await ctx.SaveChangesAsync(Ct);
    }
}

// Marks every class derived from EndpointTest with Category=Endpoint, which is
// how CI selects them. Inherited, so a derived class cannot forget it.
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class EndpointCategoryAttribute : Attribute, ITraitAttribute
{
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new("Category", "Endpoint")];
}
