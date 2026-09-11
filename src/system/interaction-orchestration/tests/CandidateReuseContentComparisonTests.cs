using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Tests;

public sealed class CandidateReuseContentComparisonTests
{
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("sample-app");

    [Fact]
    public void Renamed_root_ids_with_identical_copy_are_duplicates()
    {
        Assert.True(CandidateReuseContentComparison.TryFindExactDuplicate(App,
            Record("sample-app.alpha", "{" + "\"id\":\"alpha\",\"scope\":\"read\",\"nested\":{\"id\":\"fixed\"}}"),
            Record("sample-app.beta", "{" + "\"nested\":{\"id\":\"fixed\"},\"scope\":\"read\",\"id\":\"sample-app.beta\"}")));
    }

    [Theory]
    [InlineData("{\"id\":\"beta\",\"source\":\"changed js\"}")]
    [InlineData("{\"id\":\"beta\",\"constraints\":\"changed\"}")]
    [InlineData("{\"id\":\"beta\",\"outputSchema\":{\"type\":\"string\"}}")]
    [InlineData("{\"id\":\"beta\",\"scope\":\"write\"}")]
    [InlineData("{\"id\":\"beta\",\"nested\":{\"id\":\"changed\"}}")]
    public void Any_non_root_content_change_is_not_a_duplicate(string changed)
    {
        var baseRecord = Record("sample-app.alpha", "{\"id\":\"alpha\",\"source\":\"js\",\"constraints\":\"safe\",\"outputSchema\":{\"type\":\"object\"},\"scope\":\"read\",\"nested\":{\"id\":\"fixed\"}}");
        Assert.False(CandidateReuseContentComparison.TryFindExactDuplicate(App, baseRecord, Record("sample-app.beta", changed)));
    }

    [Fact]
    public void Invalid_or_ineligible_records_fail_closed()
    {
        var valid = Record("sample-app.alpha", "{\"id\":\"alpha\"}");
        Assert.False(CandidateReuseContentComparison.TryFindExactDuplicate(App, valid, valid));
        Assert.False(CandidateReuseContentComparison.TryFindExactDuplicate(App, valid with { Kind = "entity" }, Record("sample-app.beta", "{\"id\":\"beta\"}")));
        Assert.False(CandidateReuseContentComparison.TryFindExactDuplicate(App, valid with { Status = "archived" }, Record("sample-app.beta", "{\"id\":\"beta\"}")));
        Assert.False(CandidateReuseContentComparison.TryFindExactDuplicate(App, valid with { ContentFingerprint = new string('A', 64) }, Record("sample-app.beta", "{\"id\":\"beta\"}")));
        Assert.False(CandidateReuseContentComparison.TryFindExactDuplicate(App, valid with { ContentJson = "{", ContentFingerprint = "bad" }, Record("sample-app.beta", "{\"id\":\"beta\"}")));
        Assert.False(CandidateReuseContentComparison.TryFindExactDuplicate(App, valid with { QualifiedId = "other.alpha" }, Record("sample-app.beta", "{\"id\":\"beta\"}")));
    }

    private static CatalogRecordDefinition Record(string id, string json) => new("sample", "procedure", id, id, "copy", [], [], "", "active", 1,
        json, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))), "source", "source.md");
}
