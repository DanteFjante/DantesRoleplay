using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tests;

public sealed class CatalogIdentityLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"catalog-identity-{Guid.NewGuid():n}");

    [Fact]
    public async Task Reviewed_identity_renames_move_records_and_exact_references_as_one_valid_catalog()
    {
        Directory.CreateDirectory(_root);
        await WriteAsync(CatalogLayout.Namespace("sample"), new CatalogNamespaceFile(
            "sample", "sample", "Fixture source namespace.",
            [CatalogNamespaceKinds.ComponentDefinition, CatalogNamespaceKinds.Entity], [], true,
            CatalogNamespaceReviewStatuses.Reviewed, "Reviewed for the lifecycle test.").ToJson());
        await WriteAsync(CatalogLayout.Component("sample.value"), new ComponentDefinitionFile(
            "sample.value", "Value", "The identity is sample.value.", "").ToJson());
        await WriteAsync(CatalogLayout.Entity("sample.subject"), new EntityFile(
            "sample.subject", "Subject", null, "", [new("sample.value", "{\"amount\":1}")]).ToJson());

        var plan = new CatalogIdentityMigrationPlan(
            [new CatalogNamespaceFile(
                "correct", "correct", "Corrected fixture namespace.",
                [CatalogNamespaceKinds.ComponentDefinition, CatalogNamespaceKinds.Entity], [], true,
                CatalogNamespaceReviewStatuses.Reviewed, "Reviewed for the lifecycle test.")],
            [
                new(CatalogRecordKind.ComponentDefinition, "sample.value", "correct.value"),
                new(CatalogRecordKind.Entity, "sample.subject", "correct.subject")
            ]);

        var result = await CatalogIdentityLifecycleMigrator.MigrateAsync(_root, plan, apply: true);
        var contents = await CatalogReader.ReadAsync(_root);

        Assert.True(result.Applied);
        Assert.Equal(2, result.RenamedRecords);
        Assert.Equal("correct.value", Assert.Single(contents.Components).Id);
        Assert.Equal("The identity is correct.value.", Assert.Single(contents.Components).Description);
        var entity = Assert.Single(contents.Entities);
        Assert.Equal("correct.subject", entity.Id);
        Assert.Equal("correct.value", Assert.Single(entity.Components).DefinitionId);
        Assert.False(File.Exists(CatalogLayout.ToFileSystemPath(_root, CatalogLayout.Component("sample.value"))));
        Assert.False(File.Exists(CatalogLayout.ToFileSystemPath(_root, CatalogLayout.Entity("sample.subject"))));
        var validation = await CatalogValidator.ValidateAsync(_root);
        Assert.True(validation.IsValid);
        Assert.Equal(0, validation.Warnings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private async Task WriteAsync(string relativePath, string content)
    {
        var path = CatalogLayout.ToFileSystemPath(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }
}
