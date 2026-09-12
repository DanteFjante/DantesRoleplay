using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Fixture = DantesRoleplay.Tests.WebPagePublicationSelectionTests.Fixture;

namespace DantesRoleplay.Tests;

public sealed class WebPagePublicationStagingTests
{
    [Fact]
    public async Task Caller_staging_is_invisible_to_a_second_connection_and_rollback_preserves_retained_drafts()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Publication.CompareExchangeContentReferenceAsync(
            await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 1));
        var draft = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2);
        await using var peerData = new DantesRoleplayDbContext(new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite(fixture.Data.Database.GetConnectionString()!).Options);
        var schemas = new BoundedJsonSchemaValidator();
        var peerEntities = new SqliteEntityComponentStore(peerData, new SqliteComponentTypeRegistry(peerData, schemas), schemas);

        await using (var transaction = await fixture.Transactions.BeginAsync())
        {
            var staged = await fixture.Publication.StageContentReferenceAsync(draft, transaction);
            Assert.Equal(draft.PageComponent.Revision + 1, staged.Revision);
            Assert.True(fixture.Transactions.OwnsCurrent(transaction));
            Assert.Equal(draft.PageComponent.ValueJson,
                (await peerEntities.GetComponentAsync(Fixture.PublicationSpace, Fixture.EntityId, WebPageComponentTypes.Page))!.ValueJson);
            await transaction.RollbackAsync();
            Assert.False(fixture.Transactions.OwnsCurrent(transaction));
        }
        Assert.Equal(1, (await fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId)).Content.Revision);
        Assert.Equal(new byte[] { 2 }, (await fixture.Content.GetRevisionAssetAsync(Fixture.ContentId, 2, "assets/icon.bin"))!.Content);

        await using (var transaction = await fixture.Transactions.BeginAsync())
        {
            await fixture.Publication.StageContentReferenceAsync(draft, transaction);
            await fixture.Constraints.ValidateStateSpaceAsync(Fixture.PublicationSpace);
            await transaction.CommitAsync();
        }
        Assert.Equal(2, (await fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId)).Content.Revision);
        Assert.Equal(1, (await fixture.Content.GetSummaryAsync(Fixture.ContentId))!.ActiveRevision);
    }

    [Fact]
    public async Task Wrong_factory_is_rejected_and_stale_selection_does_not_complete_the_callers_transaction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var draft = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2);
        var wrongFactory = new SqliteEcsWriteTransactionFactory(fixture.Data);
        await using (var transaction = await wrongFactory.BeginAsync())
        {
            var wrong = await Assert.ThrowsAsync<WebPageStoreException>(() => fixture.Publication.StageContentReferenceAsync(draft, transaction));
            Assert.Equal("WEB_PUBLICATION_CALLER_TRANSACTION_REQUIRED", wrong.Code);
            Assert.True(wrongFactory.OwnsCurrent(transaction));
            await transaction.RollbackAsync();
        }
        await fixture.Administration.UpdateMetadataAsync(fixture.ApplicationId, Fixture.EntityId,
            new(draft.PageComponent.Revision, "Updated title", "Page", "example", 0, "public"));
        await using (var transaction = await fixture.Transactions.BeginAsync())
        {
            await fixture.Entities.CreateEntityAsync(Fixture.PublicationSpace, "caller-change", "Uncommitted caller change");
            var stale = await Assert.ThrowsAsync<WebPageStoreException>(() => fixture.Publication.StageContentReferenceAsync(draft, transaction));
            Assert.Equal("WEB_PAGE_SELECTION_STALE", stale.Code);
            Assert.True(fixture.Transactions.OwnsCurrent(transaction));
            Assert.NotNull(await fixture.Entities.GetEntityAsync(Fixture.PublicationSpace, "caller-change"));
            await transaction.RollbackAsync();
        }
        Assert.Null(await fixture.Entities.GetEntityAsync(Fixture.PublicationSpace, "caller-change"));
        Assert.NotNull(await fixture.Content.GetRevisionAsync(Fixture.ContentId, 2));
    }

    [Fact]
    public async Task Independent_wrapper_requires_the_real_constraint_validator()
    {
        await using var fixture = await Fixture.CreateAsync();
        var schemas = new BoundedJsonSchemaValidator();
        var withoutValidator = new WebPagePublicationService(fixture.Applications, fixture.Spaces,
            new SqliteComponentTypeRegistry(fixture.Data, schemas), fixture.Entities, new WorldStore(fixture.Data),
            fixture.Content, fixture.Web, fixture.Transactions, new(), NullLogger<WebPagePublicationService>.Instance);
        var draft = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2);
        var missing = await Assert.ThrowsAsync<WebPageStoreException>(() => withoutValidator.CompareExchangeContentReferenceAsync(draft));
        Assert.Equal("WEB_PUBLICATION_CONSTRAINTS_UNAVAILABLE", missing.Code);
        Assert.Null(fixture.Data.Database.CurrentTransaction);
        Assert.Equal(draft.PageComponent.Revision,
            (await fixture.Entities.GetComponentAsync(Fixture.PublicationSpace, Fixture.EntityId, WebPageComponentTypes.Page))!.Revision);
    }

    [Fact]
    public async Task Caller_constraint_failure_rolls_back_all_staged_changes_and_preserves_the_previous_pin()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Publication.CompareExchangeContentReferenceAsync(
            await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 1));
        var draft = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2);
        await using (var transaction = await fixture.Transactions.BeginAsync())
        {
            await fixture.Entities.CreateEntityAsync(Fixture.PublicationSpace, "duplicate-page", "Duplicate page");
            await fixture.Entities.AddComponentAsync(new(Fixture.PublicationSpace, "duplicate-page",
                draft.PageComponent.Type, draft.PageComponent.ValueJson, 0));
            await fixture.Publication.StageContentReferenceAsync(draft, transaction);
            var failure = await Assert.ThrowsAsync<EcsRoleConstraintException>(() =>
                fixture.Constraints.ValidateStateSpaceAsync(Fixture.PublicationSpace));
            Assert.Equal("ROLE_UNIQUENESS_VIOLATION", failure.Code);
            Assert.True(fixture.Transactions.OwnsCurrent(transaction));
            await transaction.RollbackAsync();
        }
        Assert.Null(await fixture.Entities.GetEntityAsync(Fixture.PublicationSpace, "duplicate-page"));
        Assert.Equal(1, (await fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId)).Content.Revision);
        Assert.NotNull(await fixture.Content.GetRevisionAsync(Fixture.ContentId, 2));
    }
}
