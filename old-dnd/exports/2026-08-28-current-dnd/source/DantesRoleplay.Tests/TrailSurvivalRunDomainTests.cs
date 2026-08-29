using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class TrailSurvivalRunDomainTests
{
    private static readonly ApplicationIdentifier Application =
        ApplicationIdentifier.Parse("trail-survival");

    [Fact]
    public async Task Confirmed_domain_contracts_parse_compile_validate_register_and_replay()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        new SqliteApplicationRegistry(db).Register(new(
            Application,
            "Trail Survival",
            "Original customizable single-player trail-survival application.",
            []));
        var validator = new BoundedJsonSchemaValidator();
        var registry = new SqliteComponentTypeRegistry(db, validator);

        foreach (var contract in Contracts())
        {
            var directory = Path.Combine(RepositoryRoot(),
                "catalog", "applications", "trail-survival", "components", contract.Folder);
            var metadataPath = Path.Combine(directory, contract.Id + ".json");
            var schemaPath = Path.Combine(directory, contract.Id + ".schema.json");
            var definition = ComponentDefinitionFile.Parse(
                await File.ReadAllTextAsync(metadataPath),
                Path.GetRelativePath(RepositoryRoot(), metadataPath),
                await File.ReadAllTextAsync(schemaPath));

            Assert.Equal(contract.Id, definition.Id);
            Assert.False(string.IsNullOrWhiteSpace(definition.Name));
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
            var compilation = validator.Compile(definition.Schema);
            Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
            Assert.True(compilation.ProfileId is
                SystemJsonSchemaProfile.Version1Id or SystemJsonSchemaProfile.Version2Id);
            Assert.Equal(SchemaValueStatus.Valid,
                validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, contract.Valid).Status);
            Assert.All(contract.Invalid, value => Assert.Equal(
                SchemaValueStatus.Invalid,
                validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, value).Status));

            var registered = registry.Define(new(Application, definition.Id, definition.Schema));
            var replay = registry.Define(new(Application, definition.Id, definition.Schema));
            Assert.Equal(Application, registered.Owner);
            Assert.Equal(1, registered.Version);
            Assert.Equal(compilation.SchemaHash, registered.SchemaHash);
            Assert.Equal(registered, replay);
        }

        Assert.Equal(
            Contracts().Select(value => value.Id).Order(StringComparer.Ordinal),
            registry.ListLatestPage(Application, null, 100).ComponentTypes
                .Select(value => value.QualifiedId));
        var other = ApplicationIdentifier.Parse("dnd2024");
        new SqliteApplicationRegistry(db).Register(new(other, "D&D 2024", "Isolation witness.", []));
        Assert.Empty(registry.ListLatestPage(other, null, 100).ComponentTypes);
    }

    [Fact]
    public async Task Complete_domain_round_trips_and_rejects_invalid_or_cross_application_state()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var trailRevision = applications.Register(new(
            Application,
            "Trail Survival",
            "Original customizable single-player trail-survival application.",
            []));
        var other = ApplicationIdentifier.Parse("dnd2024");
        var otherRevision = applications.Register(new(
            other,
            "D&D 2024",
            "Isolation witness.",
            []));
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        stateSpaces.Create(new("trail-domain", trailRevision, new string('A', 64)));
        stateSpaces.Create(new("dnd-domain", otherRevision, new string('B', 64)));

        var validator = new BoundedJsonSchemaValidator();
        var registry = new SqliteComponentTypeRegistry(db, validator);
        var registered = Contracts().ToDictionary(
            contract => contract.Id,
            contract =>
            {
                var definition = ReadDefinition(contract);
                return registry.Define(new(Application, definition.Id, definition.Schema));
            },
            StringComparer.Ordinal);
        Assert.Equal(11, registered.Count);

        var store = new SqliteEntityComponentStore(db, registry, validator);
        foreach (var (entityId, name) in new[]
        {
            ("run.main", "Active run witness"),
            ("party.main", "Party witness"),
            ("member.ada", "Member witness"),
            ("conveyance.one", "Conveyance witness"),
            ("terminal.witness", "Terminal shape witness"),
            ("invalid.witness", "Invalid write witness")
        })
        {
            await store.CreateEntityAsync("trail-domain", entityId, name);
        }
        await store.CreateEntityAsync("dnd-domain", "other.witness", "Other application witness");

        foreach (var contract in Contracts())
        {
            var type = registered[contract.Id];
            var reference = new EcsComponentReference(type.QualifiedId, type.Version, type.SchemaHash);
            var written = await store.AddComponentAsync(new(
                "trail-domain",
                EntityFor(contract.Id),
                reference,
                contract.Valid,
                0));
            Assert.Equal(contract.Valid, written.ValueJson);
            Assert.Equal(contract.Valid, (await store.GetComponentAsync(
                "trail-domain", EntityFor(contract.Id), contract.Id))!.ValueJson);
        }

        var locators = Contracts().Select(contract =>
            new EcsComponentLocator(EntityFor(contract.Id), contract.Id)).ToArray();
        Assert.Equal(11, (await store.GetComponentsAsync("trail-domain", locators)).Count);

        var policy = registered["trail-survival.policy"];
        var policyReference = new EcsComponentReference(
            policy.QualifiedId, policy.Version, policy.SchemaHash);
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddComponentAsync(new(
            "trail-domain",
            "invalid.witness",
            policyReference,
            "{\"paceId\":\"pace.standard\",\"rationId\":\"\"}",
            0)));
        Assert.Null(await store.GetComponentAsync(
            "trail-domain", "invalid.witness", policy.QualifiedId));

        var otherType = registry.Define(new(
            other,
            "dnd2024.marker",
            "{\"type\":\"object\",\"additionalProperties\":false}"));
        var otherReference = new EcsComponentReference(
            otherType.QualifiedId, otherType.Version, otherType.SchemaHash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddComponentAsync(new(
            "trail-domain", "invalid.witness", otherReference, "{}", 0)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddComponentAsync(new(
            "dnd-domain", "other.witness", policyReference,
            "{\"paceId\":\"pace.standard\",\"rationId\":\"ration.standard\"}", 0)));
        Assert.Empty((await store.ListComponentsAsync(
            "dnd-domain", "other.witness", null, 100)).Components);
        Assert.DoesNotContain(
            registry.ListLatestPage(other, null, 100).ComponentTypes,
            value => value.QualifiedId.StartsWith("trail-survival.", StringComparison.Ordinal));
    }

    private static IReadOnlyList<ContractCase> Contracts() =>
    [
        new(
            "run",
            "trail-survival.scenario-pin",
            "{\"scenarioId\":\"scenario.first\",\"scenarioVersion\":1,\"scenarioContentHash\":\"" +
                new string('A', 64) + "\",\"rulesProfileId\":\"rules.standard\"}",
            [
                "{\"scenarioId\":\"scenario.first\",\"scenarioVersion\":0,\"scenarioContentHash\":\"" + new string('A', 64) + "\",\"rulesProfileId\":\"rules.standard\"}",
                "{\"scenarioId\":\"scenario.first\",\"scenarioVersion\":1,\"scenarioContentHash\":\"bad\",\"rulesProfileId\":\"rules.standard\"}",
                "{\"scenarioId\":\"scenario.first\",\"scenarioVersion\":1,\"scenarioContentHash\":\"" + new string('A', 64) + "\",\"rulesProfileId\":\"rules.standard\",\"title\":\"derived\"}"
            ]),
        new(
            "run",
            "trail-survival.run",
            "{\"phase\":\"setup\",\"turn\":0,\"partyId\":\"party.main\",\"randomSeed\":1,\"seedCursor\":0}",
            [
                "{\"phase\":\"unknown\",\"turn\":0,\"partyId\":\"party.main\",\"randomSeed\":1,\"seedCursor\":0}",
                "{\"phase\":\"setup\",\"turn\":-1,\"partyId\":\"party.main\",\"randomSeed\":1,\"seedCursor\":0}",
                "{\"phase\":\"setup\",\"turn\":0,\"partyId\":\"party.main\",\"randomSeed\":0,\"seedCursor\":0}",
                "{\"phase\":\"setup\",\"turn\":0,\"partyId\":\"party.main\",\"randomSeed\":1,\"seedCursor\":0,\"availableActions\":[]}"
            ]),
        new(
            "run",
            "trail-survival.clock",
            "{\"elapsedMinutes\":0}",
            [
                "{\"elapsedMinutes\":-1}",
                "{\"elapsedMinutes\":1.5}",
                "{\"elapsedMinutes\":0,\"day\":1}"
            ]),
        new(
            "run",
            "trail-survival.route-progress",
            "{\"routeId\":\"route.first\",\"currentLandmarkId\":\"landmark.start\",\"activeLegId\":null,\"distanceIntoLeg\":0,\"visitedLandmarkIds\":[\"landmark.start\"]}",
            [
                "{\"routeId\":\"route.first\",\"currentLandmarkId\":\"landmark.start\",\"activeLegId\":null,\"distanceIntoLeg\":-1,\"visitedLandmarkIds\":[\"landmark.start\"]}",
                "{\"routeId\":\"route.first\",\"currentLandmarkId\":\"landmark.start\",\"activeLegId\":null,\"distanceIntoLeg\":0,\"visitedLandmarkIds\":[]}",
                "{\"routeId\":\"route.first\",\"currentLandmarkId\":\"landmark.start\",\"activeLegId\":null,\"distanceIntoLeg\":0,\"visitedLandmarkIds\":[\"landmark.start\"],\"percentComplete\":0}"
            ]),
        new(
            "party",
            "trail-survival.party",
            "{\"name\":\"Northbound Company\",\"memberIds\":[\"member.ada\",\"member.bo\"],\"conveyanceId\":\"conveyance.one\"}",
            [
                "{\"name\":\"Northbound Company\",\"memberIds\":[],\"conveyanceId\":null}",
                "{\"name\":\"Northbound Company\",\"memberIds\":[\"member.ada\",\"member.ada\"],\"conveyanceId\":null}",
                "{\"name\":\"Northbound Company\",\"memberIds\":[\"member.ada\"],\"conveyanceId\":null,\"memberCount\":1}"
            ]),
        new(
            "party",
            "trail-survival.member",
            "{\"name\":\"Ada\",\"roleId\":\"role.navigator\",\"status\":\"active\",\"healthPoints\":100,\"conditionIds\":[]}",
            [
                "{\"name\":\"Ada\",\"roleId\":\"role.navigator\",\"status\":\"missing\",\"healthPoints\":100,\"conditionIds\":[]}",
                "{\"name\":\"Ada\",\"roleId\":\"role.navigator\",\"status\":\"active\",\"healthPoints\":-1,\"conditionIds\":[]}",
                "{\"name\":\"Ada\",\"roleId\":\"role.navigator\",\"status\":\"active\",\"healthPoints\":100,\"conditionIds\":[\"condition.tired\",\"condition.tired\"]}"
            ]),
        new(
            "party",
            "trail-survival.conveyance",
            "{\"kindId\":\"conveyance.handcart\",\"status\":\"operational\",\"condition\":80,\"maximumCondition\":100,\"cargoCapacity\":500}",
            [
                "{\"kindId\":\"conveyance.handcart\",\"status\":\"unknown\",\"condition\":80,\"maximumCondition\":100,\"cargoCapacity\":500}",
                "{\"kindId\":\"conveyance.handcart\",\"status\":\"operational\",\"condition\":-1,\"maximumCondition\":100,\"cargoCapacity\":500}",
                "{\"kindId\":\"conveyance.handcart\",\"status\":\"operational\",\"condition\":80,\"maximumCondition\":100,\"cargoCapacity\":500,\"remainingCapacity\":400}"
            ]),
        new(
            "party",
            "trail-survival.resources",
            "{\"entries\":[{\"resourceId\":\"resource.food\",\"quantity\":40},{\"resourceId\":\"resource.parts\",\"quantity\":2}]}",
            [
                "{\"entries\":[{\"resourceId\":\"resource.food\",\"quantity\":-1}]}",
                "{\"entries\":[{\"resourceId\":\"resource.food\",\"quantity\":1},{\"resourceId\":\"resource.food\",\"quantity\":1}]}",
                "{\"entries\":[{\"resourceId\":\"resource.food\",\"quantity\":40,\"weight\":20}]}"
            ]),
        new(
            "decision",
            "trail-survival.policy",
            "{\"paceId\":\"pace.standard\",\"rationId\":\"ration.standard\"}",
            [
                "{\"paceId\":\"\",\"rationId\":\"ration.standard\"}",
                "{\"paceId\":\"pace.standard\"}",
                "{\"paceId\":\"pace.standard\",\"rationId\":\"ration.standard\",\"dailyFoodCost\":4}"
            ]),
        new(
            "decision",
            "trail-survival.pending-choice",
            "{\"eventId\":\"event.fork\",\"choiceIds\":[\"choice.left\",\"choice.right\"],\"openedTurn\":3}",
            [
                "{\"eventId\":\"event.fork\",\"choiceIds\":[],\"openedTurn\":3}",
                "{\"eventId\":\"event.fork\",\"choiceIds\":[\"choice.left\",\"choice.left\"],\"openedTurn\":3}",
                "{\"eventId\":\"event.fork\",\"choiceIds\":[\"choice.left\"],\"openedTurn\":-1}",
                "{\"eventId\":\"event.fork\",\"choiceIds\":[\"choice.left\"],\"openedTurn\":3,\"prompt\":\"derived\"}"
            ]),
        new(
            "decision",
            "trail-survival.outcome",
            "{\"kind\":\"victory\",\"causeId\":\"outcome.destination-reached\",\"reachedTurn\":20}",
            [
                "{\"kind\":\"active\",\"causeId\":\"outcome.none\",\"reachedTurn\":0}",
                "{\"kind\":\"victory\",\"causeId\":\"\",\"reachedTurn\":20}",
                "{\"kind\":\"defeat\",\"causeId\":\"outcome.party-lost\",\"reachedTurn\":-1}",
                "{\"kind\":\"victory\",\"causeId\":\"outcome.destination-reached\",\"reachedTurn\":20,\"summary\":\"derived\"}"
            ])
    ];

    private static ComponentDefinitionFile ReadDefinition(ContractCase contract)
    {
        var directory = Path.Combine(RepositoryRoot(),
            "catalog", "applications", "trail-survival", "components", contract.Folder);
        var metadataPath = Path.Combine(directory, contract.Id + ".json");
        var schemaPath = Path.Combine(directory, contract.Id + ".schema.json");
        return ComponentDefinitionFile.Parse(
            File.ReadAllText(metadataPath),
            Path.GetRelativePath(RepositoryRoot(), metadataPath),
            File.ReadAllText(schemaPath));
    }

    private static string EntityFor(string qualifiedTypeId) => qualifiedTypeId switch
    {
        "trail-survival.party" or "trail-survival.resources" => "party.main",
        "trail-survival.member" => "member.ada",
        "trail-survival.conveyance" => "conveyance.one",
        "trail-survival.outcome" => "terminal.witness",
        _ => "run.main"
    };

    private static string RepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DANTES_ROLEPLAY_TEST_REPOSITORY_ROOT");
        foreach (var start in new[] { configured, Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
                     .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>())
        {
            for (var directory = new DirectoryInfo(start);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                    return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private sealed record ContractCase(
        string Folder,
        string Id,
        string Valid,
        IReadOnlyList<string> Invalid);
}
