using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Ecs;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Knowledge.Tests;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class ItemRecipesProjectionTests
{
    private const string Id = "dnd2024.mechanic.inventory-item-recipes.project";
    private const string QueryId = "dnd2024.query.inventory-item-recipes";
    private static string Root { get { for(var d=new DirectoryInfo(AppContext.BaseDirectory);d is not null;d=d.Parent) if(File.Exists(Path.Combine(d.FullName,"DantesRoleplay.slnx"))) return d.FullName; throw new DirectoryNotFoundException(); } }
    private static string App => Path.Combine(Root,"catalog/applications/dnd2024");
    private static MechanicFile Mechanic => MechanicFile.Parse(File.ReadAllText(Path.Combine(App,"mechanics/data",Id+".md")),Id,File.ReadAllText(Path.Combine(App,"mechanics/data",Id+".js")));
    private static ApplicationQueryContract Query => ApplicationQueryContract.Parse(File.ReadAllText(Path.Combine(App,"queries/data",QueryId+".json")),ApplicationIdentifier.Parse("dnd2024"));
    private static readonly BoundedJsonSchemaValidator Schemas = new();
    private static string Json(object v) => JsonSerializer.Serialize(v);
    private static object Ref(string id) => new { entityId=id };

    [Fact]
    public void Registered_contract_pins_exact_effect_free_mechanic_and_schema()
    {
        var hash=ApplicationCatalogRecordContent.Fingerprint(ApplicationCatalogRecordContent.MechanicJson(Mechanic));
        var schema=Schemas.Compile(Query.OutputSchemaJson);
        Assert.True(hash==Query.ProjectionContentHash && schema.SchemaHash==Query.OutputSchemaHash,$"Mechanic hash: {hash}; Schema hash: {schema.SchemaHash}");
        Assert.Equal(ApplicationQueryExposure.BindingOnly,Query.Exposure);
        var req=MechanicRequirements.Parse(Mechanic.Requirements);
        Assert.Empty(req.Children); Assert.Empty(req.EffectComponentIds);
        Assert.Equal(Schemas.Compile(Query.InputSchemaJson!).SchemaHash,Schemas.Compile(req.InputSchema!.Value.GetRawText()).SchemaHash);
    }
    [Fact]
    public async Task Separate_groups_use_exact_links_deduplicate_and_do_not_require_owned_materials()
    {
        using var f=await Fixture.Create();
        await f.Recipe("recipe.both",["definition.shared","definition.shared"],["definition.shared"]);
        await f.Recipe("recipe.makes",["definition.shared"],["definition.absent"]);
        await f.Recipe("recipe.uses",["definition.other"],["definition.shared"]);
        await f.Recipe("recipe.hidden",["definition.shared"],[],known:false);
        await f.Game.AddKnowledgeAsync("fact.aaa", "An uncertain earlier statement", "recipe.both");
        await f.Game.RelateAsync(f.Game.Actor,"fact.aaa",f.Game.Binding.ExplicitStateRelationshipKind,"{\"stance\":\"suspected\"}");
        var (_,data)=await f.Run();
        Assert.Equal(["recipe.both","recipe.makes"],Rows(data,"makes").Select(v=>v.GetProperty("id").GetString()));
        Assert.Equal(["recipe.both","recipe.uses"],Rows(data,"uses").Select(v=>v.GetProperty("id").GetString()));
        Assert.DoesNotContain("recipe.hidden",data.GetRawText());
        Assert.Equal("Recorded recipe instructions",Rows(data,"makes")[0].GetProperty("description").GetString());
        Assert.Contains(Rows(data,"makes")[0].GetProperty("sources").EnumerateArray(),s=>s.GetProperty("knowledgeState").GetString()=="suspected");
        Assert.All(Rows(data,"makes"),v=>Assert.Equal("not-evaluated",v.GetProperty("availability").GetString()));
        Assert.Equal("Shared definition",Rows(data,"makes")[0].GetProperty("outputs")[0].GetProperty("name").GetString());
        Assert.Equal("1 Day",Rows(data,"makes")[0].GetProperty("duration").GetString());
        Assert.Equal("Fixture tool",Rows(data,"makes")[0].GetProperty("tools")[0].GetString());
        // Moving a selected stack inside a container changes the source, not recipe knowledge.
        await f.Game.AddEntityAsync("pack","Pack");
        await f.Game.Edges.MoveContainmentAsync(f.Game.Campaign,"pack",f.Game.Actor,"inventory",0);
        await f.Game.Edges.MoveContainmentAsync(f.Game.Campaign,"item.first","pack","inside",1);
        var (_,nested)=await f.Run();
        Assert.Equal(Rows(data,"makes").Length,Rows(nested,"makes").Length);
    }
    [Fact]
    public async Task Unknown_identity_and_labels_do_not_escape_through_known_recipe_links()
    {
        using var f=await Fixture.Create(identify:false);
        await f.Game.AddEntityAsync("definition.private","PRIVATE UNLEARNED MATERIAL");
        await f.Recipe("recipe.known",["definition.shared"],["definition.private"]);
        var (_,unknown)=await f.Run(); Assert.Empty(Rows(unknown,"makes")); Assert.Equal("partial",unknown.GetProperty("makes").GetProperty("state").GetString());
        await f.Know("item.first"); await f.Know("definition.shared");
        Assert.DoesNotContain("PRIVATE UNLEARNED MATERIAL",Json((await f.Resolve()).Projection!));
        var (_,known)=await f.Run(); Assert.DoesNotContain("definition.private",known.GetRawText());
        Assert.Equal(JsonValueKind.Null,Rows(known,"makes")[0].GetProperty("materials")[0].GetProperty("definitionId").ValueKind);
        await f.Know("definition.private");
        var (_,learned)=await f.Run();
        Assert.Equal("PRIVATE UNLEARNED MATERIAL",Rows(learned,"makes")[0].GetProperty("materials")[0].GetProperty("name").GetString());
    }
    [Fact]
    public async Task Incomplete_known_recipes_are_partial_but_unknown_candidates_do_not_change_empty_groups()
    {
        using var f=await Fixture.Create();
        await f.Recipe("recipe.hidden",[],[],known:false);
        var (_,empty)=await f.Run(); Assert.Equal("empty",empty.GetProperty("makes").GetProperty("state").GetString());
        await f.Recipe("recipe.incomplete",[],["definition.shared"]);
        var (_,partial)=await f.Run(); Assert.Equal("partial",partial.GetProperty("makes").GetProperty("state").GetString());
        Assert.Equal("definition-incomplete",Rows(partial,"uses")[0].GetProperty("availability").GetString());
    }
    [Fact]
    public async Task Pagination_uses_authorized_matches_and_rejects_changed_knowledge_or_inventory()
    {
        using var f=await Fixture.Create();
        for(var n=0;n<18;n++) await f.Recipe($"recipe.{n:00}",["definition.shared"],[]);
        var (revision,first)=await f.Run(); Assert.Equal(16,Rows(first,"makes").Length); Assert.Equal(16,first.GetProperty("makes").GetProperty("nextOffset").GetInt32());
        var (_,second)=await f.Run(16,revision); Assert.Equal(2,Rows(second,"makes").Length);
        Assert.Empty(Rows(first,"makes").Select(v=>v.GetProperty("id").GetString()).Intersect(Rows(second,"makes").Select(v=>v.GetProperty("id").GetString())));
        await f.Game.Edges.SetRelationshipAsync(f.Game.Campaign,f.Game.Actor,"fact.recipe.00",f.Game.Binding.ExplicitStateRelationshipKind,"{\"stance\":\"unknown\"}",1);
        await f.Run(16,revision,expectFailure:true);
        var (fresh,_) = await f.Run(); Assert.NotEqual(revision,fresh);
        await f.Game.Edges.MoveContainmentAsync(f.Game.Campaign,"item.first",f.Game.World,"elsewhere",1);
        Assert.False((await f.Resolve()).Ok);
    }
    [Fact]
    public async Task Long_multibyte_pages_fit_the_closed_byte_contract_with_continuation()
    {
        using var f=await Fixture.Create();
        for(var n=0;n<17;n++) await f.Recipe($"recipe.{n:00}",Enumerable.Repeat("definition.shared",16).ToArray(),Enumerable.Repeat("definition.shared",16).ToArray(),prose:new string('界',1000));
        var (_,data)=await f.Run(); Assert.True(System.Text.Encoding.UTF8.GetByteCount(data.GetRawText())<=65536);
        Assert.Equal("partial",data.GetProperty("makes").GetProperty("state").GetString());
        Assert.True(data.GetProperty("makes").GetProperty("nextOffset").GetInt32()>0);
    }
    private static JsonElement[] Rows(JsonElement d,string group)=>d.GetProperty(group).GetProperty("entries").EnumerateArray().ToArray();
    private sealed class Policy(KnowledgeCoreTests.KnowledgeFixture game):IAuthorizedKnowledgeAudiencePolicy
    { public Task<KnowledgeAudienceResolution> ResolveAsync(string campaignId,CancellationToken cancellationToken=default)=>Task.FromResult(new KnowledgeAudienceResolution(new("principal",game.Campaign,KnowledgeAudienceRole.Actor,game.Actor,"policy"))); }
    private sealed class Binding(KnowledgeApplicationBinding binding):IKnowledgeApplicationBindingResolver
    { public Task<KnowledgeApplicationBinding?> ResolveAsync(string campaignId,CancellationToken cancellationToken=default)=>Task.FromResult<KnowledgeApplicationBinding?>(binding); }
    private sealed class Fixture:IDisposable
    {
        public KnowledgeCoreTests.KnowledgeFixture Game {get;}=new();
        private readonly MechanicRequirements req=MechanicRequirements.Parse(Mechanic.Requirements);
        private readonly Dictionary<string,string> types=[];
        private readonly ApplicationMechanicProjectionMapping mapping;
        private readonly ApplicationAuthorizedProjectionResolver resolver;
        private Fixture(){var components=new Dictionary<string,EcsComponentReference>();foreach(var id in req.AllComponentIds()){var type="fixture-knowledge.recipe-"+types.Count;types[id]=type;components[id]=Game.DefineComponent(type);} mapping=new(components,new Dictionary<string,string>());
            resolver=new(Game.Db,new Policy(Game),new Binding(Game.Binding),new ApplicationKnowledgeActorParticipationVerifier(Game.Entities,Game.Edges),Game.Source,Game.States);}
        public static async Task<Fixture> Create(bool identify=true){var f=new Fixture();await f.Game.AddCoreAsync();await f.Game.AddParticipationAsync();
            foreach(var (id,name) in new[]{("item.first","Carried item"),("definition.shared","Shared definition"),("fixture.tool","Fixture tool"),("fixture.crafter","Fixture training"),("fixture.day","Day")}) {await f.Game.AddEntityAsync(id,name);if(identify||id.StartsWith("fixture.")) await f.Know(id);}
            await f.Component("item.first","dnd2024.core.definition-link",new{definition=Ref("definition.shared")});await f.Component("item.first","dnd2024.item.quantity",new{current=2});
            await f.Game.Edges.MoveContainmentAsync(f.Game.Campaign,"item.first",f.Game.Actor,"pack",0);return f;}
        public Task Know(string id)=>Game.RelateAsync(Game.Actor,id,Game.Binding.ExplicitStateRelationshipKind,"{\"stance\":\"known\"}");
        private async Task Component(string id,string type,object value){var schema=Schemas.Compile(File.ReadAllText(Path.Combine(App,"components",type+".schema.json")));Assert.Equal(SchemaValueStatus.Valid,Schemas.Validate(schema.NormalizedSchema,Json(value)).Status);await Game.ComponentAsync(id,types[type],Json(value));}
        public async Task Recipe(string id,string[] outputs,string[] materials,bool known=true,string prose="Recorded recipe instructions"){
            await Game.AddEntityAsync(id,"Recipe "+id);await Component(id,"dnd2024.crafting.recipe",new{outputs=outputs.Select(d=>new{definition=Ref(d),quantity=1}),materialRequirements=materials.Select(d=>new{definition=Ref(d),quantity=1}),
                workDuration=new{kind="measured",amount=1,unit=Ref("fixture.day")},toolRequirement=new{@operator="predicate",predicateId="predicate.proficiency.tool",arguments=new[]{Ref("fixture.tool")}},crafterRequirement=new{@operator="predicate",predicateId="predicate.proficiency.crafter",arguments=new[]{Ref("fixture.crafter")}}});
            await Game.AddKnowledgeAsync("fact."+id,prose,id);if(known)await Know("fact."+id);}
        public Task<ProjectionResult> Resolve(int offset=0,string? expected=null)=>resolver.ResolveAsync(new(Game.Campaign,ApplicationIdentifier.Parse(Game.ApplicationId),Id,new string('A',64),mapping,
            new Dictionary<string,string>{{"subject",Game.Actor},{"campaign",Game.Campaign}},Json(new{itemId="item.first",makesOffset=offset,usesOffset=0,expectedSourceRevision=expected}),0,Audience:new("player")),req);
        public async Task<(string,JsonElement)> Run(int offset=0,string? expected=null,bool expectFailure=false){var projection=await Resolve(offset,expected);
            if(expectFailure){Assert.Null(projection.Projection);Assert.Equal(["READ_MODEL_SOURCE_STALE"],projection.Problems);return ("",default);}
            Assert.True(projection.Ok,string.Join(';',projection.Problems));
            var run=await new JintMechanicEngine().RunAsync(Mechanic.Source,projection.Projection!,ExecutionLimits.Default);
            Assert.True(run.Ok,run.Error);Assert.Empty(run.Output.Effects);Assert.Empty(run.Output.Events);Assert.Empty(run.Output.Notifications);
            var valid=Schemas.Validate(Schemas.Compile(Query.OutputSchemaJson).NormalizedSchema,run.Output.Data);Assert.True(valid.Status==SchemaValueStatus.Valid,Json(valid));
            return (projection.Projection!.AuthorizedSourceRevision!,JsonDocument.Parse(run.Output.Data).RootElement.Clone());}
        public void Dispose()=>Game.Dispose();
    }
}
