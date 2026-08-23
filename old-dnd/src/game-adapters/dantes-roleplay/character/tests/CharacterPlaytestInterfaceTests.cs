using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using Json.Schema;

namespace DantesRoleplay.Tests;

public sealed class CharacterPlaytestInterfaceTests : IDisposable
{
    private const string Record = "dnd2024.playtest-character-record";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"character-playtest-interface-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Creates_a_complete_draft_actor_and_record_in_one_effect_transaction()
    {
        var setup = await ArrangeAsync();
        var applied = await setup.Effects.ApplyAsync(CreateDraft(setup.ActorId), dryRun: false);

        Assert.True(applied.Applied);
        var actor = await setup.World.GetEntityAsync(setup.ActorId);
        Assert.NotNull(actor);
        using var record = JsonDocument.Parse(Component(actor!, Record));
        Assert.Equal("dnd2024-playtest-character-record-v1", record.RootElement.GetProperty("format").GetString());
        Assert.Equal("draft", record.RootElement.GetProperty("state").GetString());
        Assert.Equal("Wizard", record.RootElement.GetProperty("entries")[0].GetProperty("label").GetString());
        Assert.DoesNotContain(actor.Components, component => component.DefinitionId is "dnd2024.character.class-membership" or "dnd2024.spellcasting");
    }

    [Fact]
    public async Task Catalog_schema_rejects_a_forbidden_rule_like_record_field()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == Record).Schema);
        using var invalid = JsonDocument.Parse(PlaytestRecord("draft", """[{"kind":"spell","key":"fire-bolt","label":"Fire Bolt","effect":"deal damage"}]"""));
        using var valid = JsonDocument.Parse(PlaytestRecord("draft", """[{"kind":"spell","key":"fire-bolt","label":"Fire Bolt"}]"""));

        Assert.False(schema.Evaluate(invalid.RootElement).IsValid);
        Assert.True(schema.Evaluate(valid.RootElement).IsValid);
    }

    [Fact]
    public async Task Replaces_a_complete_valid_record_without_creating_rule_state()
    {
        var setup = await ArrangeAsync();
        Assert.True((await setup.Effects.ApplyAsync(CreateDraft(setup.ActorId), dryRun: false)).Applied);

        var active = new Effect { Type = EffectType.ComponentSet, EntityId = setup.ActorId, DefinitionId = Record, Data = PlaytestRecord("active", """[{"kind":"class","key":"wizard","label":"Wizard"},{"kind":"rule-ruling","key":"first-session","label":"First-session ruling","details":"Magic is GM adjudicated."}]""") };
        Assert.True((await setup.Effects.ApplyAsync([active], dryRun: false)).Applied);
        using var record = JsonDocument.Parse(Component((await setup.World.GetEntityAsync(setup.ActorId))!, Record));
        Assert.Equal("active", record.RootElement.GetProperty("state").GetString());
        Assert.Equal(2, record.RootElement.GetProperty("entries").GetArrayLength());
        var actor = (await setup.World.GetEntityAsync(setup.ActorId))!;
        Assert.DoesNotContain(actor.Components, component => component.DefinitionId is "dnd2024.character.class-membership" or "dnd2024.spellcasting");
    }

    [Fact]
    public async Task C15_attachment_is_separate_from_and_does_not_mutate_the_playtest_record()
    {
        var setup = await ArrangeAsync();
        Assert.True((await setup.Effects.ApplyAsync(CreateDraft(setup.ActorId), dryRun: false)).Applied);
        var before = Component((await setup.World.GetEntityAsync(setup.ActorId))!, Record);

        await setup.World.CreateEntityAsync("Playtest campaign", setup.CampaignId);
        await setup.World.SetComponentAsync(setup.CampaignId, "game.core.campaign.root", "{\"status\":\"active\"}");
        var attacher = new CampaignCharacterParticipationAttacher(setup.Db, setup.World, setup.Effects, new OperationLog(setup.Db));
        var attached = await attacher.AttachAsync(new("attach-character-participation", setup.CampaignId, setup.ActorId));

        Assert.True(attached.Attached, attached.Problems.FirstOrDefault()?.Reason);
        Assert.Equal(before, Component((await setup.World.GetEntityAsync(setup.ActorId))!, Record));
        var scope = await new CampaignCharacterParticipationVerifier(setup.World).ResolveActiveScopeAsync(setup.ActorId);
        Assert.True(scope.Valid);
    }

    private async Task<Setup> ArrangeAsync()
    {
        Copy(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        return new(db, world, new EffectApplier(db, world), "actor.playtest.test-wizard", "campaign.playtest.test");
    }

    private static Effect[] CreateDraft(string actorId, string? record = null) =>
    [
        new() { Type = EffectType.EntityCreate, EntityId = actorId, Name = "Playtest Wizard" },
        new() { Type = EffectType.ComponentAdd, EntityId = actorId, DefinitionId = "dnd2024.abilities", Data = "{\"str\":8,\"dex\":14,\"con\":13,\"int\":17,\"wis\":12,\"cha\":10}" },
        new() { Type = EffectType.ComponentAdd, EntityId = actorId, DefinitionId = Record, Data = record ?? PlaytestRecord("draft", """[{"kind":"class","key":"wizard","label":"Wizard","details":"GM adjudicated."}]""") }
    ];

    private static string PlaytestRecord(string state, string entries) =>
        "{\"format\":\"dnd2024-playtest-character-record-v1\",\"state\":\"" + state + "\",\"entries\":" + entries + "}";

    private static string Component(EntitySnapshot actor, string definitionId) =>
        Assert.Single(actor.Components, component => component.DefinitionId == definitionId).Data;

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "catalog", "manifest.json"))) return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    private sealed record Setup(DantesRoleplayDbContext Db, WorldStore World, EffectApplier Effects, string ActorId, string CampaignId);
}
