using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace App.Data;

// Used when PGHOST is unset (CI, tests). AppDbContext must still be constructible
// - minimal APIs resolve it before request validation runs, so throwing at
// construction would turn every 400 into a 503. Instead the failure happens at
// the moment a connection is actually opened.
public sealed class NotConfiguredInterceptor : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
        throw new DatabaseNotConfiguredException();

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default) =>
        throw new DatabaseNotConfiguredException();
}
