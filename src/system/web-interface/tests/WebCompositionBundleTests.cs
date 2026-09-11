using System.IO.Compression;
using System.Text;
using DantesRoleplay.Web.Pages;

namespace DantesRoleplay.Tests;

public sealed class WebCompositionBundleTests
{
    [Fact]
    public void Html_normalization_preserves_legacy_content_and_copies_assets()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var normalized = WebPageContentValidator.Normalize(new WebPageBundle("<h1>Home</h1>",
            [new("assets/site.css", bytes)]));
        bytes[0] = 9;

        Assert.Equal(WebPageContentFormat.Html, normalized.ContentFormat);
        Assert.Null(normalized.CompositionJson);
        Assert.Null(normalized.CompositionHash);
        Assert.Equal((byte)1, normalized.Assets.Single().Content[0]);
        Assert.Equal("HTML_COMPOSITION_FIELDS", ExceptionCode(() => WebPageContentValidator.Normalize(
            new WebPageBundle("<p>x</p>", []) { CompositionJson = "{}" })));
    }

    [Fact]
    public void Composition_normalization_is_canonical_hashed_and_checks_selected_assets()
    {
        const string composition = """
            {"root":{"kind":"asset","path":"assets/logo.svg"},"generation":"g","components":[],"formatVersion":1}
            """;
        var normalized = WebPageContentValidator.Normalize(new WebPageBundle(string.Empty,
            [new("assets/logo.svg", [1])])
        {
            ContentFormat = WebPageContentFormat.Composition,
            CompositionJson = composition
        });

        Assert.Equal("{\"components\":[],\"formatVersion\":1,\"generation\":\"g\",\"root\":{\"kind\":\"asset\",\"path\":\"assets/logo.svg\"}}", normalized.CompositionJson);
        Assert.Matches("^[0-9A-F]{64}$", normalized.CompositionHash!);
        Assert.Equal("COMPOSITION_HASH_MISMATCH", ExceptionCode(() => WebPageContentValidator.Normalize(normalized with
        {
            CompositionHash = new string('0', 64)
        })));
        Assert.Equal("MISSING_ASSET", ExceptionCode(() => WebPageContentValidator.Normalize(new WebPageBundle(string.Empty, [])
        {
            ContentFormat = WebPageContentFormat.Composition,
            CompositionJson = composition
        })));
    }

    [Fact]
    public async Task Bundle_reader_accepts_one_composition_file_and_rejects_ambiguous_payloads()
    {
        var reader = new WebPageBundleReader();
        await using var composition = CreateZip(
            ("composition.json", Encoding.UTF8.GetBytes("{\"formatVersion\":1,\"generation\":\"g\",\"components\":[],\"root\":{\"kind\":\"text\",\"text\":\"x\"}}")),
            ("assets/logo.svg", [1, 2]));
        var bundle = await reader.ReadAsync(composition);
        Assert.Equal(WebPageContentFormat.Composition, bundle.ContentFormat);
        Assert.Equal(string.Empty, bundle.Html);
        Assert.NotNull(bundle.CompositionHash);

        await using var ambiguous = CreateZip(
            ("index.html", Encoding.UTF8.GetBytes("<p>x</p>")),
            ("composition.json", Encoding.UTF8.GetBytes("{}")));
        var exception = await Assert.ThrowsAsync<WebPageBundleException>(() => reader.ReadAsync(ambiguous));
        Assert.Equal("AMBIGUOUS_CONTENT", exception.Code);
    }

    [Fact]
    public void Composition_rejects_duplicate_json_and_exclusive_payload_violations()
    {
        Assert.Equal("DUPLICATE_FIELD", ExceptionCode(() => WebPageContentValidator.Normalize(new WebPageBundle(string.Empty, [])
        {
            ContentFormat = WebPageContentFormat.Composition,
            CompositionJson = "{\"formatVersion\":1,\"formatVersion\":1,\"generation\":\"g\",\"components\":[],\"root\":{\"kind\":\"text\",\"text\":\"x\"}}"
        })));
        Assert.Equal("COMPOSITION_HTML_REQUIRED_EMPTY", ExceptionCode(() => WebPageContentValidator.Normalize(new WebPageBundle(" ", [])
        {
            ContentFormat = WebPageContentFormat.Composition,
            CompositionJson = "{}"
        })));
    }

    [Fact]
    public async Task Composition_uploads_reject_invalid_utf8_and_duplicate_root_entries()
    {
        var reader = new WebPageBundleReader();
        await using var malformed = CreateZip(("composition.json", [0xff, 0xfe]));
        var encoding = await Assert.ThrowsAsync<WebPageBundleException>(() => reader.ReadAsync(malformed));
        Assert.Equal(400, encoding.StatusCode);
        await using var duplicate = CreateZip(("composition.json", "{}"u8.ToArray()), ("composition.json", "{}"u8.ToArray()));
        Assert.Equal("DUPLICATE_ASSET_PATH", (await Assert.ThrowsAsync<WebPageBundleException>(() => reader.ReadAsync(duplicate))).Code);
    }

    [Fact]
    public void Raw_and_canonical_composition_bytes_both_obey_the_one_mib_limit()
    {
        var raw = new WebPageBundle(string.Empty, [])
        { ContentFormat = WebPageContentFormat.Composition, CompositionJson = new string(' ', WebComposition.MaximumDocumentBytes + 1) + "{}" };
        Assert.Equal(413, Assert.Throws<WebPageBundleException>(() => WebPageContentValidator.Normalize(raw)).StatusCode);
        var node = "{\"kind\":\"text\",\"text\":\"" + new string('é', 20_000) + "\"}";
        var input = "{\"formatVersion\":1,\"generation\":\"g\",\"components\":[],\"root\":{\"kind\":\"element\",\"tag\":\"div\",\"children\":[" +
            string.Join(',', Enumerable.Repeat(node, 10)) + "]}}";
        Assert.True(Encoding.UTF8.GetByteCount(input) < WebComposition.MaximumDocumentBytes);
        Assert.Equal(413, Assert.Throws<WebPageBundleException>(() => WebPageContentValidator.Normalize(raw with { CompositionJson = input })).StatusCode);
    }

    private static string ExceptionCode(Action action)
    {
        var exception = Assert.Throws<WebPageBundleException>(action);
        return exception.Code;
    }

    private static MemoryStream CreateZip(params (string Path, byte[] Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var output = entry.Open();
                output.Write(content);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
