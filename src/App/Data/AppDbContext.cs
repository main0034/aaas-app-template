using Microsoft.EntityFrameworkCore;

namespace App.Data;

// The data model. Change it here, then run `dotnet ef migrations add <Name>`.
// Never change the schema any other way - see AGENT.md.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // snake_case in the database, PascalCase in C#. Explicit rather than a
        // naming-convention package: one less dependency, and the SQL an agent
        // reads in a migration matches the table it will see in psql.
        modelBuilder.Entity<Item>(e =>
        {
            e.ToTable("items");
            e.Property(i => i.Id).HasColumnName("id");
            e.Property(i => i.Title).HasColumnName("title");
            e.Property(i => i.Note).HasColumnName("note");
        });
    }
}
