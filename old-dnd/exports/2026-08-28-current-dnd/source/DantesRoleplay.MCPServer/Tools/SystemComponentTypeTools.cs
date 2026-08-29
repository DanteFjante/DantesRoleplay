using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.ComponentTypeAdministration;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

internal sealed class SystemComponentTypeTools
{
    private const string Kind = "system.component-type.register";
    public async Task<ToolEnvelope> RegisterAsync(IComponentTypeAdministrationService? service, IPrivateOperatorRequestAuthorizer? authorization, IOperationLog log, string payload, string intent, string[]? procedures, bool dryRun, CancellationToken cancellationToken)
    {
        var decision = authorization?.Authorize(PrivateOperatorCapability.Modify) ?? new PrivateOperatorAuthorizationPolicy().Evaluate(new(TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED"), PrivateOperatorCapability.Modify, PrivateOperatorAuthorizationPolicy.PrivateHostScope, "mcp-request"));
        if (!decision.Allowed) return await Fail(log, decision, decision.Code, "Private-operator authentication is required before component-type registration.", intent, procedures);
        if (service is null) return await Fail(log, decision, "COMPONENT_TYPE_ADMINISTRATION_UNAVAILABLE", "Component-type administration is not configured.", intent, procedures);
        try
        {
            using var document = JsonDocument.Parse(payload); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ComponentTypeAdministrationException("INVALID_PAYLOAD", "payload must be a JSON object with the documented closed component-type shape.");
            var names = root.EnumerateObject().Select(x => x.Name).ToArray(); var required = new[] { "requestToken", "applicationId", "qualifiedTypeId", "schemaJson", "expectedSchemaHash" };
            if (names.Length != required.Length || names.Distinct().Count() != names.Length || names.Except(required).Any() || required.Except(names).Any()) throw new ComponentTypeAdministrationException("INVALID_PAYLOAD", "payload must contain exactly: requestToken, applicationId, qualifiedTypeId, schemaJson, expectedSchemaHash.");
            string Text(string key, int max, bool empty = false) { var e=root.GetProperty(key); if(e.ValueKind!=JsonValueKind.String || (!empty && string.IsNullOrWhiteSpace(e.GetString())) || e.GetString()!.Length>max) throw new ComponentTypeAdministrationException("INVALID_PAYLOAD", $"{key} must be a bounded string."); return e.GetString()!; }
            var expectedElement=root.GetProperty("expectedSchemaHash"); var expected=expectedElement.ValueKind==JsonValueKind.Null ? null : Text("expectedSchemaHash",64);
            var request = new ComponentTypeRegistrationRequest(ApplicationIdentifier.Parse(Text("applicationId",63)), Text("qualifiedTypeId",200), Text("schemaJson",65536,true));
            var context = new ComponentTypeAdministrationContext(Text("requestToken",32), expected, intent, Array.AsReadOnly((procedures??[]).ToArray()), decision.Evidence);
            if(dryRun) { var p=await service.PreviewAsync(request,context,cancellationToken); return ToolEnvelope.Success(Data(true,context.RequestToken,p.Outcome,p.ComponentType),p.OperationId,Call(payload,true)); }
            var r=await service.RegisterAsync(request,context,cancellationToken); return ToolEnvelope.Success(Data(false,context.RequestToken,r.Outcome,r.ComponentType),r.OperationId,"query(kind: \"capabilities\")");
        }
        catch (JsonException) { return await Fail(log,decision,"INVALID_PAYLOAD","payload must be valid JSON with the documented closed component-type shape.",intent,procedures); }
        catch (ComponentTypeAdministrationException e) { return await Fail(log,decision,e.Code,e.Message,intent,procedures,e.Code is "DRY_RUN_REQUIRED" or "REGISTRATION_STALE" ? Call(payload,true) : VerbSurface.CommitCall(Kind,true)); }
        catch (ArgumentException e) { return await Fail(log,decision,"INVALID_PAYLOAD",e.Message,intent,procedures); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return await Fail(log, decision, "COMPONENT_TYPE_REGISTER_FAILED", "Component-type registration could not be completed.", intent, procedures); }
    }
    private static object Data(bool dryRun,string token,string outcome,DantesRoleplay.Ecs.RegisteredComponentTypeVersion type)=>new { DryRun=dryRun, RequestToken=token, Outcome=outcome, ComponentType=new { ApplicationId=type.Owner.Value,type.QualifiedId,type.Version,type.ProfileId,type.SchemaHash } };
    private static Task<ToolEnvelope> Fail(IOperationLog log,PrivateOperatorAuthorizationDecision decision,string code,string why,string intent,string[]? procedures,string? fix=null)=>ToolRunner.RunAsync(log,"commit",intent,$"commit:{Kind}",procedures,()=>Task.FromResult(new ToolOutcome(null,$"Rejected {Kind}: {code}.",[fix??VerbSurface.CommitCall(Kind,true)],new(code,why,fix??VerbSurface.CommitCall(Kind,true)),GuardEvidenceJson:JsonSerializer.Serialize(decision.Evidence))),false);
    private static string Call(string payload,bool dryRun)=>$"commit(kind: \"{Kind}\", payload: {JsonSerializer.Serialize(payload)}"+(dryRun?", dryRun: true)":")");
}
