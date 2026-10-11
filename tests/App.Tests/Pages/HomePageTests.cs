using App.Components.Pages;
using App.Data;
using App.Tests.Postgres;

namespace App.Tests.Pages;

public sealed class HomePageTests(TemplateDatabase template) : PageTest(template)
{
    [Fact]
    public async Task Lists_items_newest_first()
    {
        // Arrange
        await SeedAsync(new Item { Title = "Older item" }, new Item { Title = "Newer item" });

        // Act
        var html = await GetPageAsync<Home>("/");

        // Assert
        Assert.True(
            html.IndexOf("Newer item", StringComparison.Ordinal)
                < html.IndexOf("Older item", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Adding_an_item_shows_it_in_the_list()
    {
        // Act
        var html = await SubmitFormAsync<Home>(
            "/",
            "new-item",
            ("Input.Title", "Buy paint"),
            ("Input.Note", "white")
        );

        // Assert
        Assert.Contains("Buy paint", html);
        Assert.DoesNotContain("Nothing here yet", html);
    }

    [Fact]
    public async Task A_refused_item_shows_the_reason_and_is_not_added()
    {
        // Act: an empty title, which the route refuses
        var html = await SubmitFormAsync<Home>("/", "new-item", ("Input.Title", ""));

        // Assert
        Assert.Contains("class=\"errors\"", html);
        Assert.Contains("Nothing here yet", html);
    }
}
