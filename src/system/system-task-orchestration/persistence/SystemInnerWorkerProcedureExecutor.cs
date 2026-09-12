using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>Plan-05 callback for plan-04's sole purpose-filtered ProcedureWorkflow runner.</summary>
internal sealed class SystemInnerWorkerProcedureExecutor(
    DantesRoleplayDbContext db,
    SqliteSystemTaskDurableService durable,
    SystemInnerWorkerProcedureResolver resolver,
    SystemInnerWorkerProcedureInvoker invoker,
    SystemTaskAiInvocationLifecycleFactory lifecycles,
    TimeProvider time)
{
    internal async Task<SystemTaskRunOutcome> ExecuteLeaseAsync(SystemTaskLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.Request.Purpose != SystemTaskPurpose.ProcedureWorkflow
            || lease.Request.SelectedDefinition is null)
            return Failed("INNER_WORKER_LEASE_SCOPE_INVALID", "The leased task is not a procedure workflow.");
        try
        {
            SystemInnerWorkerRetainedAdmission? admission;
            await using (var boundary = await SystemTaskValidationTransaction.OpenAsync(db, time, false, cancellationToken))
                admission = await boundary.Store.ReadInnerWorkerAdmissionAsync(lease.Request.Handle,
                    boundary.Connection, boundary.Transaction, cancellationToken);
            if (admission is null)
                return Failed("INNER_WORKER_ADMISSION_UNAVAILABLE",
                    "The focused worker's exact retained admission cannot be verified.");

            var host = RehydrateHost(lease.Request.Invocation);
            var worker = new SystemInnerWorkerRequest(host, lease.Request.WorkflowDefinition,
                lease.Request.InputJson, admission.ResultSchemaJson, lease.Request.Dependencies,
                admission.DependencyInputs);
            var preparation = await resolver.ResolveAsync(worker, cancellationToken);
            SqliteSystemTaskDurableService.InnerWorkerAuthorityResolution authority;
            await using (var boundary = await SystemTaskValidationTransaction.OpenAsync(db, time, false, cancellationToken))
                authority = await durable.ResolveCurrentInnerWorkerProfileAsync(preparation, lease.Request, cancellationToken);
            if (authority.Failure is { } failure)
                return Failed(failure.Code, failure.SafeMessage);
            var profile = authority.Profile!;
            var dependencies = await durable.ResolveInnerWorkerDependenciesAsync(
                host, worker.DependencyHandles, worker.DependencyInputs, cancellationToken);
            var prepared = SystemInnerWorkerPreparation.AddPrerequisites(preparation.Prepared, dependencies);
            var invocation = await lifecycles.CreateProcedureAsync(
                lease, profile, preparation.ToolContext, cancellationToken);
            var response = await invoker.InvokeAsync(profile, prepared,
                preparation.ToolContext, invocation.Lifecycle, invocation.WriteApproval,
                preparation.ApplicationTools, cancellationToken);
            var evidence = SystemInnerWorkerCompletionEvidence.For(lease);
            var mapped = SystemInnerWorkerResultAdapter.MapStoredResult(worker, response,
                lease.Request.Handle, evidence, currentInvocationEvidenceVerified: true);
            if (mapped.Tag != InteractionInvocationResultTag.Completed || mapped.DataJson is null
                || mapped.CompletionEvidenceReference is null)
                return Failed(mapped.Code, mapped.SafeMessage);
            return new SystemTaskRunOutcome.Completed(new(mapped.DataJson,
                mapped.CompletionEvidenceReference,
                [profile.ManualContext.Reference]));
        }
        catch (AiLifecycleException error)
        {
            return Failed(error.Code, error.Message);
        }
        catch (InteractionTaskContextException error)
        {
            return Failed(error.Code, error.Message);
        }
        catch (InteractionContractException error)
        {
            return Failed(error.Code, error.Message);
        }
    }

    private static InteractionInvocationHost RehydrateHost(SystemTaskStoredInvocation invocation)
    {
        var bases = JsonSerializer.Deserialize<string[]>(invocation.BaseApplicationsJson)
            ?? throw new InvalidDataException("The retained application bases are invalid.");
        return new(TrustedPrincipalContext.VerifiedPrincipal(invocation.PrincipalReference, invocation.AuthenticationMethod),
            new ApplicationRevision(ApplicationIdentifier.Parse(invocation.ApplicationId), invocation.ApplicationRevision,
                invocation.ApplicationFingerprint, bases.Select(ApplicationIdentifier.Parse).ToArray()),
            invocation.StateSpaceId ?? throw new InvalidDataException("The retained procedure task has no state scope."),
            invocation.GrantReference, invocation.CommandId,
            invocation.StateRevision ?? throw new InvalidDataException("The retained procedure task has no state revision."),
            invocation.Profile, new InteractionInvocationBudget(invocation.AdmittedOperations, invocation.DeadlineUtc),
            invocation.ParentCommandId);
    }

    private static SystemTaskRunOutcome.Failed Failed(string code, string message) =>
        new(SystemTaskFailureKind.Permanent, code, message);
}

internal static class SystemInnerWorkerCompletionEvidence
{
    internal static string For(SystemTaskLease lease) => "inner-result." +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            lease.Request.Handle.TaskId + "\n" + lease.Attempt.AttemptId + "\n"
            + lease.Attempt.FencingCounter)));
}
