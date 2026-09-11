using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using DantesRoleplay.Sources;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation.Tests;

public sealed class ApplicationCandidateRetainedReaderTests
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("candidate-reader");
    private const string CandidateId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Reads_only_the_candidate_linked_retained_document_and_its_pinned_application_revision()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);

        var result = await setup.Reader.ReadAsync(Application, CandidateId, 1);

        Assert.NotNull(result);
        Assert.Equal(1, result.ApplicationRevision.Revision);
        var document = Assert.Single(result.Documents);
        Assert.Equal("file:content/candidate.json", document.Document.LogicalIdentity);
        Assert.Equal(Encoding.UTF8.GetBytes("{\"candidate\":true}"), document.RetainedBytes);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("bytes")]
    [InlineData("length")]
    public async Task Missing_or_corrupt_candidate_bytes_never_fall_back_to_source_content(string state)
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        var evidence = await db.Set<ApplicationActivationDocumentEvidenceRecord>().SingleAsync();
        if (state == "missing") evidence.RetainedBytes = null;
        else if (state == "bytes") evidence.RetainedBytes = Encoding.UTF8.GetBytes("corrupt");
        else evidence.Length = 1;
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            setup.Reader.ReadAsync(Application, CandidateId, 1));

        Assert.Equal(state == "missing" ? "ACTIVATION_EVIDENCE_MISSING" : "ACTIVATION_EVIDENCE_CORRUPT", error.Code);
    }

    [Fact]
    public async Task Cross_application_lookup_does_not_disclose_a_candidate()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        var other = ApplicationIdentifier.Parse("other-candidate-reader");
        setup.Applications.Register(new(other, "Other", "Other application.", []));

        var result = await setup.Reader.ReadAsync(other, CandidateId, 1);

        Assert.Null(result);
    }

    [Fact]
    public async Task Metadata_change_with_a_well_formed_old_hash_is_rejected()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        var candidate = await db.Set<ApplicationCandidateRevisionRecord>().SingleAsync();
        candidate.NewImplementationReason = "Modified after the immutable candidate fingerprint.";
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            setup.Reader.ReadAsync(Application, CandidateId, 1));

        Assert.Equal("APPLICATION_CANDIDATE_FINGERPRINT_MISMATCH", error.Code);
    }

    private static async Task<(ApplicationCandidateRetainedReader Reader, SqliteApplicationRegistry Applications)> SeedAsync(
        DantesRoleplayDbContext db)
    {
        var applications = new SqliteApplicationRegistry(db);
        applications.Register(new(Application, "Candidate reader", "Retained candidate fixture.", []));
        var applicationRevision = applications.Get(Application, 1)!;
        var bytes = Encoding.UTF8.GetBytes("{\"candidate\":true}");
        var document = new ActivatedApplicationDocument("file:content/candidate.json", "catalog", SourceTrust.Trusted,
            0, "content/candidate.json", "application/json", Convert.ToHexString(SHA256.HashData(bytes)),
            bytes.LongLength, true);
        var retained = new ApplicationRetainedDocumentStore(db);
        var link = Assert.Single(await retained.RetainAsync(Application, [document],
            new Dictionary<string, byte[]> { [document.LogicalIdentity] = bytes }, CancellationToken.None));
        db.Add(new Operation { Id = "candidate-source-operation", Timestamp = DateTime.UtcNow, Tool = "test" });
        var row = new ApplicationCandidateRevisionRecord
        {
            ApplicationId = Application.Value,
            CandidateId = ApplicationCandidateRetainedReaderTests.CandidateId,
            Revision = 1,
            ApplicationRevision = applicationRevision.Revision,
            ContentFingerprint = new string('0', 64),
            Origin = "runtime",
            NewImplementationReason = "Test retained candidate.",
            AuthorGrantReference = "grant@1",
            SourceOperationId = "candidate-source-operation",
            CanonicalCommandFingerprint = new string('A', 64)
        };
        var documents = new[] { new ApplicationCandidateDocument(document, bytes) };
        row.ContentFingerprint = ApplicationCandidateRetainedReader.ContentFingerprint(row, applicationRevision, documents);
        db.Add(row);
        db.Add(new ApplicationCandidateDocumentRecord
        {
            ApplicationId = Application.Value,
            CandidateId = row.CandidateId,
            Revision = row.Revision,
            Ordinal = link.Ordinal,
            IdentityId = link.IdentityId,
            EvidenceVersion = link.EvidenceVersion
        });
        await db.SaveChangesAsync();
        return (new(db, applications), applications);
    }
}
