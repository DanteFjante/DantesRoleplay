using System.Reflection;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Web.Pages;
using Microsoft.EntityFrameworkCore;
using Fixture = DantesRoleplay.Tests.WebPagePublicationSelectionTests.Fixture;

namespace DantesRoleplay.Tests;

public sealed class WebPublicationHostRoutingTests
{
    [Fact]
    public async Task Pinned_content_is_a_host_candidate_but_not_a_legacy_route()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Publication.CompareExchangeContentReferenceAsync(
            await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2));
        var expected = (await fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId)).Content;
        var discovery = Discovery(fixture);

        var host = await HostRouteAsync(discovery, "example");
        Assert.Equal("candidate", host.Status);
        Assert.Equal(expected, host.Reference);
        Assert.Equal(fixture.ApplicationId.Value, host.ApplicationId);
        Assert.Equal(Fixture.PublicationSpace, host.StateSpaceId);
        Assert.Equal(Fixture.EntityId, host.EntityId);
        Assert.Equal("content-missing", (await discovery.ResolvePageRouteAsync("example")).Status);
        Assert.Null(await discovery.GetPageAsync(fixture.ApplicationId, "example"));
    }

    [Fact]
    public async Task Legacy_content_remains_an_unpinned_host_candidate_and_ready_route()
    {
        await using var fixture = await Fixture.CreateAsync();
        var discovery = Discovery(fixture);
        var host = await HostRouteAsync(discovery, "example");
        Assert.Equal("candidate", host.Status);
        Assert.False(host.Reference!.IsPinned);
        Assert.Equal("ready", (await discovery.ResolvePageRouteAsync("example")).Status);
    }

    [Theory]
    [InlineData("slug")]
    [InlineData("index")]
    public async Task Pinned_and_legacy_pages_cannot_bypass_collision_checks(string collision)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (collision == "index")
            await fixture.Administration.SetIndexAsync(fixture.ApplicationId, Fixture.EntityId, new(true));
        var legacy = (await fixture.Entities.GetComponentAsync(Fixture.PublicationSpace,
            Fixture.EntityId, WebPageComponentTypes.Page))!;
        var value = JsonNode.Parse(legacy.ValueJson)!;
        if (collision == "index") value["slug"] = "other";
        await fixture.Publication.CompareExchangeContentReferenceAsync(
            await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2));
        // Deliberately retain invalid legacy state without the final constraint check. Both real
        // lookup paths must reject it; this is not authorized publication acceptance.
        await using (var transaction = await fixture.Transactions.BeginAsync())
        {
            await fixture.Entities.CreateEntityAsync(Fixture.PublicationSpace, "legacy-collision", "Legacy collision");
            await fixture.Entities.AddComponentAsync(new(Fixture.PublicationSpace, "legacy-collision", legacy.Type,
                value.ToJsonString(), 0));
            if (collision == "index")
            {
                var type = new SqliteComponentTypeRegistry(fixture.Data, new BoundedJsonSchemaValidator())
                    .GetLatest(WebPageComponentTypes.IndexPage)!;
                await fixture.Entities.AddComponentAsync(new(Fixture.PublicationSpace, "legacy-collision",
                    new(type.QualifiedId, type.Version, type.SchemaHash), "{}", 0));
            }
            await transaction.CommitAsync();
        }
        var discovery = Discovery(fixture);
        var host = await HostRouteAsync(discovery, "example");
        Assert.Equal("publication-invalid", host.Status);
        Assert.Null(host.Reference);
        Assert.Equal("publication-invalid", (await discovery.ResolvePageRouteAsync("example")).Status);
    }

    [Theory]
    [InlineData("hidden", "page-hidden")]
    [InlineData("disabled", "page-disabled")]
    [InlineData("malformed", "application-unavailable")]
    public async Task Unavailable_publication_entries_never_produce_a_host_candidate(string change, string status)
    {
        await using var fixture = await Fixture.CreateAsync();
        var page = (await fixture.Entities.GetComponentAsync(Fixture.PublicationSpace, Fixture.EntityId, WebPageComponentTypes.Page))!;
        if (change == "hidden")
            await fixture.Administration.UpdateMetadataAsync(fixture.ApplicationId, Fixture.EntityId,
                new(page.Revision, "Example", "Page", "example", 0, "hidden"));
        else if (change == "disabled")
            await fixture.Lifecycle.SetEntityEnabledAsync(Fixture.PublicationSpace, Fixture.EntityId, false,
                (await fixture.Entities.GetEntityAsync(Fixture.PublicationSpace, Fixture.EntityId))!.Revision);
        else
        {
            var value = JsonNode.Parse(page.ValueJson)!;
            value["activeContentReference"]!["revision"] = 2; // Incomplete pin in deliberately corrupt stored JSON.
            var malformed = value.ToJsonString();
            await fixture.Data.Database.ExecuteSqlInterpolatedAsync($"UPDATE system_ecs_component SET Data = {malformed} WHERE StateSpaceId = {Fixture.PublicationSpace} AND EntityId = {Fixture.EntityId} AND QualifiedTypeId = {WebPageComponentTypes.Page}");
        }
        var host = await HostRouteAsync(Discovery(fixture), "example");
        Assert.Equal(status, host.Status);
        Assert.Null(host.Reference);
    }

    [Fact]
    public async Task An_incomplete_state_space_scan_cannot_select_a_route()
    {
        await using var fixture = await Fixture.CreateAsync();
        var application = fixture.Applications.Get(fixture.ApplicationId)!;
        for (var index = 0; index < 100; index++)
            fixture.Spaces.Create(new($"earlier-state-{index:D3}", application,
                application.Fingerprint, application.Fingerprint, EcsStateSpaceScope.Runtime));
        Assert.NotNull(fixture.Spaces.ListPage(fixture.ApplicationId, null, 100).NextStateSpaceId);
        var host = await HostRouteAsync(Discovery(fixture), "example");
        Assert.Equal("publication-invalid", host.Status);
        Assert.Null(host.Reference);
    }

    private static WebPublicationDiscovery Discovery(Fixture fixture) => new(
        fixture.Applications, fixture.Spaces, fixture.Entities, fixture.Content, fixture.Lifecycle);

    private static async Task<(string Status, WebPageContentReference? Reference, string? ApplicationId,
        string? StateSpaceId, string? EntityId)> HostRouteAsync(
        WebPublicationDiscovery discovery, string slug)
    {
        var method = typeof(WebPublicationDiscovery).GetMethod("ResolveHostRouteAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(discovery, [slug, CancellationToken.None])!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var status = (string)result.GetType().GetProperty("Status")!.GetValue(result)!;
        var candidate = result.GetType().GetProperty("Candidate")!.GetValue(result);
        var reference = candidate is null ? null : (WebPageContentReference?)candidate.GetType()
            .GetProperty("ActiveContentReference")!.GetValue(candidate);
        object? Field(string name) => candidate?.GetType().GetProperty(name)!.GetValue(candidate);
        return (status, reference, (Field("ApplicationId") as ApplicationIdentifier)?.Value,
            Field("PublicationStateSpaceId") as string, Field("EntityId") as string);
    }
}
