using System.Net;
using System.Net.Http.Json;
using App.Data;
using App.Tests.Postgres;

namespace App.Tests;

public sealed class ItemsEndpointTests(TemplateDatabase template) : EndpointTest(template)
{
    [Fact]
    public async Task Post_then_get_returns_the_item_newest_first()
    {
        // Arrange
        await SeedAsync(new Item { Title = "older" });

        // Act
        var created = await Client.PostAsJsonAsync(
            "/items",
            new { title = "newer", note = "n" },
            Ct
        );
        var items = await Client.GetFromJsonAsync<List<Item>>("/items", Ct);

        // Assert
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(["newer", "older"], items!.Select(i => i.Title));
    }
}
