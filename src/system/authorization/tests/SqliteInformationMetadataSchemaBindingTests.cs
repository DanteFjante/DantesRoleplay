using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Information;
using DantesRoleplay.Interactions;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.CatalogNamespaces;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    private const string MetadataTypeId = "demo.schema.information-metadata";
    private const string RankSchema = """
        {"type":"object","properties":{"rank":{"type":"integer","minimum":1}},"required":["rank"],"additionalProperties":false}
        """;

    [Fact]
    public async Task Existing_inline_schema_semantics_are_explicit_in_readback()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info");
        await SeedInformationGrantAsync(db, ["demo.info"], [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var service = InformationService(db, setup);
        await service.WriteSourceAsync(ApplicationHost(setup, "inline-source", InteractionExecutionProfile.Atomic),
            new(new("source.inline", "demo.info", "Inline"), 0, "demo.info.inline"));
        await service.WriteRecordAsync(ApplicationHost(setup, "inline-record", InteractionExecutionProfile.Atomic),
            new(new("record.inline", "source.inline", "Inline", "Preserved", "{}"), 0));

        var read = await service.ReadRecordRevisionAsync(ApplicationHost(setup, "inline-read", grantReference: "grant@1"),
            new("record.inline"));

        Assert.Equal("completed", read.Status);
        Assert.Equal("inline", read.Record!.MetadataSchema.Mode);
        Assert.Null(read.Record.MetadataSchema.RegisteredSchema);
        Assert.Equal(1, read.Record.MetadataSchema.SourceRevision);
        Assert.Equal("{}", read.Record.MetadataSchema.SchemaJson);
    }

    [Fact]
    public async Task Registered_metadata_schema_is_pinned_for_current_and_historical_readback()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info"); AddMetadataSchemaNamespace(setup);
        await SeedInformationGrantAsync(db, ["demo.info"], [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var versionOne = types.Define(new(Application, MetadataTypeId, RankSchema));
        var service = InformationService(db, setup, new InformationStore(db, schemas, types));
        var referenceOne = new EcsComponentReference(versionOne.QualifiedId, versionOne.Version, versionOne.SchemaHash);

        var sourceWrite = await service.WriteSourceAsync(ApplicationHost(setup, "schema-source-v1", InteractionExecutionProfile.Atomic),
            new(new("source.schema", "demo.info", "Schema", MetadataSchema: referenceOne), 0, "demo.info.schema"));
        var recordWrite = await service.WriteRecordAsync(ApplicationHost(setup, "schema-record-v1", InteractionExecutionProfile.Atomic),
            new(new("record.schema", "source.schema", "Rank", "Preserved", "{\"rank\":1}"), 0));
        var versionTwo = types.Define(new(Application, MetadataTypeId,
            """{"type":"object","properties":{"rank":{"type":"integer","minimum":1},"note":{"type":"string"}},"required":["rank"],"additionalProperties":false}"""));
        var referenceTwo = new EcsComponentReference(versionTwo.QualifiedId, versionTwo.Version, versionTwo.SchemaHash);
        var sourceRevision = await service.WriteSourceAsync(ApplicationHost(setup, "schema-source-v2", InteractionExecutionProfile.Atomic),
            new(new("source.schema", "demo.info", "Schema", MetadataSchema: referenceTwo), 1, "demo.info.schema"));

        var read = await service.ReadRecordRevisionAsync(ApplicationHost(setup, "schema-read", grantReference: "grant@1"),
            new("record.schema", 1));

        Assert.Equal(InteractionInvocationResultTag.Committed, sourceWrite.Tag);
        Assert.Equal(InteractionInvocationResultTag.Committed, recordWrite.Tag);
        Assert.Equal(InteractionInvocationResultTag.Committed, sourceRevision.Tag);
        Assert.Equal("completed", read.Status);
        Assert.Equal("{\"rank\":1}", read.Record!.MetadataJson);
        Assert.Equal("registered", read.Record.MetadataSchema.Mode);
        Assert.Equal(1, read.Record.MetadataSchema.SourceRevision);
        Assert.Equal(referenceOne, read.Record.MetadataSchema.RegisteredSchema);
        var currentSource = await db.Set<InformationSource>().AsNoTracking().SingleAsync();
        Assert.Equal(2, currentSource.Revision);
        Assert.Equal(referenceTwo.QualifiedTypeId, currentSource.MetadataSchemaQualifiedId);
        Assert.Equal(referenceTwo.TypeVersion, currentSource.MetadataSchemaVersion);
        Assert.Equal(referenceTwo.SchemaHash, currentSource.MetadataSchemaHash);
        Assert.Equal(1, (await db.Set<InformationRecord>().AsNoTracking().SingleAsync()).MetadataSchemaSourceRevision);
        Assert.Equal(2, await db.Set<InformationContentRevisionRecord>().CountAsync(value => value.Kind == "source"));
        Assert.Single(await db.Set<InformationContentRevisionRecord>().Where(value => value.Kind == "record").ToArrayAsync());
    }

    [Fact]
    public async Task Registered_schema_rejects_invalid_metadata_without_changing_records_or_history()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info"); AddMetadataSchemaNamespace(setup);
        await SeedInformationGrantAsync(db, ["demo.info"], [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var type = types.Define(new(Application, MetadataTypeId, RankSchema));
        var service = InformationService(db, setup, new InformationStore(db, schemas, types));
        await service.WriteSourceAsync(ApplicationHost(setup, "invalid-source", InteractionExecutionProfile.Atomic),
            new(new("source.schema", "demo.info", "Schema", MetadataSchema: new(type.QualifiedId, type.Version, type.SchemaHash)),
                0, "demo.info.schema"));

        var rejected = await service.WriteRecordAsync(ApplicationHost(setup, "invalid-record", InteractionExecutionProfile.Atomic),
            new(new("record.invalid", "source.schema", "Invalid", "Preserve", "{\"extra\":true}"), 0));

        Assert.Equal(InteractionInvocationResultTag.Failed, rejected.Tag);
        Assert.Equal("INFORMATION_RECORD_METADATA_INVALID", rejected.Code);
        Assert.Empty(await db.Set<InformationRecord>().ToArrayAsync());
        Assert.Empty(await db.Set<InformationContentRevisionRecord>().Where(value => value.Kind == "record").ToArrayAsync());
        Assert.Single(await db.Set<InformationContentRevisionRecord>().Where(value => value.Kind == "source").ToArrayAsync());
    }

    [Fact]
    public async Task Wrong_schema_version_hash_and_incompatible_change_preserve_prior_schema_and_data()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info"); AddMetadataSchemaNamespace(setup);
        await SeedInformationGrantAsync(db, ["demo.info"], [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var versionOne = types.Define(new(Application, MetadataTypeId, RankSchema));
        var service = InformationService(db, setup, new InformationStore(db, schemas, types));
        var wrong = await service.WriteSourceAsync(ApplicationHost(setup, "wrong-schema", InteractionExecutionProfile.Atomic),
            new(new("source.wrong", "demo.info", "Wrong", MetadataSchema:
                new(versionOne.QualifiedId, versionOne.Version, new string('A', 64))), 0, "demo.info.wrong"));
        Assert.Equal(InteractionInvocationResultTag.Failed, wrong.Tag);
        Assert.Equal("INFORMATION_SOURCE_SCHEMA_REFERENCE_NOT_FOUND", wrong.Code);

        var referenceOne = new EcsComponentReference(versionOne.QualifiedId, versionOne.Version, versionOne.SchemaHash);
        await service.WriteSourceAsync(ApplicationHost(setup, "preserve-source", InteractionExecutionProfile.Atomic),
            new(new("source.schema", "demo.info", "Schema", MetadataSchema: referenceOne), 0, "demo.info.schema"));
        await service.WriteRecordAsync(ApplicationHost(setup, "preserve-record", InteractionExecutionProfile.Atomic),
            new(new("record.schema", "source.schema", "Rank", "Preserved", "{\"rank\":1}"), 0));
        var incompatible = types.Define(new(Application, MetadataTypeId,
            """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}"""));
        var rejected = await service.WriteSourceAsync(ApplicationHost(setup, "incompatible-schema", InteractionExecutionProfile.Atomic),
            new(new("source.schema", "demo.info", "Schema", MetadataSchema:
                new(incompatible.QualifiedId, incompatible.Version, incompatible.SchemaHash)), 1, "demo.info.schema"));
        var stale = await service.WriteRecordAsync(ApplicationHost(setup, "stale-record", InteractionExecutionProfile.Atomic),
            new(new("record.schema", "source.schema", "Changed", "Lost", "{\"rank\":2}"), 0));

        Assert.Equal(InteractionInvocationResultTag.Failed, rejected.Tag);
        Assert.Equal("INFORMATION_SOURCE_SCHEMA_INCOMPATIBLE", rejected.Code);
        Assert.Equal(InteractionInvocationResultTag.Failed, stale.Tag);
        Assert.Equal("INFORMATION_REVISION_CONFLICT", stale.Code);
        var source = await db.Set<InformationSource>().AsNoTracking().SingleAsync(value => value.Id == "source.schema");
        var record = await db.Set<InformationRecord>().AsNoTracking().SingleAsync();
        Assert.Equal(1, source.Revision);
        Assert.Equal(referenceOne.SchemaHash, source.MetadataSchemaHash);
        Assert.Equal("Preserved", record.Content);
        Assert.Equal("{\"rank\":1}", record.MetadataJson);
        Assert.Equal(1, record.Revision);
    }

    [Fact]
    public async Task Historical_read_fails_closed_when_the_registered_schema_drifts()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); AddInformationNamespace(setup, "demo.info"); AddMetadataSchemaNamespace(setup);
        await SeedInformationGrantAsync(db, ["demo.info"], [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var type = types.Define(new(Application, MetadataTypeId, RankSchema));
        var service = InformationService(db, setup, new InformationStore(db, schemas, types));
        await service.WriteSourceAsync(ApplicationHost(setup, "drift-source", InteractionExecutionProfile.Atomic),
            new(new("source.schema", "demo.info", "Schema", MetadataSchema: new(type.QualifiedId, type.Version, type.SchemaHash)),
                0, "demo.info.schema"));
        await service.WriteRecordAsync(ApplicationHost(setup, "drift-record", InteractionExecutionProfile.Atomic),
            new(new("record.schema", "source.schema", "Rank", "Preserved", "{\"rank\":1}"), 0));
        var storedType = await db.Set<ComponentTypeVersionRecord>().SingleAsync(value =>
            value.QualifiedId == type.QualifiedId && value.Version == type.Version);
        storedType.SchemaJson = "{\"type\":\"object\"}";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rejectedWrite = await service.WriteRecordAsync(ApplicationHost(setup, "drift-write", InteractionExecutionProfile.Atomic),
            new(new("record.after-drift", "source.schema", "After", "Rejected", "{\"rank\":2}"), 0));

        var read = await service.ReadRecordRevisionAsync(ApplicationHost(setup, "drift-read", grantReference: "grant@1"),
            new("record.schema", 1));

        Assert.Equal(InteractionInvocationResultTag.Failed, rejectedWrite.Tag);
        Assert.Equal("INFORMATION_SOURCE_SCHEMA_REFERENCE_DRIFT", rejectedWrite.Code);
        Assert.Equal("unavailable", read.Status);
        Assert.Equal("INFORMATION_SOURCE_SCHEMA_REFERENCE_DRIFT", read.ErrorCode);
        Assert.Equal("Preserved", (await db.Set<InformationRecord>().AsNoTracking().SingleAsync()).Content);
    }

    private static void AddMetadataSchemaNamespace(SetupState setup) => setup.Namespaces.Register(
        new CatalogNamespaceRegistration("demo.schema", "human-domain-label", "Information schema fixture namespace.",
            [CatalogNamespaceKinds.ComponentType], ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed schema fixture."));
}
