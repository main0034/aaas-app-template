using System.Net.Sockets;
using App.Data;
using Azure.Identity;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace App;

// Fail with a status code, not a stack trace: an unreachable or unconfigured
// database is a 503. Anything else falls through to the default handler, which
// returns a 500 problem response without exception details.
public sealed class DatabaseUnavailableHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext http, Exception exception, CancellationToken ct)
    {
        if (!IsDatabaseFailure(exception))
        {
            return false;
        }

        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "database unavailable",
                Detail = exception is DatabaseNotConfiguredException
                    ? exception.Message
                    : exception.GetBaseException().Message,
            },
        });
    }

    public static bool IsDatabaseFailure(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is DatabaseNotConfiguredException
                or NpgsqlException
                or AuthenticationFailedException
                or CredentialUnavailableException
                or SocketException
                or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }
}
