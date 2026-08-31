using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationExecution.Tests;

/// <summary>
/// Runs development-only adoption probes from declarative files. This fixture intentionally knows
/// no ruleset identifiers, data fields, formulas, or outcomes; it proves only generic projection
/// materialization and impact behavior.
/// </summary>
public sealed class ApplicationAdoptionProbeTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Declarative_operation_view_probes_materialize_through_exact_dependency_graphs()
    {
        var probes = ProbePaths().ToArray();
        Assert.NotEmpty(probes);

        foreach (var paths in probes)
        {
            var manifestJson = File.ReadAllText(paths.Manifest);
            var schemas = new BoundedJsonSchemaValidator();
            var compiled = schemas.Compile(File.ReadAllText(paths.Schema));
            Assert.True(compiled.IsAccepted,
                paths.Schema + ": " + string.Join("; ", compiled.Diagnostics.Select(diagnostic => diagnostic.Code)));
            var manifestValidation = schemas.Validate(compiled.ProfileId, compiled.NormalizedSchema, manifestJson);
            Assert.True(manifestValidation.Status == SchemaValueStatus.Valid,
                paths.Manifest + ": " + string.Join("; ", manifestValidation.Diagnostics.Select(diagnostic =>
                    diagnostic.Code + " " + diagnostic.Pointer + " " + diagnostic.Message)));
            var manifestWithUnexpectedProperty = JsonNode.Parse(manifestJson)!.AsObject();
            manifestWithUnexpectedProperty["unexpected"] = true;
            Assert.Equal(SchemaValueStatus.Invalid,
                schemas.Validate(compiled.ProfileId, compiled.NormalizedSchema,
                    manifestWithUnexpectedProperty.ToJsonString()).Status);
            var manifest = JsonSerializer.Deserialize<ProbeManifest>(manifestJson, Json);
            Assert.NotNull(manifest);
            ValidateManifest(manifest!);

            await using var db = _fixture.CreateContext();
            var application = ApplicationIdentifier.Parse(manifest.Fixture.ApplicationId);
            var applications = new SqliteApplicationRegistry(db);
            var revision = applications.Register(new(application, manifest.Fixture.ApplicationName, "", []));
            var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
            stateSpaces.Create(new(manifest.Fixture.StateSpaceId, revision, new string('A', 64)));
            var types = new SqliteComponentTypeRegistry(db, schemas);
            var store = new SqliteEntityComponentStore(db, types, schemas);
            await store.CreateEntityAsync(manifest.Fixture.StateSpaceId, manifest.Fixture.Subject.Id,
                manifest.Fixture.Subject.Name);

            var registeredComponents = manifest.Fixture.Components.ToDictionary(component => component.Id,
                component => types.Define(new(application, component.QualifiedTypeId, component.Schema.GetRawText())),
                StringComparer.Ordinal);
            var componentReferences = registeredComponents.ToDictionary(pair => pair.Key,
                pair => new EcsComponentReference(pair.Value.QualifiedId, pair.Value.Version, pair.Value.SchemaHash),
                StringComparer.Ordinal);
            var written = new Dictionary<string, EcsComponentView>(StringComparer.Ordinal);
            foreach (var component in manifest.Fixture.Components)
                written[component.Id] = await store.AddComponentAsync(new(manifest.Fixture.StateSpaceId,
                    manifest.Fixture.Subject.Id, componentReferences[component.Id], component.Data.GetRawText(), 0));
            var unrelated = types.Define(new(application, application.Value + ".unrelated", "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}}}"));
            await store.AddComponentAsync(new(manifest.Fixture.StateSpaceId, manifest.Fixture.Subject.Id,
                new(unrelated.QualifiedId, unrelated.Version, unrelated.SchemaHash), "{\"value\":99}", 0));

            var registry = new SqliteProjectionDefinitionRegistry(db, types, schemas);
            var definitions = new Dictionary<string, RegisteredProjectionDefinition>(StringComparer.Ordinal);
            foreach (var projection in manifest.Projections)
            {
                var request = new ProjectionDefinitionRequest(application, projection.Id,
                    projection.OutputSchema.GetRawText(),
                    projection.ComponentInputs.Select(input => new ProjectionComponentInput(input.InputId,
                        input.EntityRole, componentReferences[input.ComponentId])).ToArray(),
                    projection.DependencyInputs.Select(input => new ProjectionDependencyInput(input.InputId,
                        definitions[input.ProjectionId].Reference, input.RoleBindings)).ToArray(),
                    projection.Mappings.Select(mapping => new StructuralProjectionMapping(mapping.InputId,
                        mapping.SourcePointer, mapping.TargetPointer)).ToArray());
                definitions.Add(projection.Id, registry.Define(request));
            }

            var target = definitions[manifest.Materialization.ProjectionId];
            var materializer = new ProjectionMaterializer(registry, store, stateSpaces, schemas);
            var before = await store.GetComponentAsync(manifest.Fixture.StateSpaceId, manifest.Fixture.Subject.Id,
                componentReferences[manifest.Expected.ComponentImpact.ComponentId].QualifiedTypeId);
            var definitionCount = db.Set<ProjectionDefinitionVersionRecord>().Count();
            var first = await materializer.MaterializeAsync(new(manifest.Fixture.StateSpaceId, target.Reference,
                manifest.Materialization.RoleEntityIds));
            var second = await materializer.MaterializeAsync(new(manifest.Fixture.StateSpaceId, target.Reference,
                manifest.Materialization.RoleEntityIds));

            AssertJsonEqual(manifest.Expected.Output.GetRawText(), first.OutputJson);
            Assert.Equal(first.OutputJson, second.OutputJson);
            Assert.DoesNotContain("unrelated", first.OutputJson, StringComparison.Ordinal);
            Assert.Equal(target.Reference, first.Projection);
            var expectedReference = componentReferences[manifest.Expected.ComponentImpact.ComponentId];
            Assert.Equal(new ProjectionSourceRevision(manifest.Fixture.Subject.Id, expectedReference,
                manifest.Expected.SourceRevision), Assert.Single(first.SourceRevisions));
            Assert.Equal(definitionCount, db.Set<ProjectionDefinitionVersionRecord>().Count());
            var after = await store.GetComponentAsync(manifest.Fixture.StateSpaceId, manifest.Fixture.Subject.Id,
                expectedReference.QualifiedTypeId);
            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.Equal(before!.Revision, after!.Revision);
            Assert.Equal(before.ValueJson, after.ValueJson);

            var graph = registry.GetImpactGraph(application);
            var parent = manifest.Projections.Single(projection => projection.Id != target.QualifiedId);
            Assert.Equal([parent.Id + "@" + definitions[parent.Id].Version],
                graph.Forward[target.QualifiedId + "@" + target.Version]);
            Assert.Equal([target.QualifiedId + "@" + target.Version],
                graph.Reverse[parent.Id + "@" + definitions[parent.Id].Version]);
            var repeatedGraph = registry.GetImpactGraph(application);
            Assert.Equal(GraphEntries(graph.Forward), GraphEntries(repeatedGraph.Forward));
            Assert.Equal(GraphEntries(graph.Reverse), GraphEntries(repeatedGraph.Reverse));
            var impact = new ProjectionImpactService(applications, new SqliteProjectionImpactSnapshotReader(db)).Analyze(
                application, $"component:{expectedReference.QualifiedTypeId}@{expectedReference.TypeVersion}#{manifest.Expected.ComponentImpact.Pointer}");
            Assert.Equal(manifest.Expected.ComponentImpact.DependentProjectionIds,
                impact.Dependents.Select(value => value.Node.QualifiedId).ToArray());
            Assert.Equal([1, 2], impact.Dependents.Select(value => value.Depth).ToArray());

            await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(new(
                manifest.Fixture.StateSpaceId, target.Reference, new Dictionary<string, string>())));
            await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(new(
                manifest.Fixture.StateSpaceId, target.Reference, new Dictionary<string, string>
                {
                    ["subject"] = manifest.Fixture.Subject.Id,
                    ["extra"] = manifest.Fixture.Subject.Id
                })));
            await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(new(
                manifest.Fixture.StateSpaceId, target.Reference, new Dictionary<string, string> { ["subject"] = "missing" })));
            await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(new(
                manifest.Fixture.StateSpaceId, target.Reference with { ContentHash = new string('B', 64) },
                manifest.Materialization.RoleEntityIds)));

            var other = ApplicationIdentifier.Parse(application.Value + "-other");
            var otherRevision = applications.Register(new(other, "Other", "", []));
            stateSpaces.Create(new(manifest.Fixture.StateSpaceId + "-other", otherRevision, new string('B', 64)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(new(
                manifest.Fixture.StateSpaceId + "-other", target.Reference, manifest.Materialization.RoleEntityIds)));

            var originalCount = db.Set<ProjectionDefinitionVersionRecord>().Count();
            var componentInput = manifest.Projections.First(projection => projection.ComponentInputs.Count > 0).ComponentInputs.Single();
            Assert.Throws<ArgumentException>(() => registry.Define(new ProjectionDefinitionRequest(application,
                application.Value + ".invalid-source", "{\"type\":\"object\"}",
                [new(componentInput.InputId, componentInput.EntityRole, componentReferences[componentInput.ComponentId])], [],
                [new(componentInput.InputId, "/missing", "/value")])));
            Assert.Throws<ArgumentException>(() => registry.Define(new ProjectionDefinitionRequest(application,
                application.Value + ".unknown-dependency", "{}", [],
                [new("unknown", new ProjectionReference(application.Value + ".missing", 1, new string('A', 64)),
                    new Dictionary<string, string> { ["subject"] = "subject" })], [new("unknown", "", "")] )));
            Assert.Equal(originalCount, db.Set<ProjectionDefinitionVersionRecord>().Count());

            var invalidOutput = registry.Define(new ProjectionDefinitionRequest(application,
                application.Value + ".invalid-output", "{\"type\":\"string\"}",
                [new(componentInput.InputId, componentInput.EntityRole, componentReferences[componentInput.ComponentId])], [],
                [new(componentInput.InputId, "", "")]));
            await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(new(
                manifest.Fixture.StateSpaceId, invalidOutput.Reference, manifest.Materialization.RoleEntityIds)));

            var type = registeredComponents[manifest.Expected.ComponentImpact.ComponentId];
            var legacyType = new ComponentTypeVersion(application, type.QualifiedId, type.Version, type.SchemaHash);
            var firstCycle = application.Value + ".cycle-one";
            var secondCycle = application.Value + ".cycle-two";
            Assert.Throws<ArgumentException>(() => ProjectionValidator.Validate([
                new(application, firstCycle, 1, [new("subject", legacyType, "")], [new(secondCycle, 1)], [new("subject", "", "")]),
                new(application, secondCycle, 1, [new("subject", legacyType, "")], [new(firstCycle, 1)], [new("subject", "", "")])
            ]));
        }
    }

    [Fact]
    public async Task Declarative_wrapper_probes_run_only_closed_seeded_views()
    {
        var probes = WrapperProbePaths().ToArray();
        Assert.NotEmpty(probes);

        foreach (var paths in probes)
        {
            var manifestJson = File.ReadAllText(paths.Manifest);
            var schemas = new BoundedJsonSchemaValidator();
            var compiled = schemas.Compile(File.ReadAllText(paths.Schema));
            Assert.True(compiled.IsAccepted,
                paths.Schema + ": " + string.Join("; ", compiled.Diagnostics.Select(diagnostic => diagnostic.Code)));
            var manifestValidation = schemas.Validate(compiled.ProfileId, compiled.NormalizedSchema, manifestJson);
            Assert.True(manifestValidation.Status == SchemaValueStatus.Valid,
                paths.Manifest + ": " + string.Join("; ", manifestValidation.Diagnostics.Select(diagnostic =>
                    diagnostic.Code + " " + diagnostic.Pointer + " " + diagnostic.Message)));
            var manifestWithUnexpectedProperty = JsonNode.Parse(manifestJson)!.AsObject();
            manifestWithUnexpectedProperty["unexpected"] = true;
            Assert.Equal(SchemaValueStatus.Invalid,
                schemas.Validate(compiled.ProfileId, compiled.NormalizedSchema,
                    manifestWithUnexpectedProperty.ToJsonString()).Status);

            var manifest = JsonSerializer.Deserialize<WrapperProbeManifest>(manifestJson, Json);
            Assert.NotNull(manifest);
            ValidateWrapperManifest(manifest!, paths);
            var source = File.ReadAllText(Path.Combine(Path.GetDirectoryName(paths.Manifest)!, manifest!.SourcePath));
            var resultSchema = schemas.Compile(File.ReadAllText(Path.Combine(Path.GetDirectoryName(paths.Manifest)!,
                manifest.ResultSchemaPath)));
            Assert.True(resultSchema.IsAccepted);
            using var operationManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                Path.GetDirectoryName(paths.Manifest)!, manifest.OperationViewManifestPath)));
            var operationView = operationManifest.RootElement.GetProperty("expected").GetProperty("output").GetRawText();
            var engine = new JintMechanicEngine();

            foreach (var vector in manifest.Vectors)
            {
                var view = vector.View.ValueKind == JsonValueKind.Undefined ? operationView : vector.View.GetRawText();
                var first = await engine.RunAsync(source, WrapperProjection(manifest, view, vector.Input.GetRawText(), vector.Seed),
                    ExecutionLimits.Default);
                var second = await engine.RunAsync(source, WrapperProjection(manifest, view, vector.Input.GetRawText(), vector.Seed),
                    ExecutionLimits.Default);
                Assert.True(first.Ok, vector.Name + ": " + first.Error);
                Assert.True(second.Ok, vector.Name + ": " + second.Error);
                Assert.Equal(first.Output.Data, second.Output.Data);
                Assert.Equal(vector.Seed, first.Seed);
                Assert.Equal(SchemaValueStatus.Valid,
                    schemas.Validate(resultSchema.ProfileId, resultSchema.NormalizedSchema, first.Output.Data).Status);
                AssertJsonEqual(vector.Expected.GetRawText(), first.Output.Data);
                Assert.Empty(first.Output.Effects);
                Assert.Empty(first.Output.Events);
                Assert.Empty(first.Output.Notifications);
                Assert.Single(first.Log);
            }

            foreach (var invalid in manifest.InvalidInputs)
            {
                var failed = await engine.RunAsync(source, WrapperProjection(manifest, operationView,
                    invalid.Value.GetRawText(), 7), ExecutionLimits.Default);
                Assert.False(failed.Ok, invalid.Name);
                Assert.Empty(failed.Log);
            }
            foreach (var invalid in manifest.InvalidViews)
            {
                var failed = await engine.RunAsync(source, WrapperProjection(manifest, invalid.Value.GetRawText(),
                    manifest.Vectors[0].Input.GetRawText(), 7), ExecutionLimits.Default);
                Assert.False(failed.Ok, invalid.Name);
                Assert.Empty(failed.Log);
            }

            var invalidResult = JsonNode.Parse(manifest.Vectors[0].Expected.GetRawText())!.AsObject();
            invalidResult["unexpected"] = true;
            Assert.Equal(SchemaValueStatus.Invalid,
                schemas.Validate(resultSchema.ProfileId, resultSchema.NormalizedSchema, invalidResult.ToJsonString()).Status);
        }
    }

    [Fact]
    public async Task Declarative_parity_probes_match_retained_sources_and_freeze_context()
    {
        var probes = ParityProbePaths().ToArray();
        Assert.NotEmpty(probes);

        foreach (var paths in probes)
        {
            var manifestJson = File.ReadAllText(paths.Manifest);
            var schemas = new BoundedJsonSchemaValidator();
            var compiled = schemas.Compile(File.ReadAllText(paths.Schema));
            Assert.True(compiled.IsAccepted,
                paths.Schema + ": " + string.Join("; ", compiled.Diagnostics.Select(diagnostic => diagnostic.Code)));
            Assert.Equal(SchemaValueStatus.Valid,
                schemas.Validate(compiled.ProfileId, compiled.NormalizedSchema, manifestJson).Status);
            var manifestWithUnexpectedProperty = JsonNode.Parse(manifestJson)!.AsObject();
            manifestWithUnexpectedProperty["unexpected"] = true;
            Assert.Equal(SchemaValueStatus.Invalid,
                schemas.Validate(compiled.ProfileId, compiled.NormalizedSchema,
                    manifestWithUnexpectedProperty.ToJsonString()).Status);

            var manifest = JsonSerializer.Deserialize<ParityProbeManifest>(manifestJson, Json);
            Assert.NotNull(manifest);
            var wrapperPath = ResolveArtifact(paths.Manifest, manifest!.WrapperSourcePath);
            var resultSchemaPath = ResolveArtifact(paths.Manifest, manifest.WrapperResultSchemaPath);
            var archivePath = ResolveArtifact(paths.Manifest, manifest.ArchiveRecordPath);
            var mutationPath = ResolveArtifact(paths.Manifest, manifest.MutationSourcePath);
            var wrapperHash = FileHash(wrapperPath);
            var archiveHash = FileHash(archivePath);
            var mutationHash = FileHash(mutationPath);
            var wrapperSource = File.ReadAllText(wrapperPath);
            using var archiveRecord = JsonDocument.Parse(File.ReadAllText(archivePath));
            var archiveSource = archiveRecord.RootElement.GetProperty(manifest.ArchiveSourceProperty).GetString();
            Assert.False(string.IsNullOrWhiteSpace(archiveSource));
            var resultSchema = schemas.Compile(File.ReadAllText(resultSchemaPath));
            Assert.True(resultSchema.IsAccepted);
            var engine = new JintMechanicEngine();
            var candidateResults = new Dictionary<string, MechanicRunResult>(StringComparer.Ordinal);

            foreach (var scenario in manifest.Scenarios)
            {
                var scores = scenario.Scores.GetRawText();
                var view = "{\"scores\":" + scores + "}";
                var input = scenario.Input.GetRawText();
                var candidate = await engine.RunAsync(wrapperSource,
                    Projection(manifest.Roles.Candidate, manifest.Components.Candidate, view, input, scenario.Seed),
                    ExecutionLimits.Default);
                var retained = await engine.RunAsync(archiveSource!,
                    Projection(manifest.Roles.Archive, manifest.Components.Archive, scores, input, scenario.Seed),
                    ExecutionLimits.Default);
                Assert.True(candidate.Ok, scenario.Name + " candidate: " + candidate.Error);
                Assert.True(retained.Ok, scenario.Name + " retained: " + retained.Error);
                Assert.Equal(SchemaValueStatus.Valid,
                    schemas.Validate(resultSchema.ProfileId, resultSchema.NormalizedSchema, candidate.Output.Data).Status);
                foreach (var pointer in manifest.SharedResultPointers)
                    Assert.True(JsonElement.DeepEquals(JsonPointer(candidate.Output.Data, pointer),
                        JsonPointer(retained.Output.Data, pointer)), scenario.Name + " mismatch at " + pointer);
                Assert.True(JsonElement.DeepEquals(JsonPointer(candidate.Output.Data, manifest.CandidateScorePointer),
                    JsonPointer(scores, scenario.SelectedScorePointer)), scenario.Name + " selected score mismatch");
                AssertNoProposals(candidate);
                AssertNoProposals(retained);

                var replay = await engine.RunAsync(wrapperSource,
                    Projection(manifest.Roles.Candidate, manifest.Components.Candidate, view, input, scenario.Seed),
                    ExecutionLimits.Default);
                Assert.True(replay.Ok, scenario.Name + " replay: " + replay.Error);
                Assert.Equal(candidate.Output.Data, replay.Output.Data);
                candidateResults.Add(scenario.Name, candidate);
            }

            var changedScenario = manifest.Scenarios.Single(value => value.Name == manifest.SeedChange.Scenario);
            var changedScores = changedScenario.Scores.GetRawText();
            var changedView = "{\"scores\":" + changedScores + "}";
            var changedInput = changedScenario.Input.GetRawText();
            var changedCandidate = await engine.RunAsync(wrapperSource,
                Projection(manifest.Roles.Candidate, manifest.Components.Candidate, changedView, changedInput,
                    manifest.SeedChange.AlternateSeed), ExecutionLimits.Default);
            var changedRetained = await engine.RunAsync(archiveSource!,
                Projection(manifest.Roles.Archive, manifest.Components.Archive, changedScores, changedInput,
                    manifest.SeedChange.AlternateSeed), ExecutionLimits.Default);
            Assert.True(changedCandidate.Ok, changedCandidate.Error);
            Assert.True(changedRetained.Ok, changedRetained.Error);
            Assert.NotEqual(candidateResults[changedScenario.Name].Output.Data, changedCandidate.Output.Data);
            foreach (var pointer in manifest.SharedResultPointers)
                Assert.True(JsonElement.DeepEquals(JsonPointer(changedCandidate.Output.Data, pointer),
                    JsonPointer(changedRetained.Output.Data, pointer)), "changed-seed mismatch at " + pointer);
            AssertNoProposals(changedCandidate);
            AssertNoProposals(changedRetained);

            var mutation = await engine.RunAsync(File.ReadAllText(mutationPath),
                Projection(manifest.Roles.Candidate, manifest.Components.Candidate, changedView, changedInput, 7),
                ExecutionLimits.Default);
            Assert.True(mutation.Ok, mutation.Error);
            using var mutationData = JsonDocument.Parse(mutation.Output.Data);
            Assert.All(mutationData.RootElement.EnumerateObject(), property => Assert.True(property.Value.GetBoolean(), property.Name));
            AssertNoProposals(mutation);

            Assert.Equal(wrapperHash, FileHash(wrapperPath));
            Assert.Equal(archiveHash, FileHash(archivePath));
            Assert.Equal(mutationHash, FileHash(mutationPath));
        }
    }

    private static void ValidateManifest(ProbeManifest manifest)
    {
        Assert.False(string.IsNullOrWhiteSpace(manifest.Format));
        Assert.Equal(manifest.Fixture.Components.Count, manifest.Fixture.Components.Select(component => component.Id)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(manifest.Projections.Count, manifest.Projections.Select(projection => projection.Id)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(manifest.Projections, projection => projection.Id == manifest.Materialization.ProjectionId);
        Assert.Equal(manifest.Fixture.Subject.Id, manifest.Materialization.RoleEntityIds["subject"]);
    }

    private static void ValidateWrapperManifest(WrapperProbeManifest manifest, ProbePathPair paths)
    {
        Assert.False(string.IsNullOrWhiteSpace(manifest.Format));
        Assert.NotEmpty(manifest.Vectors);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(paths.Manifest)!, manifest.SourcePath)));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(paths.Manifest)!, manifest.ResultSchemaPath)));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(paths.Manifest)!, manifest.OperationViewManifestPath)));
    }

    private static MechanicProjection WrapperProjection(WrapperProbeManifest manifest, string view, string input, long seed) =>
        new()
        {
            Roles = new Dictionary<string, EntityProjection>
            {
                [manifest.Role] = new("subject", "Subject", new Dictionary<string, string>
                {
                    [manifest.ViewComponent] = view
                })
            },
            Input = input,
            Seed = seed
        };

    private static MechanicProjection Projection(string role, string component, string value, string input, long seed) =>
        new()
        {
            Roles = new Dictionary<string, EntityProjection>
            {
                [role] = new("subject", "Subject", new Dictionary<string, string> { [component] = value })
            },
            Input = input,
            Seed = seed
        };

    private static void AssertNoProposals(MechanicRunResult result)
    {
        Assert.Empty(result.Output.Effects);
        Assert.Empty(result.Output.Events);
        Assert.Empty(result.Output.Notifications);
    }

    private static IEnumerable<ProbePathPair> ProbePaths()
    {
        var rulesetRoot = Path.Combine(RepositoryRoot(), "ruleset");
        return Directory.EnumerateFiles(rulesetRoot, "*.probe.json", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".wrapper.probe.json", StringComparison.Ordinal))
            .Where(path => !path.EndsWith(".parity.probe.json", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(manifest => new ProbePathPair(manifest, Path.ChangeExtension(manifest, ".schema.json")))
            .Where(paths => File.Exists(paths.Schema));
    }

    private static IEnumerable<ProbePathPair> WrapperProbePaths()
    {
        var rulesetRoot = Path.Combine(RepositoryRoot(), "ruleset");
        return Directory.EnumerateFiles(rulesetRoot, "*.wrapper.probe.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(manifest => new ProbePathPair(manifest, Path.ChangeExtension(manifest, ".schema.json")))
            .Where(paths => File.Exists(paths.Schema));
    }

    private static IEnumerable<ProbePathPair> ParityProbePaths()
    {
        var rulesetRoot = Path.Combine(RepositoryRoot(), "ruleset");
        return Directory.EnumerateFiles(rulesetRoot, "*.parity.probe.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(manifest => new ProbePathPair(manifest, Path.ChangeExtension(manifest, ".schema.json")))
            .Where(paths => File.Exists(paths.Schema));
    }

    private static string ResolveArtifact(string manifestPath, string relativePath)
    {
        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, relativePath));
        Assert.StartsWith(RepositoryRoot() + Path.DirectorySeparatorChar, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(resolved), resolved);
        return resolved;
    }

    private static string FileHash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static JsonElement JsonPointer(string json, string pointer)
    {
        using var document = JsonDocument.Parse(json);
        var current = document.RootElement;
        foreach (var raw in pointer.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            current = current.ValueKind == JsonValueKind.Array
                ? current[int.Parse(segment, System.Globalization.CultureInfo.InvariantCulture)]
                : current.GetProperty(segment);
        }
        return current.Clone();
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }

    private static void AssertJsonEqual(string expected, string actual)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);
        Assert.True(JsonElement.DeepEquals(expectedDocument.RootElement, actualDocument.RootElement));
    }

    private static IReadOnlyList<string> GraphEntries(IReadOnlyDictionary<string, IReadOnlyList<string>> graph) =>
        graph.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => entry.Key + ":" + string.Join("|", entry.Value)).ToArray();

    public void Dispose() => _fixture.Dispose();

    private sealed record ProbePathPair(string Manifest, string Schema);
    private sealed record ProbeManifest(string Format, ProbeFixture Fixture, IReadOnlyList<ProbeProjection> Projections,
        ProbeMaterialization Materialization, ProbeExpected Expected);
    private sealed record ProbeFixture(string ApplicationId, string ApplicationName, string StateSpaceId,
        ProbeSubject Subject, IReadOnlyList<ProbeComponent> Components);
    private sealed record ProbeSubject(string Id, string Name);
    private sealed record ProbeComponent(string Id, string QualifiedTypeId, JsonElement Schema, JsonElement Data);
    private sealed record ProbeProjection(string Id, JsonElement OutputSchema, IReadOnlyList<ProbeComponentInput> ComponentInputs,
        IReadOnlyList<ProbeDependencyInput> DependencyInputs, IReadOnlyList<ProbeMapping> Mappings);
    private sealed record ProbeComponentInput(string InputId, string EntityRole, string ComponentId);
    private sealed record ProbeDependencyInput(string InputId, string ProjectionId, Dictionary<string, string> RoleBindings);
    private sealed record ProbeMapping(string InputId, string SourcePointer, string TargetPointer);
    private sealed record ProbeMaterialization(string ProjectionId, Dictionary<string, string> RoleEntityIds);
    private sealed record ProbeExpected(JsonElement Output, int SourceRevision, ProbeComponentImpact ComponentImpact);
    private sealed record ProbeComponentImpact(string ComponentId, string Pointer, IReadOnlyList<string> DependentProjectionIds);
    private sealed record WrapperProbeManifest(string Format, string SourcePath, string ResultSchemaPath,
        string OperationViewManifestPath, string Role, string ViewComponent, IReadOnlyList<WrapperProbeVector> Vectors,
        IReadOnlyList<WrapperProbeInvalid> InvalidInputs, IReadOnlyList<WrapperProbeInvalid> InvalidViews);
    private sealed record WrapperProbeVector(string Name, JsonElement Input, long Seed, JsonElement Expected, JsonElement View);
    private sealed record WrapperProbeInvalid(string Name, JsonElement Value);
    private sealed record ParityProbeManifest(string Format, string WrapperSourcePath,
        string WrapperResultSchemaPath, string ArchiveRecordPath, string ArchiveSourceProperty,
        string MutationSourcePath, ParityNames Roles, ParityNames Components,
        IReadOnlyList<string> SharedResultPointers, string CandidateScorePointer,
        IReadOnlyList<ParityScenario> Scenarios, ParitySeedChange SeedChange);
    private sealed record ParityNames(string Candidate, string Archive);
    private sealed record ParityScenario(string Name, JsonElement Scores, JsonElement Input, long Seed,
        string SelectedScorePointer);
    private sealed record ParitySeedChange(string Scenario, long AlternateSeed);
}
