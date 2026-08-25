using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.Interactions;

public enum InteractionCapability
{
    Plan,
    Execute,
    ReadReceipt
}

public enum InteractionPlannerPreference
{
    Automatic,
    Local,
    Remote
}

public sealed record InteractionAuthorizationRequest(
    TrustedPrincipalContext Principal,
    ApplicationIdentifier ApplicationId,
    string StateSpaceId,
    InteractionCapability Capability,
    string CorrelationId);

public sealed record InteractionAuthorizationDecision
{
    private InteractionAuthorizationDecision(
        bool allowed,
        string code,
        string principalReference,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        InteractionCapability capability,
        string evidenceReference)
    {
        Allowed = allowed;
        Code = code;
        PrincipalReference = principalReference;
        ApplicationId = applicationId;
        StateSpaceId = stateSpaceId;
        Capability = capability;
        EvidenceReference = evidenceReference;
    }

    public bool Allowed { get; }
    public string Code { get; }
    public string PrincipalReference { get; }
    public ApplicationIdentifier ApplicationId { get; }
    public string StateSpaceId { get; }
    public InteractionCapability Capability { get; }
    public string EvidenceReference { get; }

    public static InteractionAuthorizationDecision Allow(InteractionAuthorizationRequest request, string evidenceReference)
    {
        ValidateRequest(request);
        if (!request.Principal.Verified)
            throw new InteractionContractException("UNVERIFIED_PRINCIPAL", "An allowed decision requires a verified principal.");
        return new(true, "INTERACTION_ALLOWED", request.Principal.PrincipalId, request.ApplicationId,
            request.StateSpaceId, request.Capability, InteractionGuard.Identifier(evidenceReference, nameof(evidenceReference)));
    }

    public static InteractionAuthorizationDecision Deny(InteractionAuthorizationRequest request, string code, string evidenceReference)
    {
        ValidateRequest(request);
        return new(false, InteractionGuard.Identifier(code, nameof(code)), request.Principal.PrincipalId,
            request.ApplicationId, request.StateSpaceId, request.Capability,
            InteractionGuard.Identifier(evidenceReference, nameof(evidenceReference)));
    }

    private static void ValidateRequest(InteractionAuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Principal);
        ArgumentNullException.ThrowIfNull(request.ApplicationId);
        InteractionGuard.Identifier(request.StateSpaceId, nameof(request.StateSpaceId));
        InteractionGuard.Identifier(request.CorrelationId, nameof(request.CorrelationId));
        if (!Enum.IsDefined(request.Capability))
            throw new InteractionContractException("INVALID_CAPABILITY", "The interaction capability is not supported.");
    }
}

public interface IInteractionAuthorizationPolicy
{
    InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request);
}

public enum InteractionAiRole
{
    Inner,
    Outer
}

public sealed record InteractionRoleProfile
{
    private InteractionRoleProfile(InteractionAiRole role, string model, string reasoningEffort)
    {
        Role = role;
        Model = model;
        ReasoningEffort = reasoningEffort;
    }

    public InteractionAiRole Role { get; }
    public string Model { get; }
    public string ReasoningEffort { get; }
    public string StableKey => $"{Role.ToString().ToLowerInvariant()}:{Model}:{ReasoningEffort}";

    public static InteractionRoleProfile Inner { get; } = new(InteractionAiRole.Inner, "gpt-5.6-luna", "low");
    public static InteractionRoleProfile Outer { get; } = new(InteractionAiRole.Outer, "gpt-5.6-luna", "high");

    public static InteractionRoleProfile For(InteractionAiRole role) => role switch
    {
        InteractionAiRole.Inner => Inner,
        InteractionAiRole.Outer => Outer,
        _ => throw new InteractionContractException("INVALID_AI_ROLE", "The interaction AI role is not supported.")
    };

    public static void EnsureResumeCompatible(InteractionRoleProfile expected, InteractionRoleProfile candidate)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!string.Equals(expected.StableKey, candidate.StableKey, StringComparison.Ordinal))
            throw new InteractionContractException("ROLE_PROFILE_CHANGED", "An interaction cannot resume under a different role, model, or reasoning effort.");
    }
}

public sealed record InteractionBudgets
{
    public InteractionBudgets(int maximumPlanSteps, int maximumObservationBytes, int maximumModelOutputBytes)
    {
        if (maximumPlanSteps is < 1 or > InteractionContractLimits.ProposalSteps)
            throw new InteractionContractException("INVALID_PLAN_BUDGET", "The plan-step budget is outside the closed limit.");
        if (maximumObservationBytes is < 1 or > InteractionContractLimits.JsonBytes
            || maximumModelOutputBytes is < 1 or > InteractionContractLimits.JsonBytes)
            throw new InteractionContractException("INVALID_BYTE_BUDGET", "The observation/model byte budget is outside the closed limit.");
        MaximumPlanSteps = maximumPlanSteps;
        MaximumObservationBytes = maximumObservationBytes;
        MaximumModelOutputBytes = maximumModelOutputBytes;
    }

    public int MaximumPlanSteps { get; }
    public int MaximumObservationBytes { get; }
    public int MaximumModelOutputBytes { get; }
}

public sealed record InteractionHostContext
{
    public InteractionHostContext(
        TrustedPrincipalContext principal,
        ApplicationRevision applicationRevision,
        string stateSpaceId,
        string sessionContextId,
        string stateRevision,
        string effectiveSetFingerprint,
        InteractionRoleProfile roleProfile,
        InteractionBudgets budgets,
        InteractionAuthorizationDecision authorization,
        string? conversationId = null,
        string? parentDelegationId = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(applicationRevision);
        ArgumentNullException.ThrowIfNull(roleProfile);
        ArgumentNullException.ThrowIfNull(budgets);
        ArgumentNullException.ThrowIfNull(authorization);
        if (!principal.Verified) throw new InteractionContractException("UNVERIFIED_PRINCIPAL", "A host context requires a verified principal.");
        if (applicationRevision.Revision < 1) throw new InteractionContractException("INVALID_APPLICATION_REVISION", "The application revision must be positive.");
        if (applicationRevision.BaseApplications is null) throw new InteractionContractException("INVALID_APPLICATION_REVISION", "Base application revisions are required.");
        InteractionGuard.UpperSha256(applicationRevision.Fingerprint, nameof(applicationRevision.Fingerprint));
        if (!authorization.Allowed || authorization.Capability != InteractionCapability.Plan)
            throw new InteractionContractException("PLAN_NOT_AUTHORIZED", "The host context requires an allowed plan decision.");
        if (authorization.PrincipalReference != principal.PrincipalId
            || authorization.ApplicationId != applicationRevision.ApplicationId
            || authorization.StateSpaceId != stateSpaceId)
            throw new InteractionContractException("AUTHORIZATION_SCOPE_MISMATCH", "Authorization evidence does not match the host-owned scope.");

        Principal = principal;
        ApplicationRevision = applicationRevision with
        {
            BaseApplications = Array.AsReadOnly(applicationRevision.BaseApplications.ToArray())
        };
        StateSpaceId = InteractionGuard.Identifier(stateSpaceId, nameof(stateSpaceId));
        SessionContextId = InteractionGuard.Identifier(sessionContextId, nameof(sessionContextId));
        StateRevision = InteractionGuard.Identifier(stateRevision, nameof(stateRevision));
        EffectiveSetFingerprint = InteractionGuard.UpperSha256(effectiveSetFingerprint, nameof(effectiveSetFingerprint));
        RoleProfile = roleProfile;
        Budgets = budgets;
        Authorization = authorization;
        ConversationId = InteractionGuard.OptionalBounded(conversationId, InteractionContractLimits.Identifier, "INVALID_CONVERSATION_ID", nameof(conversationId));
        ParentDelegationId = InteractionGuard.OptionalBounded(parentDelegationId, InteractionContractLimits.Identifier, "INVALID_DELEGATION_ID", nameof(parentDelegationId));
    }

    public TrustedPrincipalContext Principal { get; }
    public ApplicationRevision ApplicationRevision { get; }
    public string StateSpaceId { get; }
    public string SessionContextId { get; }
    public string StateRevision { get; }
    public string EffectiveSetFingerprint { get; }
    public InteractionRoleProfile RoleProfile { get; }
    public InteractionBudgets Budgets { get; }
    public InteractionAuthorizationDecision Authorization { get; }
    public string? ConversationId { get; }
    public string? ParentDelegationId { get; }
}

public sealed record InteractionIntent
{
    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "conversationFactReferences", "idempotencyKey", "intentText", "maximumPlanSteps", "plannerPreference", "roleHints"
    };

    private InteractionIntent(
        string idempotencyKey,
        string intentText,
        IReadOnlyDictionary<string, string> roleHints,
        IReadOnlyList<string> conversationFactReferences,
        int maximumPlanSteps,
        InteractionPlannerPreference plannerPreference)
    {
        IdempotencyKey = idempotencyKey;
        IntentText = intentText;
        RoleHints = roleHints;
        ConversationFactReferences = conversationFactReferences;
        MaximumPlanSteps = maximumPlanSteps;
        PlannerPreference = plannerPreference;
    }

    public string IdempotencyKey { get; }
    public string IntentText { get; }
    public IReadOnlyDictionary<string, string> RoleHints { get; }
    public IReadOnlyList<string> ConversationFactReferences { get; }
    public int MaximumPlanSteps { get; }
    public InteractionPlannerPreference PlannerPreference { get; }

    public static InteractionIntent Parse(string json)
    {
        var canonical = InteractionCanonicalJson.CanonicalizeObject(json);
        using var document = JsonDocument.Parse(canonical);
        var properties = document.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
        var forbidden = properties.Keys.Where(x => !AllowedProperties.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (forbidden.Length != 0)
            throw new InteractionContractException("CALLER_AUTHORITY_FORBIDDEN", $"Caller field '{forbidden[0]}' is not part of the closed intent contract.");

        var key = InteractionGuard.IdempotencyKey(RequiredString(properties, "idempotencyKey"));
        var intent = InteractionGuard.Bounded(RequiredString(properties, "intentText"), InteractionContractLimits.IntentText,
            "INVALID_INTENT_TEXT", "intentText");
        var maximum = InteractionContractLimits.ProposalSteps;
        if (properties.TryGetValue("maximumPlanSteps", out var maximumElement))
        {
            if (maximumElement.ValueKind != JsonValueKind.Number || !maximumElement.TryGetInt32(out maximum))
                throw new InteractionContractException("INVALID_MAXIMUM_PLAN_STEPS", "maximumPlanSteps must be an integer.");
        }
        if (maximum is < 1 or > InteractionContractLimits.ProposalSteps)
            throw new InteractionContractException("INVALID_MAXIMUM_PLAN_STEPS", "maximumPlanSteps is outside the closed limit.");

        var preference = InteractionPlannerPreference.Automatic;
        if (properties.TryGetValue("plannerPreference", out var preferenceElement))
        {
            if (preferenceElement.ValueKind != JsonValueKind.String)
                throw new InteractionContractException("INVALID_PLANNER_PREFERENCE", "plannerPreference must be a closed string value.");
            preference = preferenceElement.GetString() switch
            {
                "automatic" => InteractionPlannerPreference.Automatic,
                "local" => InteractionPlannerPreference.Local,
                "remote" => InteractionPlannerPreference.Remote,
                _ => throw new InteractionContractException("INVALID_PLANNER_PREFERENCE", "plannerPreference is not supported.")
            };
        }

        var hints = new Dictionary<string, string>(StringComparer.Ordinal);
        if (properties.TryGetValue("roleHints", out var hintsElement))
        {
            if (hintsElement.ValueKind != JsonValueKind.Object)
                throw new InteractionContractException("INVALID_ROLE_HINTS", "roleHints must be an object.");
            foreach (var item in hintsElement.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.String)
                    throw new InteractionContractException("INVALID_ROLE_HINTS", "Every role hint must be a string reference.");
                hints.Add(item.Name, item.Value.GetString()!);
            }
        }

        var facts = Array.Empty<string>();
        if (properties.TryGetValue("conversationFactReferences", out var factsElement))
        {
            if (factsElement.ValueKind != JsonValueKind.Array || factsElement.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
                throw new InteractionContractException("INVALID_CONVERSATION_FACTS", "Conversation facts must be string references.");
            facts = factsElement.EnumerateArray().Select(x => x.GetString()!).ToArray();
        }

        return new(key, intent,
            InteractionGuard.CopyMap(hints, InteractionContractLimits.RoleHints, "INVALID_ROLE_HINTS"),
            InteractionGuard.CopyDistinctList(facts, InteractionContractLimits.ConversationFacts, "INVALID_CONVERSATION_FACTS"),
            maximum, preference);
    }

    private static string RequiredString(IReadOnlyDictionary<string, JsonElement> properties, string name)
    {
        if (!properties.TryGetValue(name, out var element) || element.ValueKind != JsonValueKind.String)
            throw new InteractionContractException("MISSING_INTENT_FIELD", $"{name} is required and must be a string.");
        return element.GetString()!;
    }
}

public sealed record AuthorizedInteractionEnvelope
{
    private AuthorizedInteractionEnvelope(InteractionIntent intent, InteractionHostContext host, string fingerprint)
    {
        Intent = intent;
        Host = host;
        Fingerprint = fingerprint;
    }

    public InteractionIntent Intent { get; }
    public InteractionHostContext Host { get; }
    public string Fingerprint { get; }

    public static AuthorizedInteractionEnvelope Create(InteractionIntent intent, InteractionHostContext host)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(host);
        if (intent.MaximumPlanSteps > host.Budgets.MaximumPlanSteps)
            throw new InteractionContractException("PLAN_BUDGET_EXCEEDED", "The requested plan exceeds the host-owned budget.");

        var canonical = JsonSerializer.Serialize(new
        {
            intent = new
            {
                intent.IntentText,
                roleHints = intent.RoleHints,
                conversationFacts = intent.ConversationFactReferences,
                intent.MaximumPlanSteps,
                plannerPreference = intent.PlannerPreference.ToString().ToLowerInvariant()
            },
            host = new
            {
                applicationId = host.ApplicationRevision.ApplicationId.Value,
                applicationRevision = host.ApplicationRevision.Revision,
                applicationFingerprint = host.ApplicationRevision.Fingerprint,
                baseApplications = host.ApplicationRevision.BaseApplications.Select(x => x.Value),
                host.StateSpaceId,
                host.SessionContextId,
                host.StateRevision,
                host.EffectiveSetFingerprint,
                principal = host.Principal.PrincipalId,
                authenticationMethod = host.Principal.AuthenticationMethod,
                role = host.RoleProfile.StableKey,
                host.ConversationId,
                host.ParentDelegationId,
                authorizationEvidence = host.Authorization.EvidenceReference,
                budgets = host.Budgets
            }
        });
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/authorized-interaction-envelope/v1",
            InteractionCanonicalJson.CanonicalizeObject(canonical));
        return new(intent, host, fingerprint);
    }

    /// <summary>
    /// Rehydrates the redacted envelope identity needed to validate an execution request. Only an
    /// authorized receipt store may supply <paramref name="persistedFingerprint"/>; it is evidence,
    /// not a caller-selected replacement for normal envelope creation.
    /// </summary>
    public static AuthorizedInteractionEnvelope FromReceipt(
        InteractionIntent redactedIntent,
        InteractionHostContext currentHost,
        string persistedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(redactedIntent);
        ArgumentNullException.ThrowIfNull(currentHost);
        persistedFingerprint = InteractionGuard.UpperSha256(persistedFingerprint, nameof(persistedFingerprint));
        if (redactedIntent.MaximumPlanSteps > currentHost.Budgets.MaximumPlanSteps)
            throw new InteractionContractException("PLAN_BUDGET_EXCEEDED", "The persisted plan exceeds the current host budget.");
        return new(redactedIntent, currentHost, persistedFingerprint);
    }
}
