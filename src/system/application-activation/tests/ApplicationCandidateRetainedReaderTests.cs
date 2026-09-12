using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
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

    [Fact]
    public async Task Streaming_metadata_fingerprint_matches_the_original_v1_canonical_formula()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        var metadata = (await setup.Reader.ReadMetadataAsync(Application, CandidateId, 1))!;
        var oldFormula = InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-revision/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                metadata.RevisionRow.ApplicationId, metadata.RevisionRow.CandidateId, metadata.RevisionRow.Revision,
                metadata.RevisionRow.ApplicationRevision, applicationFingerprint = metadata.ApplicationRevision.Fingerprint,
                metadata.RevisionRow.ExpectedActiveFingerprint, metadata.RevisionRow.Origin,
                metadata.RevisionRow.SynchronizationEvidenceReference, metadata.RevisionRow.NewImplementationReason,
                metadata.RevisionRow.AuthorGrantReference, metadata.RevisionRow.SourceOperationId,
                metadata.RevisionRow.CanonicalCommandFingerprint, documents = metadata.Documents.Select(value => new
                {
                    value.LogicalIdentity, value.SourceId, value.Trust, value.Precedence, value.RelativePath,
                    value.MediaType, value.ContentFingerprint, value.Length, value.IsText
                })
            })));

        Assert.Equal(oldFormula, ApplicationCandidateRetainedReader.MetadataFingerprint(
            metadata.RevisionRow, metadata.ApplicationRevision, metadata.Documents));
    }

    [Fact]
    public async Task Large_metadata_reads_selected_bytes_without_touching_unrelated_corruption_while_full_read_rejects_it()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var setup = await SeedLargeAsync(db);
        var metadata = (await setup.Reader.ReadMetadataAsync(Application, CandidateId, 1))!;
        Assert.Equal(129, metadata.Documents.Count);
        var selectedPath = metadata.Documents[0].RelativePath;
        var unrelated = metadata.Documents[^1];
        var unrelatedEvidence = await db.Set<ApplicationActivationDocumentEvidenceRecord>().SingleAsync(value =>
            value.ContentFingerprint == unrelated.ContentFingerprint);
        unrelatedEvidence.RetainedBytes = Encoding.UTF8.GetBytes(new string('z', checked((int)unrelated.Length)));
        await db.SaveChangesAsync();

        var selected = await setup.Reader.ReadSelectedAsync(metadata, [selectedPath]);
        Assert.Single(selected);
        await Assert.ThrowsAsync<ApplicationActivationException>(() => setup.Reader.ReadAsync(Application, CandidateId, 1));

        var selectedEvidence = await db.Set<ApplicationActivationDocumentEvidenceRecord>().SingleAsync(value =>
            value.ContentFingerprint == metadata.Documents[0].ContentFingerprint);
        selectedEvidence.RetainedBytes = Encoding.UTF8.GetBytes(new string('q', checked((int)metadata.Documents[0].Length)));
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ApplicationActivationException>(() => setup.Reader.ReadSelectedAsync(metadata, [selectedPath]));
        Assert.Equal("ACTIVATION_EVIDENCE_CORRUPT", error.Code);
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

    private static async Task<(ApplicationCandidateRetainedReader Reader, SqliteApplicationRegistry Applications)> SeedLargeAsync(
        DantesRoleplayDbContext db)
    {
        var applications = new SqliteApplicationRegistry(db);
        applications.Register(new(Application, "Candidate reader", "Retained candidate fixture.", []));
        var applicationRevision = applications.Get(Application, 1)!;
        var documents = Enumerable.Range(0, 129).Select(index =>
        {
            var path = $"content/{index:D3}/{new string('x', 520)}.json";
            var bytes = Encoding.UTF8.GetBytes($"{{\"index\":{index}}}");
            return (Document: new ActivatedApplicationDocument("file:" + path, "catalog", SourceTrust.Trusted, 0,
                path, "application/json", Convert.ToHexString(SHA256.HashData(bytes)), bytes.LongLength, true), Bytes: bytes);
        }).ToArray();
        var links = await new ApplicationRetainedDocumentStore(db).RetainAsync(Application,
            documents.Select(value => value.Document).ToArray(), documents.ToDictionary(value => value.Document.LogicalIdentity, value => value.Bytes), default);
        db.Add(new Operation { Id = "candidate-source-operation", Timestamp = DateTime.UtcNow, Tool = "test" });
        var row = new ApplicationCandidateRevisionRecord { ApplicationId = Application.Value, CandidateId = CandidateId, Revision = 1,
            ApplicationRevision = applicationRevision.Revision, ContentFingerprint = new string('0', 64), Origin = "runtime",
            NewImplementationReason = "Large retained candidate.", AuthorGrantReference = "grant@1",
            SourceOperationId = "candidate-source-operation", CanonicalCommandFingerprint = new string('A', 64) };
        row.ContentFingerprint = ApplicationCandidateRetainedReader.MetadataFingerprint(row, applicationRevision,
            documents.Select(value => value.Document).ToArray());
        db.Add(row);
        foreach (var link in links) db.Add(new ApplicationCandidateDocumentRecord { ApplicationId = Application.Value,
            CandidateId = CandidateId, Revision = 1, Ordinal = link.Ordinal, IdentityId = link.IdentityId, EvidenceVersion = link.EvidenceVersion });
        await db.SaveChangesAsync();
        return (new(db, applications), applications);
    }
}
