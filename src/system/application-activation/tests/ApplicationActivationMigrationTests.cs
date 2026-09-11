using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.ApplicationActivation.Tests;

public sealed class ApplicationActivationMigrationTests
{
    [Fact]
    public async Task Selected_baseline_upgrades_without_inventing_or_losing_legacy_document_content()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options;
        await using var db = new DantesRoleplayDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync("20260911000100_ApplicationEventSourceContext");

        var app = ApplicationIdentifier.Parse("migration-fixture");
        new SqliteApplicationRegistry(db).Register(new(app, "Migration fixture", "Retained evidence upgrade.", []));
        var bytes = Encoding.UTF8.GetBytes("old exact source bytes");
        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO system_application_activation_document_identity (ApplicationId, LogicalIdentity)
            VALUES ({app.Value}, 'file:content/fixture.txt');
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO system_application_activation_document_evidence
                (IdentityId, EvidenceVersion, SourceId, Trust, Precedence, RelativePath,
                 MediaType, ContentFingerprint, Length, IsText)
            SELECT Id, 1, 'fixture', 1, 0, 'content/fixture.txt', 'text/plain', {fingerprint}, {bytes.Length}, 1
            FROM system_application_activation_document_identity WHERE ApplicationId = {app.Value};
            """);

        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var evidence = await db.Set<ApplicationActivationDocumentEvidenceRecord>().SingleAsync();
        Assert.Equal(fingerprint, evidence.ContentFingerprint);
        Assert.Equal(bytes.Length, evidence.Length);
        Assert.Null(evidence.RetainedBytes);

        // A migration cannot recreate old files. Retention is populated only by a verified activation.
        // Prove the new storage can retain exact binary data without changing its existing identity.
        evidence.RetainedBytes = bytes;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var persisted = await db.Set<ApplicationActivationDocumentEvidenceRecord>().SingleAsync();
        Assert.Equal(bytes, persisted.RetainedBytes);
        Assert.Equal(fingerprint, persisted.ContentFingerprint);
        Assert.Equal(1, persisted.EvidenceVersion);
    }
}
