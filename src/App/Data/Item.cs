using System.ComponentModel.DataAnnotations;

namespace App.Data;

// A trivial Postgres-backed resource. It exists to prove the connection and the
// migration path work end to end. Replace it when generating a real application.
public sealed class Item
{
    public int Id { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    [MaxLength(2000)]
    public string? Note { get; set; }
}
