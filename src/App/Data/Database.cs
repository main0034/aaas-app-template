// Database access using Entra ID authentication.
//
// There is no password. The container's user-assigned managed identity requests
// a short-lived token for Postgres and Npgsql uses it where a password would go.
//
// Why it is done this way: any stored credential has to live somewhere Terraform
// can see, which means it ends up in Terraform state and has to be readable by
// whatever identity runs `terraform plan`. Removing the credential entirely was
// the only thing that actually solved that.
//
// Consequences to be aware of:
//   - tokens expire (roughly an hour), so the password is a PROVIDER that Npgsql
//     calls for every new physical connection, not a fixed string. Azure.Identity
//     caches the token, so this is cheap.
//   - locally there is no managed identity, so set PGPASSWORD to fall back to
//     ordinary password auth against a local Postgres.
//
// Do not write your own connection logic. Use AppDbContext.

using Azure.Core;
using Azure.Identity;
using Npgsql;

namespace App.Data;

public sealed class Database : IAsyncDisposable
{
    // Scope for Azure Database for PostgreSQL. Not the ARM scope - a token for
    // https://management.azure.com/.default is rejected by Postgres with a
    // confusing authentication failure.
    private static readonly TokenRequestContext PostgresScope =
        new(["https://ossrdbms-aad.database.windows.net/.default"]);

    private readonly Lazy<NpgsqlDataSource> _dataSource;
    private TokenCredential? _credential;

    public Database(IConfiguration config)
    {
        // PGHOST has no default on purpose. Unset means "no database configured",
        // which lets CI run without one and keeps the app from requesting an
        // Azure token that would only time out.
        Host = config["PGHOST"] ?? "";
        Name = config["PGDATABASE"] ?? "postgres";
        User = config["PGUSER"] ?? "postgres";
        Password = config["PGPASSWORD"] ?? "";
        ClientId = config["AZURE_CLIENT_ID"];

        // Lazy: nothing touches the network until the first query. Startup must
        // not depend on the database - see the health contract in AGENT.md.
        _dataSource = new Lazy<NpgsqlDataSource>(Build);
    }

    public string Host { get; }
    public string Name { get; }
    public string User { get; }
    private string Password { get; }
    private string? ClientId { get; }

    public bool IsConfigured => Host.Length > 0;

    public bool UsesManagedIdentity => Password.Length == 0;

    public NpgsqlDataSource DataSource =>
        IsConfigured
            ? _dataSource.Value
            : throw new DatabaseNotConfiguredException();

    private NpgsqlDataSource Build()
    {
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Database = Name,
            Username = User,
            SslMode = Host == "localhost" ? SslMode.Prefer : SslMode.Require,
            // Npgsql probes for Kerberos (GSSAPI) encryption by default. The
            // runtime image has no Kerberos library, and nothing here uses it.
            GssEncryptionMode = GssEncryptionMode.Disable,
            Timeout = 15,
            // Tokens are per connection; don't keep idle connections long enough
            // for one to outlive its token.
            ConnectionIdleLifetime = 300,
        };

        var builder = new NpgsqlDataSourceBuilder(csb.ConnectionString);

        if (UsesManagedIdentity)
        {
            builder.UsePasswordProvider(
                _ => Credential().GetToken(PostgresScope, default).Token,
                async (_, ct) => (await Credential().GetTokenAsync(PostgresScope, ct)).Token);
        }
        else
        {
            builder.ConnectionStringBuilder.Password = Password;
        }

        return builder.Build();
    }

    // AZURE_CLIENT_ID is the user-assigned identity's client id, set by Terraform.
    // Without it the credential looks for a system-assigned identity and fails.
    private TokenCredential Credential() =>
        _credential ??= new ManagedIdentityCredential(
            ManagedIdentityId.FromUserAssignedClientId(ClientId
                ?? throw new InvalidOperationException(
                    "AZURE_CLIENT_ID is not set, and PGPASSWORD is empty. " +
                    "Set PGPASSWORD for local development.")));

    public async ValueTask DisposeAsync()
    {
        if (_dataSource.IsValueCreated)
        {
            await _dataSource.Value.DisposeAsync();
        }
    }
}

public sealed class DatabaseNotConfiguredException()
    : InvalidOperationException("database is not configured (PGHOST is unset)");
