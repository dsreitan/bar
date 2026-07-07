using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Bff.Api.Tests;

/// <summary>
/// Thin wiring tests: token/user in → core invoked with correctly mapped
/// inputs → status and shape out. Licensing RULES are never asserted here —
/// that's the entitlements test suite's job.
/// </summary>
[TestFixture]
public class ApiSmokeTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Licensed_content_returns_200()
    {
        var response = await _client.GetAsync("/content/multi-tavle?userId=alice");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Multi 1-7 tavle");
    }

    [Test]
    public async Task Unlicensed_content_returns_403_with_a_reason()
    {
        var response = await _client.GetAsync("/content/multi-tavle?userId=dave");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("FAIL_PublicationsNoMatchingValues");
    }

    [Test]
    public async Task Unknown_content_returns_404()
    {
        var response = await _client.GetAsync("/content/no-such-content?userId=alice");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Why_endpoint_surfaces_the_dropped_publication_id_incident()
    {
        // bob's license carries publication 551909, which the registry doesn't
        // know — in production this is invisible; here it is line one of the trace.
        var response = await _client.GetAsync("/internal/entitlements/why?userId=bob&contentId=multi-tavle");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var trace = JsonDocument.Parse(body).RootElement;

        trace.GetProperty("verdict").GetString().Should().Be("Deny");
        trace.GetProperty("assembly").GetProperty("droppedPublicationIds").EnumerateArray()
            .Select(e => e.GetInt32()).Should().Contain(551909);
        body.Should().Contain("NOT in the publication registry");
    }

    [Test]
    public async Task Why_endpoint_explains_an_allow_with_the_matching_publication()
    {
        var response = await _client.GetAsync("/internal/entitlements/why?userId=alice&contentId=refleks-naturfag-kap1");

        var body = await response.Content.ReadAsStringAsync();
        var trace = JsonDocument.Parse(body).RootElement;

        trace.GetProperty("verdict").GetString().Should().Be("Allow");
        body.Should().Contain("Refleks naturfag");
        body.Should().Contain("Subject matched");
    }
}
