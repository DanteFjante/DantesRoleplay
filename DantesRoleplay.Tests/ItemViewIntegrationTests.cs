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

public sealed class ItemViewIntegrationTests
{
    private static readonly string[] Tabs=["details","recipes","uses"];
    private static string Root {get{for(var d=new DirectoryInfo(AppContext.BaseDirectory);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"DantesRoleplay.slnx")))return d.FullName;throw new DirectoryNotFoundException();}}
    private static string App=>Path.Combine(Root,"catalog/applications/dnd2024");
    private static MechanicFile Mechanic(string tab){var id=$"dnd2024.mechanic.inventory-item-{tab}.project";return MechanicFile.Parse(File.ReadAllText(Path.Combine(App,"mechanics/data",id+".md")),id,File.ReadAllText(Path.Combine(App,"mechanics/data",id+".js")));}
    private static ApplicationQueryContract Query(string tab)=>ApplicationQueryContract.Parse(File.ReadAllText(Path.Combine(App,"queries/data",$"dnd2024.query.inventory-item-{tab}.json")),ApplicationIdentifier.Parse("dnd2024"));
    private static readonly BoundedJsonSchemaValidator Schemas=new();
    private static string Json(object v)=>JsonSerializer.Serialize(v);
    private static object Ref(string id)=>new{entityId=id};
    [Fact]
    public async Task All_three_authored_queries_preserve_Actor_and_GM_preview_parity_and_never_write()
    {
        using var f=await Fixture.Create();await f.KnownRecords();
        foreach(var tab in Tabs){f.Policy.IsDm=false;var actor=await f.Run(tab);f.Policy.IsDm=true;var preview=await f.Run(tab);Assert.Equal(actor.GetRawText(),preview.GetRawText());
            var first=await f.Resolve(tab);await f.Run(tab);Assert.Equal(first.Projection!.AuthorizedSourceRevision,(await f.Resolve(tab)).Projection!.AuthorizedSourceRevision);}
    }
    [Fact]
    public async Task Unidentified_magic_item_hides_identity_and_associations_in_every_player_tab()
    {
        using var f=await Fixture.Create();await f.KnownRecords();
        await f.Component("item.first","dnd2024.magic-item.knowledge",new{knowledgeRelationship=new{stateSpaceId=f.Game.Campaign,fromEntityId=f.Game.Actor,toEntityId="item.first",qualifiedKind=f.Game.Binding.ExplicitStateRelationshipKind},identityKnown=false,curseKnown=false});
        foreach(var tab in Tabs){var data=await f.Run(tab);Assert.DoesNotContain("PRIVATE",data.ToString());if(tab=="details"){Assert.Equal(JsonValueKind.Null,data.GetProperty("definitionId").ValueKind);Assert.Empty(data.GetProperty("media").EnumerateArray());}else {Assert.Empty(data.GetProperty("uses").GetProperty("entries").EnumerateArray());if(tab=="recipes")Assert.Empty(data.GetProperty("makes").GetProperty("entries").EnumerateArray());}}
        f.Policy.IsDm=true;Assert.Contains("PRIVATE",(await f.Run("details","dm")).ToString());Assert.Contains("PRIVATE RECIPE",(await f.Run("recipes","dm")).ToString());Assert.Contains("PRIVATE ACTIVITY",(await f.Run("uses","dm")).ToString());
    }
    [Fact]
    public async Task Shared_definition_does_not_share_learned_recipes_or_activities_between_observers()
    {
        using var f=await Fixture.Create();await f.KnownRecords();await f.SecondObserver();f.Policy.IsDm=true;
        Assert.Contains("PRIVATE RECIPE",(await f.Run("recipes")).ToString());Assert.Contains("PRIVATE ACTIVITY",(await f.Run("uses")).ToString());
        foreach(var tab in new[]{"recipes","uses"}){var second=await f.Run(tab,observer:"actor.second",item:"item.second");Assert.Equal("empty",second.GetProperty("uses").GetProperty("state").GetString());Assert.DoesNotContain("PRIVATE",second.ToString());}
    }
    [Fact]
    public async Task Edited_routes_and_transferred_possession_fail_closed_for_all_three_queries()
    {
        using var f=await Fixture.Create();await f.KnownRecords();await f.SecondObserver();
        foreach(var tab in Tabs){Assert.Equal(["READ_MODEL_FORBIDDEN"],(await f.Resolve(tab,"dm")).Problems);Assert.Equal(["READ_MODEL_FORBIDDEN"],(await f.Resolve(tab,observer:"actor.second")).Problems);Assert.Equal(["READ_MODEL_FORBIDDEN"],(await f.Resolve(tab,state:"wrong-state")).Problems);Assert.Equal(["READ_MODEL_SELECTION_UNAVAILABLE"],(await f.Resolve(tab,item:"not.carried")).Problems);}
        await f.Game.Edges.MoveContainmentAsync(f.Game.Campaign,"item.first","actor.second","pack",1);
        foreach(var tab in Tabs){var denied=await f.Resolve(tab);Assert.False(denied.Ok);Assert.Null(denied.Projection);Assert.Equal(["READ_MODEL_SELECTION_UNAVAILABLE"],denied.Problems);}
    }
    private sealed class Policy(KnowledgeCoreTests.KnowledgeFixture game):IAuthorizedKnowledgeAudiencePolicy
    { public bool IsDm; public Task<KnowledgeAudienceResolution> ResolveAsync(string campaignId,CancellationToken cancellationToken=default)=>Task.FromResult(new KnowledgeAudienceResolution(new("principal",game.Campaign,IsDm?KnowledgeAudienceRole.GameMaster:KnowledgeAudienceRole.Actor,IsDm?null:game.Actor,"policy"))); }
    private sealed class Binding(KnowledgeApplicationBinding binding):IKnowledgeApplicationBindingResolver
    { public Task<KnowledgeApplicationBinding?> ResolveAsync(string campaignId,CancellationToken cancellationToken=default)=>Task.FromResult<KnowledgeApplicationBinding?>(binding); }
    private sealed class Fixture:IDisposable
    {
        public KnowledgeCoreTests.KnowledgeFixture Game {get;}=new();
        public Policy Policy {get;}
        private readonly Dictionary<string,string> types=[];
        private readonly ApplicationMechanicProjectionMapping mapping;
        private readonly ApplicationAuthorizedProjectionResolver resolver;
        private Fixture(){Policy=new(Game);var components=new Dictionary<string,EcsComponentReference>();foreach(var id in Tabs.SelectMany(t=>MechanicRequirements.Parse(Mechanic(t).Requirements).AllComponentIds()).Distinct()){var type="fixture-knowledge.use-"+types.Count;types[id]=type;components[id]=Game.DefineComponent(type);} mapping=new(components,new Dictionary<string,string>());
            resolver=new(Game.Db,Policy,new Binding(Game.Binding),new ApplicationKnowledgeActorParticipationVerifier(Game.Entities,Game.Edges),Game.Source,Game.States);}
        public static async Task<Fixture> Create(bool identify=true){var f=new Fixture();await f.Game.AddCoreAsync();await f.Game.AddParticipationAsync();
            foreach(var (id,name) in new[]{("item.first","PRIVATE ITEM"),("definition.shared","PRIVATE DEFINITION"),("fixture.tool","Fixture tool"),("fixture.crafter","Fixture training"),("fixture.day","Day")}) {await f.Game.AddEntityAsync(id,name);if(identify||id.StartsWith("fixture.")) await f.Know(id);}
            await f.Component("definition.shared","dnd2024.core.version",new{revision=1,status="active"});
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

        public async Task KnownRecords() {
            await Activity("activity.private","PRIVATE ACTIVITY");await Membership("activity.private");
            await Game.AddEntityAsync("recipe.private","PRIVATE RECIPE NAME");
            await Component("recipe.private","dnd2024.crafting.recipe",new{outputs=new[]{new{definition=Ref("definition.shared"),quantity=1}},materialRequirements=new[]{new{definition=Ref("definition.shared"),quantity=1}},
                workDuration=new{kind="measured",amount=1,unit=Ref("fixture.day")},toolRequirement=new{@operator="predicate",predicateId="predicate.proficiency.tool",arguments=new[]{Ref("fixture.tool")}},crafterRequirement=new{@operator="predicate",predicateId="predicate.proficiency.crafter",arguments=new[]{Ref("fixture.crafter")}}});
            await Statement("fact.recipe","PRIVATE RECIPE INSTRUCTIONS","recipe.private");
        }
        public async Task SecondObserver() {
            await Game.AddEntityAsync("actor.second","Second observer");await Game.AddEntityAsync("participation.second","Second membership");
            await Game.ComponentAsync("participation.second",Game.Binding.ParticipationComponentTypeId,"{\"state\":\"ready\"}");
            await Game.RelateAsync(Game.Campaign,"participation.second",Game.Binding.CampaignParticipationRelationshipKind,"{}");await Game.RelateAsync("participation.second","actor.second",Game.Binding.ParticipationActorRelationshipKind,"{}");
            await Game.AddEntityAsync("item.second","Second item");await Component("item.second","dnd2024.core.definition-link",new{definition=Ref("definition.shared")});await Component("item.second","dnd2024.item.quantity",new{current=1});
            await Game.Edges.MoveContainmentAsync(Game.Campaign,"item.second","actor.second","pack",0);
            foreach(var id in new[]{"item.second","definition.shared"})await Game.RelateAsync("actor.second",id,Game.Binding.ExplicitStateRelationshipKind,"{\"stance\":\"known\"}");
        }
        public Task<ProjectionResult> Resolve(string tab,string perspective="player",string? observer=null,string? item=null,string? state=null) {
            var mechanic=Mechanic(tab);var input=new Dictionary<string,object?>{{"itemId",item??"item.first"}};
            if(tab!="details")input["expectedSourceRevision"]=null;
            if(tab=="recipes"){input["makesOffset"]=0;input["usesOffset"]=0;}else if(tab=="uses")input["offset"]=0;
            return resolver.ResolveAsync(new(state??Game.Campaign,ApplicationIdentifier.Parse(Game.ApplicationId),mechanic.Id,new string('A',64),mapping,
                new Dictionary<string,string>{{"subject",observer??Game.Actor},{"campaign",Game.Campaign}},Json(input),0,Audience:new(perspective)),MechanicRequirements.Parse(mechanic.Requirements));
        }
        public async Task<JsonElement> Run(string tab,string perspective="player",string? observer=null,string? item=null) {
            var before=Game.Db.ChangeTracker.Entries().Count();var projection=await Resolve(tab,perspective,observer,item);Assert.True(projection.Ok,string.Join(';',projection.Problems));
            var run=await new JintMechanicEngine().RunAsync(Mechanic(tab).Source,projection.Projection!,ExecutionLimits.Default);Assert.True(run.Ok,run.Error);
            Assert.Empty(run.Output.Effects);Assert.Empty(run.Output.Events);Assert.Empty(run.Output.Notifications);
            var schema=Schemas.Compile(Query(tab).OutputSchemaJson);var valid=Schemas.Validate(schema.NormalizedSchema,run.Output.Data);Assert.True(valid.Status==SchemaValueStatus.Valid,Json(valid));
            Assert.Equal(before,Game.Db.ChangeTracker.Entries().Count());Assert.DoesNotContain(Game.Db.ChangeTracker.Entries(),e=>e.State is Microsoft.EntityFrameworkCore.EntityState.Added or Microsoft.EntityFrameworkCore.EntityState.Modified or Microsoft.EntityFrameworkCore.EntityState.Deleted);
            return JsonDocument.Parse(run.Output.Data).RootElement.Clone();
        }
        public void Dispose()=>Game.Dispose();
    }
}
