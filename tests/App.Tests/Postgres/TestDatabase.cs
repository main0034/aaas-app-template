// Endpoint tests run against a real Postgres. This file is the plumbing; you
// should not need to change it. See AGENT.md, "Every new route gets a test".
//
// How it works:
//   - TEST_POSTGRES holds a connection string to a server the tests may create
//     and drop databases on. CI sets it; locally, set it to your own Postgres
//     (for example "Host=localhost;Username=postgres;Password=postgres").
//   - Once per test run, a template database is created and the app's
//     migrations are applied to it - the same migrations the init container runs.
//   - Every test gets its own database, cloned from that template
//     (CREATE DATABASE ... TEMPLATE, ~0.1s), so a test sees only the rows it
//     seeded itself and can assert exactly which rows come back. An endpoint that
//     commits its own transaction is still isolated, which rollback could not do.
//
// Without TEST_POSTGRES the endpoint tests are skipped with a message. CI runs
// them with --fail-skips, so a missing database there is a failure, never a pass.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

[assembly: AssemblyFixture(typeof(App.Tests.Postgres.TemplateDatabase))]

namespace App.Tests.Postgres;

public static class TestServer
{
    public const string Variable = "TEST_POSTGRES";

    public static NpgsqlConnectionStringBuilder? Settings { get; } =
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } cs
            ? new NpgsqlConnectionStringBuilder(cs)
            : null;

    public static string SkipReason =>
        $"{Variable} is not set, so endpoint tests cannot reach a Postgres. CI sets it; " +
        $"locally, point it at a Postgres you can create databases on.";

    public static async Task ExecuteAsync(string sql)
    {
        var admin = new NpgsqlConnectionStringBuilder(Settings!.ConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };
        await using var conn = new NpgsqlConnection(admin.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // Starts the real app, configured the way the platform configures it, against
    // one database.
    public static WebApplicationFactory<Program> AppFor(string database) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("PGHOST", Settings!.Host);
            b.UseSetting("PGUSER", Settings.Username);
            b.UseSetting("PGPASSWORD", Settings.Password);
            b.UseSetting("PGDATABASE", database);
        });
}

// One migrated database per test run, used only as a template to clone from.
public sealed class TemplateDatabase : IAsyncLifetime
{
    public string Name { get; } = $"app_test_template_{Guid.NewGuid():N}";

    public async ValueTask InitializeAsync()
    {
        if (TestServer.Settings is null)
        {
            return;
        }

        await TestServer.ExecuteAsync($"CREATE DATABASE \"{Name}\"");

        await using (var app = TestServer.AppFor(Name))
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<App.Data.AppDbContext>().Database.MigrateAsync();
        }

        // A database with open connections cannot be used as a template.
        NpgsqlConnection.ClearAllPools();
    }

    public async ValueTask DisposeAsync()
    {
        if (TestServer.Settings is not null)
        {
            await TestServer.ExecuteAsync($"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)");
        }
    }
}
