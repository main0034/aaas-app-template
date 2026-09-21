using App.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace App;

// Entry point for `App migrate`, which the platform runs as an init container
// before each new revision starts. Exit code 0 means the schema is current and
// the app container may start; anything else stops the revision, and the old
// revision keeps serving.
//
// Safe to run concurrently: EF Core takes a database lock around the migration
// run, so replicas starting together apply each migration exactly once. Each
// migration runs in its own transaction, so a failure leaves no half-applied
// schema behind.
public static class MigrationRunner
{
    public static async Task<int> ApplyAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<Database>();
        if (!db.IsConfigured)
        {
            Console.Error.WriteLine("[migrate] PGHOST is not set; nothing to migrate against.");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            // On a fresh database the history table does not exist yet, and asking
            // EF for pending migrations would log a failed query - which reads like
            // the cause of whatever goes wrong next. Check for it first.
            var history = ctx.GetService<IHistoryRepository>();
            var pending = await history.ExistsAsync()
                ? (await ctx.Database.GetPendingMigrationsAsync()).ToList()
                : ctx.Database.GetMigrations().ToList();
            // Counted before the lock is taken, so a replica that waits on another
            // may report migrations that are already applied by the time it runs.
            Console.WriteLine(pending.Count == 0
                ? "[migrate] schema is current"
                : $"[migrate] {pending.Count} pending: {string.Join(", ", pending)}");

            await ctx.Database.MigrateAsync();

            var applied = await ctx.Database.GetAppliedMigrationsAsync();
            Console.WriteLine($"[migrate] done; latest is {applied.LastOrDefault() ?? "(none)"}");
            return 0;
        }
        catch (Exception ex)
        {
            // One line that names the cause, then the detail. The first line is
            // what shows up in the revision's failure summary.
            Console.Error.WriteLine($"[migrate] FAILED: {ex.GetBaseException().Message}");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
