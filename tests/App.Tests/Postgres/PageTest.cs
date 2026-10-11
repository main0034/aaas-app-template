// Base class for page tests. A page is tested by rendering it against a real,
// seeded Postgres and, if it has a form, by submitting the form - the same way
// a browser does, with no JavaScript. See AGENT.md, "Screens".
//
// What a page test proves: the page renders (200, HTML), it shows the rows it
// should, and its form reaches a route that exists. What it does not prove: the
// route's rules. Those are the route's endpoint and acceptance tests.

using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace App.Tests.Postgres;

public abstract class PageTest(TemplateDatabase template) : EndpointTest(template)
{
    // GETs a page and asserts it rendered. TPage names the component, which is how
    // scripts/check-pages.py knows every page has a test.
    protected async Task<string> GetPageAsync<TPage>(string path)
        where TPage : IComponent
    {
        Assert.NotEmpty(typeof(TPage).GetCustomAttributes<RouteAttribute>());
        using var response = await Client.GetAsync(path, Ct);
        return await RenderedAsync(response, $"GET {path}");
    }

    // Submits a page's form as a browser would: form fields, posted to the page,
    // naming the form. Fields are named as the form binds them ("Input.Title").
    // Follows the redirect a successful submit makes, so the returned HTML is the
    // page the user lands on.
    protected async Task<string> SubmitFormAsync<TPage>(
        string path,
        string formName,
        params (string Name, string Value)[] fields
    )
        where TPage : IComponent
    {
        Assert.NotEmpty(typeof(TPage).GetCustomAttributes<RouteAttribute>());
        using var content = new FormUrlEncodedContent(
            fields
                .Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
                .Prepend(new("_handler", formName))
        );
        using var response = await Client.PostAsync(path, content, Ct);
        return await RenderedAsync(response, $"POST {path} ({formName})");
    }

    private static async Task<string> RenderedAsync(HttpResponseMessage response, string what)
    {
        var html = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"{what} answered {(int)response.StatusCode}:\n{html}"
        );
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        return WebUtility.HtmlDecode(html);
    }
}
