using System.Text.Json;
using DantesRoleplay.Web.Pages;

namespace DantesRoleplay.Tests;

public sealed class WebCompositionTests
{
    [Fact]
    public void Dynamic_bindings_retain_only_catalog_intent_and_bounded_input()
    {
        var parsed = new WebCompositionParser().Parse("""
            {"formatVersion":1,"generation":"g","queries":[{"name":"summary","query":"example.query.summary","input":{"filter":"open"}}],
             "actions":[{"name":"refresh","mechanic":"example.mechanic.refresh"}],"components":[],
             "root":{"kind":"element","tag":"button","action":"refresh","children":[{"kind":"value","path":"summary.label"}]}}
            """);

        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors.Select(error => error.Code)));
        Assert.Equal("example.query.summary", parsed.Document!.QueryBindings["summary"].QualifiedQueryId);
        Assert.Equal("{\"filter\":\"open\"}", parsed.Document.QueryBindings["summary"].InputJson);
        Assert.Equal("example.mechanic.refresh", parsed.Document.ActionBindings["refresh"].QualifiedMechanicId);

        foreach (var authorityField in new[] { "grantReference", "stateSpaceId", "roles", "audience" })
        {
            var forged = new WebCompositionParser().Parse($$$"""
                {"formatVersion":1,"generation":"g","queries":[{"name":"summary","query":"example.query.summary","{{{authorityField}}}":"forged"}],
                 "components":[],"root":{"kind":"value","path":"summary"}}
                """);
            Assert.Equal("UNKNOWN_FIELD", Assert.Single(forged.Errors).Code);
        }
    }

    [Fact]
    public void Reusable_component_renders_slots_loops_and_escaped_values()
    {
        var parsed = new WebCompositionParser().Parse("""
            {"formatVersion":1,"generation":"gen-1","queries":[{"name":"items"}],"actions":[{"name":"select"}],
             "components":[{"id":"card","revision":"r1","requiredProps":["title"],"template":{"kind":"element","tag":"section","children":[{"kind":"value","path":"props.title"},{"kind":"slot","name":"body"}]}},{"id":"command","revision":"r1","template":{"kind":"element","tag":"button","action":"select","children":[{"kind":"text","text":"Select"}]}}],
             "root":{"kind":"element","tag":"main","children":[{"kind":"component","id":"card","revision":"r1","props":{"title":"<unsafe>"},"slots":{"body":[{"kind":"each","items":"items","as":"item","children":[{"kind":"element","tag":"p","children":[{"kind":"value","path":"item.name"}]}]}]}},{"kind":"component","id":"command","revision":"r1"},{"kind":"component","id":"command","revision":"r1"}]}}
            """);

        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors.Select(error => error.Code)));
        using var values = JsonDocument.Parse("[{\"name\":\"Ada & Bob\"}]");
        var rendered = new WebCompositionRenderer().Render(parsed.Document!, new Dictionary<string, JsonElement>
        {
            ["items"] = values.RootElement.Clone()
        });

        Assert.True(rendered.IsSuccess, string.Join("; ", rendered.Errors.Select(error => error.Code)));
        Assert.Equal("<main><section>&lt;unsafe&gt;<p>Ada &amp; Bob</p></section><button data-web-action=\"select\" type=\"button\" disabled aria-disabled=\"true\">Select</button><button data-web-action=\"select\" type=\"button\" disabled aria-disabled=\"true\">Select</button></main>", rendered.Html);
    }

    [Fact]
    public void Required_props_component_cycles_and_unknown_fields_are_rejected()
    {
        var parser = new WebCompositionParser();
        var missing = parser.Parse("""
            {"formatVersion":1,"generation":"g","components":[{"id":"x","revision":"1","requiredProps":["name"],"template":{"kind":"text","text":"x"}}],"root":{"kind":"component","id":"x","revision":"1"}}
            """);
        var cycle = parser.Parse("""
            {"formatVersion":1,"generation":"g","components":[
              {"id":"a","revision":"1","template":{"kind":"component","id":"b","revision":"1"}},
              {"id":"b","revision":"1","template":{"kind":"component","id":"a","revision":"1"}}],
              "root":{"kind":"component","id":"a","revision":"1"}}
            """);
        var unknown = parser.Parse("""
            {"formatVersion":1,"generation":"g","components":[],"root":{"kind":"text","text":"x","html":"<script>"}}
            """);
        var unsafeUrl = parser.Parse("""
            {"formatVersion":1,"generation":"g","components":[],"root":{"kind":"element","tag":"a","attributes":{"href":"javascript:alert(1)"},"children":[]}}
            """);

        Assert.Equal("MISSING_REQUIRED_PROP", Assert.Single(missing.Errors).Code);
        Assert.Equal("COMPONENT_CYCLE", Assert.Single(cycle.Errors).Code);
        Assert.Equal("UNKNOWN_FIELD", Assert.Single(unknown.Errors).Code);
        Assert.Equal("UNSAFE_ATTRIBUTE_VALUE", Assert.Single(unsafeUrl.Errors).Code);
    }

    [Fact]
    public void Assets_must_be_supplied_by_the_selected_content_revision()
    {
        const string document = """
            {"formatVersion":1,"generation":"g","components":[],"root":{"kind":"element","tag":"img","attributes":{"src":"asset:assets/logo.svg"},"children":[]}}
            """;
        var parser = new WebCompositionParser();

        Assert.Equal("MISSING_ASSET", Assert.Single(parser.Parse(document).Errors).Code);
        Assert.Equal("INVALID_AVAILABLE_ASSET", Assert.Single(parser.Parse(document, ["index.html"]).Errors).Code);
        var parsed = parser.Parse(document, ["assets/logo.svg"]);
        Assert.True(parsed.IsValid);
        var renderer = new WebCompositionRenderer();
        Assert.Equal("ASSET_BASE_REQUIRED", Assert.Single(renderer.Render(parsed.Document!, new Dictionary<string, JsonElement>()).Errors).Code);
        Assert.Equal("UNSAFE_ASSET_BASE", Assert.Single(renderer.Render(parsed.Document!, new Dictionary<string, JsonElement>(), "https://example.test/").Errors).Code);
        var rendered = renderer.Render(parsed.Document!, new Dictionary<string, JsonElement>(), "/ui/control-center/");
        Assert.Equal("<img src=\"/ui/control-center/assets/logo.svg\">", rendered.Html);
        rendered = renderer.Render(parsed.Document!, new Dictionary<string, JsonElement>(), "/ui/example/content/retained-page/revisions/2/");
        Assert.Equal("<img src=\"/ui/example/content/retained-page/revisions/2/assets/logo.svg\">", rendered.Html);
        foreach (var invalidBase in new[] { "/ui/example/revisions/2/", "/ui/example/content/page/revisions/0/",
            "/ui/example/content/page/revisions/02/", "/ui/example/content/page/revisions/-1/",
            "/ui/example/content/page/revisions/2147483648/", "/ui/example/content/../revisions/2/",
            "/ui/example/content/page/revisions/2/?token=untrusted" })
            Assert.Equal("UNSAFE_ASSET_BASE", Assert.Single(renderer.Render(parsed.Document!,
                new Dictionary<string, JsonElement>(), invalidBase).Errors).Code);
    }

    [Fact]
    public void Missing_query_data_and_resource_budgets_fail_closed()
    {
        var parsed = new WebCompositionParser().Parse("""
            {"formatVersion":1,"generation":"g","queries":[{"name":"rows"}],"components":[],"root":{"kind":"each","items":"rows","as":"row","children":[{"kind":"value","path":"row"}]}}
            """);
        Assert.True(parsed.IsValid);
        var missing = new WebCompositionRenderer().Render(parsed.Document!, new Dictionary<string, JsonElement>());
        Assert.Equal("MISSING_QUERY_DATA", Assert.Single(missing.Errors).Code);

        using var tooMany = JsonDocument.Parse("[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100]");
        var bounded = new WebCompositionRenderer().Render(parsed.Document!, new Dictionary<string, JsonElement> { ["rows"] = tooMany.RootElement.Clone() });
        Assert.Equal("LOOP_LIMIT_EXCEEDED", Assert.Single(bounded.Errors).Code);
    }

    [Fact]
    public void Conditions_unknown_references_duplicate_props_and_urls_are_checked()
    {
        var parser = new WebCompositionParser();
        var conditional = parser.Parse("""
            {"formatVersion":1,"generation":"g","queries":[{"name":"visible"}],"components":[],"root":{"kind":"if","condition":"visible","children":[{"kind":"text","text":"yes"}],"otherwise":[{"kind":"text","text":"no"}]}}
            """);
        using var falseValue = JsonDocument.Parse("false");
        Assert.Equal("no", new WebCompositionRenderer().Render(conditional.Document!, new Dictionary<string, JsonElement> { ["visible"] = falseValue.RootElement.Clone() }).Html);

        Assert.Equal("MISSING_COMPONENT", Assert.Single(parser.Parse("""
            {"formatVersion":1,"generation":"g","components":[],"root":{"kind":"component","id":"missing","revision":"1"}}
            """).Errors).Code);
        Assert.Equal("DUPLICATE_FIELD", Assert.Single(parser.Parse("""
            {"formatVersion":1,"generation":"g","components":[],"root":{"kind":"component","id":"x","id":"y","revision":"1"}}
            """).Errors).Code);
        Assert.Equal("UNSAFE_ATTRIBUTE_VALUE", Assert.Single(parser.Parse("""
            {"formatVersion":1,"generation":"g","components":[],"root":{"kind":"element","tag":"img","attributes":{"src":"//host/a.png"},"children":[]}}
            """).Errors).Code);
        Assert.Equal("VOID_ELEMENT_CHILDREN", Assert.Single(parser.Parse("""
            {"formatVersion":1,"generation":"g","components":[],"root":{"kind":"element","tag":"img","children":[{"kind":"text","text":"x"}]}}
            """).Errors).Code);
    }

    [Fact]
    public void Expanded_output_is_bounded_even_when_the_document_is_small()
    {
        var text = new string('x', 20_000);
        var document = "{\"formatVersion\":1,\"generation\":\"g\",\"queries\":[{\"name\":\"rows\"}],\"components\":[],\"root\":{\"kind\":\"each\",\"items\":\"rows\",\"as\":\"row\",\"children\":[{\"kind\":\"text\",\"text\":" + JsonSerializer.Serialize(text) + "}]}}";
        var parsed = new WebCompositionParser().Parse(document);
        using var rows = JsonDocument.Parse("[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99]");
        var rendered = new WebCompositionRenderer().Render(parsed.Document!, new Dictionary<string, JsonElement> { ["rows"] = rows.RootElement.Clone() });
        Assert.Equal("RENDER_OUTPUT_LIMIT_EXCEEDED", Assert.Single(rendered.Errors).Code);
    }

    [Fact]
    public void Escaping_streams_into_the_output_cap_without_allocating_the_full_expansion()
    {
        var parsed = new WebCompositionParser().Parse("""
            {"formatVersion":1,"generation":"g","queries":[{"name":"value"}],"components":[],
             "root":{"kind":"value","path":"value"}}
            """);
        using var data = JsonDocument.Parse("\"" + new string('&', 600_000) + "\"");
        var values = new Dictionary<string, JsonElement> { ["value"] = data.RootElement };
        var renderer = new WebCompositionRenderer();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var rendered = renderer.Render(parsed.Document!, values);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Null(rendered.Html);
        Assert.Equal("RENDER_OUTPUT_LIMIT_EXCEEDED", Assert.Single(rendered.Errors).Code);
        // A full &amp; string plus the output buffer would allocate over 12 MiB. The cap must
        // prevent that transient expansion as well as rejecting the final oversized response.
        Assert.InRange(allocated, 0, 8 * 1024 * 1024);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Output_budget_includes_markup_and_accepts_the_exact_limit(int extra, bool success)
    {
        var parsed = new WebCompositionParser().Parse("""
            {"formatVersion":1,"generation":"g","queries":[{"name":"value"}],"components":[],
             "root":{"kind":"element","tag":"p","children":[{"kind":"value","path":"value"}]}}
            """);
        using var data = JsonDocument.Parse("\"" + new string('x', WebComposition.MaximumRenderedCharacters - 7 + extra) + "\"");
        var rendered = new WebCompositionRenderer().Render(parsed.Document!, new Dictionary<string, JsonElement> { ["value"] = data.RootElement });
        Assert.Equal(success, rendered.IsSuccess);
        if (success) Assert.Equal(WebComposition.MaximumRenderedCharacters, rendered.Html!.Length);
        else Assert.Equal("RENDER_OUTPUT_LIMIT_EXCEEDED", Assert.Single(rendered.Errors).Code);
    }

    [Fact]
    public void Expanded_nodes_are_bounded_even_when_nested_loops_emit_no_text()
    {
        var parsed = new WebCompositionParser().Parse("""
            {"formatVersion":1,"generation":"g","queries":[{"name":"a"},{"name":"b"},{"name":"c"},{"name":"d"}],"components":[],"root":{"kind":"each","items":"a","as":"one","children":[{"kind":"each","items":"b","as":"two","children":[{"kind":"each","items":"c","as":"three","children":[{"kind":"each","items":"d","as":"four","children":[]}]}]}]}}
            """);
        using var values = JsonDocument.Parse("[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99]");
        var input = new Dictionary<string, JsonElement>
        {
            ["a"] = values.RootElement.Clone(), ["b"] = values.RootElement.Clone(), ["c"] = values.RootElement.Clone(), ["d"] = values.RootElement.Clone()
        };
        var rendered = new WebCompositionRenderer().Render(parsed.Document!, input);
        Assert.Equal("RENDER_NODE_LIMIT_EXCEEDED", Assert.Single(rendered.Errors).Code);
    }

    [Fact]
    public void Format_loop_scope_and_compiled_document_boundaries_fail_closed()
    {
        var parser = new WebCompositionParser();
        Assert.Equal("UNSUPPORTED_FORMAT_VERSION", Assert.Single(parser.Parse("""
            {"formatVersion":1.5,"generation":"g","components":[],"root":{"kind":"text","text":"x"}}
            """).Errors).Code);
        Assert.Equal("UNSUPPORTED_FORMAT_VERSION", Assert.Single(parser.Parse("""
            {"formatVersion":999999999999999999999999999999999,"generation":"g","components":[],"root":{"kind":"text","text":"x"}}
            """).Errors).Code);
        Assert.Equal("RESERVED_LOOP_NAME", Assert.Single(parser.Parse("""
            {"formatVersion":1,"generation":"g","queries":[{"name":"rows"}],"components":[],"root":{"kind":"each","items":"rows","as":"rows","children":[]}}
            """).Errors).Code);
        Assert.Empty(typeof(WebCompositionDocument).GetConstructors());
    }

    [Fact]
    public void Slots_keep_the_callers_props_scope()
    {
        var parsed = new WebCompositionParser().Parse("""
            {"formatVersion":1,"generation":"g","components":[
              {"id":"shell","revision":"1","template":{"kind":"element","tag":"section","children":[{"kind":"slot","name":"body"}]}},
              {"id":"outer","revision":"1","requiredProps":["label"],"template":{"kind":"component","id":"shell","revision":"1","slots":{"body":[{"kind":"value","path":"props.label"}]}}}],
              "root":{"kind":"component","id":"outer","revision":"1","props":{"label":"caller"}}}
            """);
        var rendered = new WebCompositionRenderer().Render(parsed.Document!, new Dictionary<string, JsonElement>());
        Assert.Equal("<section>caller</section>", rendered.Html);
    }
}
