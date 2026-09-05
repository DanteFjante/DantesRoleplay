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

public sealed class ItemUsesProjectionTests
{
    private const string Id = "dnd2024.mechanic.inventory-item-uses.project";
    private const string QueryId = "dnd2024.query.inventory-item-uses";
    private static string Root { get { for(var d=new DirectoryInfo(AppContext.BaseDirectory);d is not null;d=d.Parent) if(File.Exists(Path.Combine(d.FullName,"DantesRoleplay.slnx"))) return d.FullName; throw new DirectoryNotFoundException(); } }
    private static string App => Path.Combine(Root,"catalog/applications/dnd2024");
    private static MechanicFile Mechanic => MechanicFile.Parse(File.ReadAllText(Path.Combine(App,"mechanics/data",Id+".md")),Id,File.ReadAllText(Path.Combine(App,"mechanics/data",Id+".js")));
    private static ApplicationQueryContract Query => ApplicationQueryContract.Parse(File.ReadAllText(Path.Combine(App,"queries/data",QueryId+".json")),ApplicationIdentifier.Parse("dnd2024"));
    private static readonly BoundedJsonSchemaValidator Schemas = new();
    private static string Json(object v) => JsonSerializer.Serialize(v);
    private static object Ref(string id) => new { entityId=id };


    [Fact]
    public void Contract_pins_exact_source_schema_and_bounded_read_only_linked_materialization()
    {
        var hash=ApplicationCatalogRecordContent.Fingerprint(ApplicationCatalogRecordContent.MechanicJson(Mechanic));
        var schema=Schemas.Compile(Query.OutputSchemaJson);
        Assert.True(hash==Query.ProjectionContentHash && schema.SchemaHash==Query.OutputSchemaHash,$"Mechanic hash: {hash}; Schema hash: {schema.SchemaHash}");
        var req=MechanicRequirements.Parse(Mechanic.Requirements);
        Assert.True(req.AuthorizedContext!.Valid(req));Assert.Empty(req.Children);Assert.Empty(req.EffectComponentIds);
        Assert.Equal(ApplicationQueryExposure.BindingOnly,Query.Exposure);
        Assert.Equal(Schemas.Compile(Query.InputSchemaJson!).SchemaHash,Schemas.Compile(req.InputSchema!.Value.GetRawText()).SchemaHash);
        var bad=req.AuthorizedContext.SourceSets with { Activities=req.AuthorizedContext.SourceSets.Activities! with { Linked=new(){ComponentId="fixture.membership",Field="activities",TargetComponentIds=["fixture.presentation"],ReferencePaths=new(){["undeclared"]=["value"]}}}};
        Assert.False(bad.Valid);
    }
    [Fact]
    public async Task Linked_weapon_tool_and_consumable_fields_have_canonical_sources_without_execution()
    {
        using var f=await Fixture.Create();
        foreach(var (id,name) in new[]{("activity.weapon","Staff attack"),("activity.tool","Carve wood"),("activity.heal","Drink restorative")})await f.Activity(id,name);
        foreach(var (id,name) in new[]{("fixture.die","d6"),("fixture.damage","Bludgeoning"),("fixture.healing","Hit points"),("fixture.charge","Charges")}){await f.Game.AddEntityAsync(id,name);await f.Know(id);}
        await f.Component("activity.weapon","dnd2024.activity.attack",new{mode="melee",abilityOptions=new[]{Ref("fixture.crafter")}});
        await f.Component("activity.weapon","dnd2024.activity.damage",new{parts=new[]{new{amount=new{count=1,dieRef=Ref("fixture.die"),modifier=0},damageType=Ref("fixture.damage")}},delivery="on-hit",criticalBehavior="eligible"});
        await f.Component("activity.tool","dnd2024.activity.check",new{abilityOptions=new[]{Ref("fixture.crafter")},proficiencySources=new[]{Ref("fixture.tool")},difficulty=10});
        await f.Component("activity.heal","dnd2024.activity.healing",new{amount=4,healingType=Ref("fixture.healing")});
        await f.Component("activity.heal","dnd2024.activity.cost",new{payments=new[]{new{resource=Ref("fixture.charge"),amount=1,timing="declaration"}}});
        await f.Membership("activity.weapon","activity.tool","activity.heal");
        await f.Component("item.first","dnd2024.activity.membership",new{activities=new[]{Ref("activity.weapon")}});
        var (revision,data)=await f.Run();Assert.Equal(3,Rows(data).Length);Assert.Equal("ready",data.GetProperty("uses").GetProperty("state").GetString());
        Assert.Contains("1 × d6 Bludgeoning damage",data.ToString());Assert.Contains("Fixture tool",data.ToString());Assert.Contains("4 Hit points healing",data.ToString());Assert.Contains("Charges (declaration)",data.ToString());
        Assert.All(Rows(data),row=>{Assert.Equal("not-evaluated",row.GetProperty("availability").GetString());Assert.Equal("unsupported",row.GetProperty("executionSupport").GetString());Assert.Equal("Canonical activity record",row.GetProperty("sources")[0].GetProperty("label").GetString());});
        Assert.Equal(revision,(await f.Run()).Item1);
    }
    [Fact]
    public async Task Learned_special_and_uncertain_statements_never_promote_hidden_activity_fields()
    {
        using var f=await Fixture.Create();await f.Activity("activity.hidden","SECRET TRUE PURPOSE",known:false);await f.Membership("activity.hidden");
        await f.Statement("fact.special","The staff opens the old gate.","item.first");
        await f.Statement("fact.rumour","It may reveal a hidden trail.","activity.hidden","suspected");
        var projection=await f.Resolve();Assert.True(projection.Ok);Assert.False(projection.Projection!.References.ContainsKey("activity.hidden"));
        var (_,data)=await f.Run();Assert.Equal(2,Rows(data).Length);Assert.DoesNotContain("SECRET",data.ToString());
        Assert.All(Rows(data),r=>{Assert.Equal("recorded-application",r.GetProperty("kind").GetString());Assert.Equal("adjudication-required",r.GetProperty("executionSupport").GetString());Assert.Empty(r.GetProperty("effects").EnumerateArray());});
        Assert.Contains(Rows(data),r=>r.GetProperty("knowledgeState").GetString()=="suspected");
        await f.Know("activity.hidden");Assert.Equal(3,Rows((await f.Run()).Item2).Length);
    }
    [Fact]
    public async Task Unknown_identity_and_unknown_labels_are_not_exposed_in_player_data()
    {
        using var f=await Fixture.Create(identify:false);await f.Activity("activity.weapon","HIDDEN ATTACK");await f.Membership("activity.weapon");
        var (_,hidden)=await f.Run();Assert.Empty(Rows(hidden));Assert.Equal("partial",hidden.GetProperty("uses").GetProperty("state").GetString());
        await f.Know("item.first");await f.Know("definition.shared");await f.Game.AddEntityAsync("fixture.private","PRIVATE RESOURCE");
        await f.Component("activity.weapon","dnd2024.activity.cost",new{payments=new[]{new{resource=Ref("fixture.private"),amount=2,timing="declaration"}}});
        var projection=await f.Resolve();Assert.False(projection.Projection!.References.ContainsKey("fixture.private"));
        var (_,safe)=await f.Run();Assert.DoesNotContain("PRIVATE RESOURCE",safe.ToString());Assert.DoesNotContain("fixture.private",safe.ToString());Assert.Contains("Supporting details unavailable",safe.ToString());
        var (_,dm)=await f.Run(perspective:"dm");Assert.Contains("PRIVATE RESOURCE",dm.ToString());
    }
    [Fact]
    public async Task Fixed_activity_stays_known_when_quantity_is_insufficient_and_viewing_changes_nothing()
    {
        using var f=await Fixture.Create();await f.Component("definition.shared","dnd2024.item-activity",new{activities=new[]{new{id="open",kind="consume-and-grant-item",consumeQuantity=3,grant=new{definitionId="definition.result",name="Prepared component",slot="pack"}}}});
        var (revision,data)=await f.Run(perspective:"dm");var row=Assert.Single(Rows(data));
        Assert.Equal("requirements-not-met",row.GetProperty("availability").GetString());Assert.Equal("supported",row.GetProperty("executionSupport").GetString());Assert.Equal(3,row.GetProperty("costs")[0].GetProperty("value").GetInt32());Assert.Contains("Prepared component",row.ToString());
        Assert.Equal(revision,(await f.Run(perspective:"dm")).Item1);
        Assert.Empty(Rows((await f.Run()).Item2));
    }
    [Fact]
    public async Task Empty_is_distinct_from_incomplete_and_changed_knowledge_rejects_continuation()
    {
        using var f=await Fixture.Create();Assert.Equal("empty",(await f.Run()).Item2.GetProperty("uses").GetProperty("state").GetString());
        for(var i=0;i<34;i++)await f.Statement($"fact.{i:00}","Recorded application "+i,"item.first");
        var (revision,data)=await f.Run();Assert.Equal(32,Rows(data).Length);Assert.Equal(32,data.GetProperty("uses").GetProperty("nextOffset").GetInt32());
        Assert.Equal(2,Rows((await f.Run(32,revision)).Item2).Length);
        await f.Game.Edges.SetRelationshipAsync(f.Game.Campaign,f.Game.Actor,"fact.33",f.Game.Binding.ExplicitStateRelationshipKind,"{\"stance\":\"unknown\"}",1);
        await f.Run(32,revision,expectFailure:true);
        await f.Game.AddEntityAsync("activity.incomplete","Incomplete activity");await f.Know("activity.incomplete");await f.Membership("activity.incomplete");
        var (_,partial)=await f.Run();Assert.Contains("definition-incomplete",partial.ToString());
    }
    [Fact]
    public async Task Multilingual_statements_return_a_fitting_advancing_prefix()
    {
        using var f=await Fixture.Create();for(var i=0;i<34;i++)await f.Statement($"fact.{i:00}",new string('界',1024),"item.first");
        var (revision,data)=await f.Run();Assert.True(System.Text.Encoding.UTF8.GetByteCount(data.GetRawText())<=65536);
        var next=data.GetProperty("uses").GetProperty("nextOffset").GetInt32();Assert.InRange(next,1,31);Assert.Contains("byte-limit",data.ToString());
        Assert.NotEmpty(Rows((await f.Run(next,revision)).Item2));
    }
    private static JsonElement[] Rows(JsonElement data)=>data.GetProperty("uses").GetProperty("entries").EnumerateArray().ToArray();
    private sealed class Policy(KnowledgeCoreTests.KnowledgeFixture game):IAuthorizedKnowledgeAudiencePolicy
    { public Task<KnowledgeAudienceResolution> ResolveAsync(string campaignId,CancellationToken cancellationToken=default)=>Task.FromResult(new KnowledgeAudienceResolution(new("principal",game.Campaign,KnowledgeAudienceRole.GameMaster,null,"policy"))); }
    private sealed class Binding(KnowledgeApplicationBinding binding):IKnowledgeApplicationBindingResolver
    { public Task<KnowledgeApplicationBinding?> ResolveAsync(string campaignId,CancellationToken cancellationToken=default)=>Task.FromResult<KnowledgeApplicationBinding?>(binding); }
    private sealed class Fixture:IDisposable
    {
        public KnowledgeCoreTests.KnowledgeFixture Game {get;}=new();
        private readonly MechanicRequirements req=MechanicRequirements.Parse(Mechanic.Requirements);
        private readonly Dictionary<string,string> types=[];
        private readonly ApplicationMechanicProjectionMapping mapping;
        private readonly ApplicationAuthorizedProjectionResolver resolver;
        private Fixture(){var components=new Dictionary<string,EcsComponentReference>();foreach(var id in req.AllComponentIds()){var type="fixture-knowledge.use-"+types.Count;types[id]=type;components[id]=Game.DefineComponent(type);} mapping=new(components,new Dictionary<string,string>());
            resolver=new(Game.Db,new Policy(Game),new Binding(Game.Binding),new ApplicationKnowledgeActorParticipationVerifier(Game.Entities,Game.Edges),Game.Source,Game.States);}
        public static async Task<Fixture> Create(bool identify=true){var f=new Fixture();await f.Game.AddCoreAsync();await f.Game.AddParticipationAsync();
            foreach(var (id,name) in new[]{("item.first","Carried item"),("definition.shared","Shared definition"),("fixture.tool","Fixture tool"),("fixture.crafter","Fixture training"),("fixture.day","Day")}) {await f.Game.AddEntityAsync(id,name);if(identify||id.StartsWith("fixture.")) await f.Know(id);}
            await f.Component("item.first","dnd2024.core.definition-link",new{definition=Ref("definition.shared")});await f.Component("item.first","dnd2024.item.quantity",new{current=2});
            await f.Game.Edges.MoveContainmentAsync(f.Game.Campaign,"item.first",f.Game.Actor,"pack",0);return f;}
        public Task Know(string id)=>Game.RelateAsync(Game.Actor,id,Game.Binding.ExplicitStateRelationshipKind,"{\"stance\":\"known\"}");
        public async Task Component(string id,string type,object value){var schema=Schemas.Compile(File.ReadAllText(Path.Combine(App,"components",type+".schema.json")));Assert.Equal(SchemaValueStatus.Valid,Schemas.Validate(schema.NormalizedSchema,Json(value)).Status);await Game.ComponentAsync(id,types[type],Json(value));}
        public async Task Activity(string id,string name,bool known=true) {
            await Game.AddEntityAsync(id,name);if(known)await Know(id);
            await Component(id,"dnd2024.core.presentation",new{summary=name+" purpose"});
            await Component(id,"dnd2024.activity.activation",new{economy="action"});
        }
        public Task Membership(params string[] ids)=>Component("definition.shared","dnd2024.activity.membership",new{activities=ids.Select(Ref)});
        public async Task Statement(string id,string prose,string subject,string stance="known") {
            await Game.AddKnowledgeAsync(id,prose,subject);
            await Game.RelateAsync(Game.Actor,id,Game.Binding.ExplicitStateRelationshipKind,Json(new{stance}));
        }
        public Task<ProjectionResult> Resolve(int offset=0,string? expected=null,string perspective="player")=>resolver.ResolveAsync(new(Game.Campaign,ApplicationIdentifier.Parse(Game.ApplicationId),Id,new string('A',64),mapping,
            new Dictionary<string,string>{{"subject",Game.Actor},{"campaign",Game.Campaign}},Json(new{itemId="item.first",offset=offset,expectedSourceRevision=expected}),0,Audience:new(perspective)),req);
        public async Task<(string,JsonElement)> Run(int offset=0,string? expected=null,bool expectFailure=false,string perspective="player"){var projection=await Resolve(offset,expected,perspective);
            if(expectFailure){Assert.Null(projection.Projection);Assert.Equal(["READ_MODEL_SOURCE_STALE"],projection.Problems);return ("",default);}
            Assert.True(projection.Ok,string.Join(';',projection.Problems));
            var run=await new JintMechanicEngine().RunAsync(Mechanic.Source,projection.Projection!,ExecutionLimits.Default);
            Assert.True(run.Ok,run.Error);Assert.Empty(run.Output.Effects);Assert.Empty(run.Output.Events);Assert.Empty(run.Output.Notifications);
            var valid=Schemas.Validate(Schemas.Compile(Query.OutputSchemaJson).NormalizedSchema,run.Output.Data);Assert.True(valid.Status==SchemaValueStatus.Valid,Json(valid));
            return (projection.Projection!.AuthorizedSourceRevision!,JsonDocument.Parse(run.Output.Data).RootElement.Clone());}
        public void Dispose()=>Game.Dispose();
    }
}
