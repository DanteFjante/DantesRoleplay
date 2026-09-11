using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed record SystemTaskValidationAuthority(
    InteractionInvocationResult? Failure,
    ApplicationCandidateSelectionEvidence? Selection = null,
    StandingGrantRevision? ReadGrant = null,
    StandingGrantRevision? ValidateGrant = null,
    ApplicationCandidateCausationEvidence? Causation = null);

/// <summary>Rehydrates actual owner evidence and independently evaluates current application grants.</summary>
internal sealed class SystemTaskApplicationValidationGate(
    DantesRoleplayDbContext db, IApplicationRegistry applications, IApplicationActivationReader activations,
    IStandingGrantTargetResolver targets, IStandingGrantPolicy policy, TimeProvider time)
{
    internal async Task<SystemTaskValidationAuthority> CheckAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, bool requireValidate,
        string? causationOperationId = null, string? causalCommandId = null,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Validation authority must join the owner's transaction.");
        if (host.StateSpaceId is not null || host.StateRevision is not null
            || candidate.ApplicationId != host.ApplicationRevision.ApplicationId || candidate.ApplicationId.IsSystem)
            return Failed("INNER_VALIDATION_SCOPE_INVALID", "Validation requires its exact application scope.");
        if (host.Budget.DeadlineUtc <= time.GetUtcNow().UtcDateTime)
            return Unavailable("INNER_VALIDATION_DEADLINE_EXPIRED", "The validation deadline has elapsed.");
        var current = applications.Get(candidate.ApplicationId);
        if (current is null || current.Revision != host.ApplicationRevision.Revision
            || current.Fingerprint != host.ApplicationRevision.Fingerprint
            || !current.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications))
            return Failed("INNER_VALIDATION_APPLICATION_STALE", "The application revision is no longer current.");
        var selection = await new ApplicationCandidateSelectionReader(db, applications, activations, targets)
            .ReadAsync(host, candidate, cancellationToken);
        if (selection is null || selection.Candidate != candidate || selection.Targets.IsDefaultOrEmpty
            || selection.Targets.Any(target => target.Candidate != candidate
                || target.OwnerApplicationId != candidate.ApplicationId || target.RetainedActivation is not null))
            return Unavailable("INNER_VALIDATION_SELECTION_UNAVAILABLE", "Exact candidate selection evidence is unavailable.");

        var read = await policy.EvaluateAsync(host,
            new(StandingGrantCapability.Read, StandingGrantScope.Application, selection.Targets, []), cancellationToken);
        if (!Matches(read, host, StandingGrantCapability.Read)) return Denied(read);
        StandingGrantRevision? validateGrant = null;
        if (requireValidate)
        {
            var validate = await policy.EvaluateAsync(host,
                new(StandingGrantCapability.Validate, StandingGrantScope.Application, selection.Targets, []), cancellationToken);
            if (!Matches(validate, host, StandingGrantCapability.Validate)) return Denied(validate);
            validateGrant = validate.Grant;
            if (validateGrant!.GrantReference != read.Grant!.GrantReference
                || validateGrant.Revision != read.Grant.Revision
                || validateGrant.ContentFingerprint != read.Grant.ContentFingerprint)
                return Unavailable("INNER_VALIDATION_AUTHORITY_CHANGED", "Validation authority changed during the check.");
        }

        ApplicationCandidateCausationEvidence? causation = null;
        if ((causationOperationId is null) != (causalCommandId is null))
            return Failed("INNER_VALIDATION_CAUSATION_INVALID", "Causation requires both its operation and causal command.");
        if (causationOperationId is not null)
        {
            causation = await new ApplicationCandidateCausationReader(db, applications, activations)
                .ReadAsync(candidate, causalCommandId!, causationOperationId, cancellationToken);
            if (causation is null || causation.CandidateRef != candidate
                || causation.OperationId != causationOperationId || causation.CausalCommandId != causalCommandId)
                return Unavailable("INNER_VALIDATION_CAUSATION_UNAVAILABLE", "The exact causal authoring receipt could not be verified.");
        }
        return new(null, selection, read.Grant, validateGrant, causation);
    }

    /// <summary>
    /// A selected-document DTO, narrower sample closure, or Create(material, true) cannot satisfy
    /// this gate. The broader owner proof and its V2/manual/profile binding are still required.
    /// </summary>
    internal static InteractionInvocationResult? ExecutionPrerequisite(SystemTaskValidationAuthority authority)
    {
        if (authority.Failure is not null) return authority.Failure;
        if (authority.Selection?.DependenciesComplete != true)
            return InteractionInvocationResult.Unavailable("INNER_VALIDATION_DEPENDENCIES_UNAVAILABLE",
                "Complete candidate dependency and sidecar coverage is required before durable validation admission.");
        return InteractionInvocationResult.Unavailable("INNER_VALIDATION_CONTEXT_BINDING_UNAVAILABLE",
            "Owner-verified selected-source, manual-context, and reviewer-profile bindings are required.");
    }

    private bool Matches(StandingGrantDecision decision, InteractionInvocationHost host, StandingGrantCapability capability) =>
        decision.Allowed && decision.Grant is { } grant && !grant.Revoked
        && grant.GrantReference == host.GrantReference && grant.PrincipalReference == host.Principal.PrincipalId
        && grant.ApplicationId == host.ApplicationRevision.ApplicationId && grant.Scope == StandingGrantScope.Application
        && grant.StateSpaceId is null && grant.Capabilities.Contains(capability)
        && grant.ExpiresAtUtc > time.GetUtcNow().UtcDateTime;

    private static SystemTaskValidationAuthority Denied(StandingGrantDecision decision) =>
        decision.Code.Contains("UNAVAILABLE", StringComparison.Ordinal)
            ? Unavailable("INNER_VALIDATION_AUTHORITY_UNAVAILABLE", "Current application authority is unavailable.")
            : Failed("INNER_VALIDATION_NOT_AUTHORIZED", "Current application authority does not permit this validation operation.");
    private static SystemTaskValidationAuthority Failed(string code, string message) => new(InteractionInvocationResult.Failed(code, message));
    private static SystemTaskValidationAuthority Unavailable(string code, string message) => new(InteractionInvocationResult.Unavailable(code, message));
}
