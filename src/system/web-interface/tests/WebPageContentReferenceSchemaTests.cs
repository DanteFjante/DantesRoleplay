using System.Text.Json;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class WebPageContentReferenceSchemaTests
{
    private const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [Theory]
    [InlineData("html")]
    [InlineData("composition-v1")]
    public void Active_content_reference_accepts_legacy_and_pinned_forms(string contentFormat)
    {
        var schema = LoadSchema();

        Assert.Equal(SchemaValueStatus.Valid, Validate(schema, new
        {
            title = "Page",
            navigationLabel = "Page",
            slug = "page",
            order = 0,
            visibility = "public",
            activeContentReference = new { pageId = "page" }
        }));
        Assert.Equal(SchemaValueStatus.Valid, Validate(schema, new
        {
            title = "Page",
            navigationLabel = "Page",
            slug = "page",
            order = 0,
            visibility = "public",
            activeContentReference = new
            {
                pageId = "page", revision = 1, contentFormat,
                contentHash = Hash, assetInventoryFingerprint = Hash
            }
        }));
    }

    [Theory]
    [InlineData("{\"revision\":1}")]
    [InlineData("{\"pageId\":\"page\",\"revision\":1}")]
    [InlineData("{\"contentFormat\":\"html\"}")]
    [InlineData("{\"pageId\":\"page\",\"revision\":1,\"contentFormat\":\"html\",\"contentHash\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"assetInventoryFingerprint\":\"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF\"}")]
    [InlineData("{\"pageId\":\"page\",\"revision\":1,\"contentFormat\":\"html\",\"contentHash\":\"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF\",\"assetInventoryFingerprint\":\"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDE\"}")]
    [InlineData("{\"pageId\":\"page\",\"revision\":0,\"contentFormat\":\"html\",\"contentHash\":\"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF\",\"assetInventoryFingerprint\":\"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF\"}")]
    [InlineData("{\"pageId\":\"page\",\"revision\":1,\"contentFormat\":\"xml\",\"contentHash\":\"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF\",\"assetInventoryFingerprint\":\"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF\"}")]
    [InlineData("{\"pageId\":\"page\",\"revision\":1,\"contentFormat\":\"html\",\"contentHash\":\"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF\",\"assetInventoryFingerprint\":\"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF\",\"extra\":true}")]
    public void Active_content_reference_rejects_partial_or_malformed_pinned_forms(string referenceJson)
    {
        var schema = LoadSchema();
        using var reference = JsonDocument.Parse(referenceJson);
        var page = JsonSerializer.Serialize(new
        {
            title = "Page",
            navigationLabel = "Page",
            slug = "page",
            order = 0,
            visibility = "public",
            activeContentReference = reference.RootElement
        });

        Assert.Equal(SchemaValueStatus.Invalid, Validate(schema, page));
    }

    private static SchemaCompilationResult LoadSchema()
    {
        var root = RepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "catalog", "components", "system", "web", "page.schema.json"));
        var result = new BoundedJsonSchemaValidator().Compile(text);
        Assert.True(result.IsAccepted);
        return result;
    }

    private static SchemaValueStatus Validate(SchemaCompilationResult schema, object page) =>
        new BoundedJsonSchemaValidator().Validate(schema.ProfileId, schema.NormalizedSchema,
            page is string json ? json : JsonSerializer.Serialize(page)).Status;

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
