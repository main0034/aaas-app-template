using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace App.Data;

// Used only by `dotnet ef` (migrations add, has-pending-model-changes). Neither
// command connects to a database, so a placeholder connection string is enough -
// which is what lets CI check the model without a database or an Azure identity.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=design-time-only")
            .Options);
}
