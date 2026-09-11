using System.Text.Json;

namespace DantesRoleplay.Projections.Tests;

public sealed class ApplicationObjectSubmissionDiffTests
{
    private static readonly IReadOnlyList<GeneratedApplicationObjectWriteMapping> Mappings =
    [
        new("/name", "set", "profile", "/name", null),
        new("/note", "set", "profile", "/note", null),
        new("/note", "clear", "profile", "/note", null),
        new("/settings", "set", "profile", "/settings", null),
        new("/tags", "set", "profile", "/tags", null),
        new("/members", "relationship.add", null, null, "members")
    ];

    [Fact]
    public void Complete_submission_returns_only_changed_declared_writable_fields()
    {
        var changes = Diff(
            """{"id":"object.1","name":"Before","note":"Keep","calculated":3,"members":[{"id":"member.1"}]}""",
            """{"id":"object.1","name":"After","note":"Keep","calculated":3,"members":[{"id":"member.1"}]}""");

        Assert.Equal("{\"name\":\"After\"}", changes);
    }

    [Fact]
    public void Omitted_fields_are_preserved_and_an_equal_read_only_subset_is_ignored()
    {
        var changes = Diff(
            """{"id":"object.1","name":"Before","calculated":{"score":3,"label":"three"}}""",
            """{"calculated":{"score":3}}""");

        Assert.Equal("{}", changes);
        Assert.Equal("{}", Diff(
            """{"id":"object.1","name":"Before","metadata":{"complete":true}}""", "{}"));
        Assert.Equal("{}", Diff(
            """{"metadata":{"complete":true,"nextCursor":"next"}}""", """{"metadata":{}}"""));
    }

    [Fact]
    public void Changed_added_or_removed_read_only_values_are_rejected()
    {
        AssertReadOnlyChanged("""{"id":"object.1"}""", """{"id":"object.2"}""");
        AssertReadOnlyChanged("""{"id":"object.1"}""", """{"extra":true}""");
        AssertReadOnlyChanged("""{"id":"object.1"}""", """{"extra":null}""");
        AssertReadOnlyChanged("""{"members":[{"id":"member.1"}]}""", """{"members":[]}""");
        AssertReadOnlyChanged("""{"metadata":{"complete":true}}""", """{"metadata":{"complete":false}}""");
    }

    [Fact]
    public void Explicit_null_is_a_clear_only_when_that_operation_is_declared()
    {
        Assert.Equal("{\"note\":null}", Diff("""{"name":"Before","note":"Keep"}""",
            """{"note":null}"""));
        var failure = Assert.Throws<ApplicationObjectWriteException>(() => Diff(
            """{"name":"Before"}""", """{"name":null}"""));
        Assert.Equal("OBJECT_WRITE_REQUEST_INVALID", failure.Code);
        Assert.Equal("{}", Diff("""{}""", """{"note":null}"""));
    }

    [Fact]
    public void Declared_object_and_array_values_are_compared_atomically()
    {
        var changes = Diff(
            """{"settings":{"mode":"one","private":true},"tags":["a"],"readonly":[1]}""",
            """{"settings":{"mode":"two","private":true},"tags":["b"],"readonly":[1]}""");

        Assert.Equal("two", Json(changes).GetProperty("settings").GetProperty("mode").GetString());
        Assert.True(Json(changes).GetProperty("settings").GetProperty("private").GetBoolean());
        Assert.Equal("b", Json(changes).GetProperty("tags")[0].GetString());
        Assert.False(Json(changes).TryGetProperty("readonly", out _));
    }

    [Fact]
    public void Exact_nested_mapping_can_create_an_absent_or_null_optional_parent()
    {
        var mappings = new[]
        {
            new GeneratedApplicationObjectWriteMapping("/profile/note", "set", "profile", "/note", null)
        };

        Assert.Equal("{\"profile\":{\"note\":\"added\"}}",
            ApplicationObjectSubmissionDiffer.CreateChanges("{}", """{"profile":{"note":"added"}}""", mappings));
        Assert.Equal("{\"profile\":{\"note\":\"added\"}}",
            ApplicationObjectSubmissionDiffer.CreateChanges("""{"profile":null}""",
                """{"profile":{"note":"added"}}""", mappings));
        AssertReadOnlyChangedWith(mappings, """{"profile":"opaque"}""",
            """{"profile":{"note":"added"}}""");
        AssertReadOnlyChangedWith(mappings, "{}", """{"profile":{"extra":null}}""");
    }

    [Fact]
    public void Escaped_nested_pointers_and_canonical_key_order_are_preserved()
    {
        var mappings = new[]
        {
            new GeneratedApplicationObjectWriteMapping("/z", "set", "profile", "/z", null),
            new GeneratedApplicationObjectWriteMapping("/a~1b/~0note", "set", "profile", "/note", null)
        };
        var changes = ApplicationObjectSubmissionDiffer.CreateChanges(
            """{"z":"before","a/b":{"~note":"before"}}""",
            """{"z":"after","a/b":{"~note":"after"}}""", mappings);

        Assert.Equal("{\"a/b\":{\"~note\":\"after\"},\"z\":\"after\"}", changes);
    }

    [Fact]
    public void Object_property_order_is_not_a_change_but_array_order_remains_exact()
    {
        Assert.Equal("{}", Diff(
            """{"settings":{"mode":"one","private":true},"metadata":{"a":1,"b":2}}""",
            """{"settings":{"private":true,"mode":"one"},"metadata":{"b":2,"a":1}}"""));
        AssertReadOnlyChanged("""{"readonly":["a","b"]}""", """{"readonly":["b","a"]}""");
    }

    [Fact]
    public void A_large_valid_current_object_allows_a_narrow_bounded_submission()
    {
        var current = JsonSerializer.Serialize(new { name = "before", retained = new string('x', 70_000) });
        Assert.Equal("{\"name\":\"after\"}", Diff(current, """{"name":"after"}"""));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"name\":\"one\",\"name\":\"two\"}")]
    public void Invalid_submissions_fail_before_diffing(string submitted)
    {
        var failure = Assert.Throws<ApplicationObjectWriteException>(() => Diff("{}", submitted));
        Assert.Equal("OBJECT_WRITE_REQUEST_INVALID", failure.Code);
    }

    [Fact]
    public void Excessive_depth_and_submitted_bytes_are_rejected()
    {
        var deep = string.Concat(Enumerable.Repeat("{\"x\":", 33)) + "0"
            + string.Concat(Enumerable.Repeat("}", 33));
        Assert.Equal("OBJECT_WRITE_REQUEST_INVALID",
            Assert.Throws<ApplicationObjectWriteException>(() => Diff("{}", deep)).Code);
        var oversized = JsonSerializer.Serialize(new { name = new string('x', 65_536) });
        Assert.Equal("OBJECT_WRITE_REQUEST_INVALID",
            Assert.Throws<ApplicationObjectWriteException>(() => Diff("{}", oversized)).Code);
    }

    private static string Diff(string current, string submitted) =>
        ApplicationObjectSubmissionDiffer.CreateChanges(current, submitted, Mappings);

    private static void AssertReadOnlyChanged(string current, string submitted)
        => AssertReadOnlyChangedWith(Mappings, current, submitted);

    private static void AssertReadOnlyChangedWith(
        IReadOnlyList<GeneratedApplicationObjectWriteMapping> mappings,
        string current,
        string submitted)
    {
        var failure = Assert.Throws<ApplicationObjectWriteException>(() =>
            ApplicationObjectSubmissionDiffer.CreateChanges(current, submitted, mappings));
        Assert.Equal("OBJECT_WRITE_READ_ONLY_CHANGED", failure.Code);
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
