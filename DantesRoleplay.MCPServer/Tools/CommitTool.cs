using System.ComponentModel;
using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.Campaign;
using DantesRoleplay.Quest;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Events;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SystemFeedback;
using DantesRoleplay.World;
using DantesRoleplay.Story;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The write side of the three-verb MCP surface. Payloads are deliberately JSON objects so each
/// commit kind can retain the shape of its existing implementation handler.
///
/// The kind list is <see cref="VerbSurface.CommitKinds"/> and nothing else; a guard test asserts
/// the switch below against it in both directions. Every rejection carries the expected payload
/// in full rather than a pointer to it, so a session that guessed wrong is corrected in the same
/// round trip instead of spending another one asking.
/// </summary>
[McpServerToolType]
public sealed class CommitTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [McpServerTool(Name = "commit")]
    [Description(
        "Change anything in this system. kind is one of: procedure, component, effects, mechanic, event-type, subscription, " +
        "action, itinerary-advance, campaign, quest, notification, feedback, story-plan. payload is a JSON object encoded as a string, shaped per kind — " +
        "query(kind: \"capabilities\") gives every shape exactly, and a rejection repeats the one " +
        "you needed. Where dryRun is supported, send dryRun: true first and read every check, then " +
        "commit the identical payload. This is the only path that changes state.")]
    public async Task<ToolEnvelope> CommitAsync(
        IProcedureStore procedures,
        IWorldStore world,
        IEffectApplier effects,
        IMechanicStore mechanics,
        IEventTypeStore eventTypes,
        ISubscriptionStore subscriptions,
        IActionRunner actions,
        IModeAwareItineraryReader itineraries,
        ICampaignBlueprintValidator campaigns,
        ICampaignBootstrapper campaignBootstrapper,
        ICampaignContinuityRunner campaignContinuity,
        ICampaignSessionValidator campaignSessions,
        ICampaignSessionStarter campaignSessionStarter,
        IQuestCreator quests,
        IQuestLifecycleRunner questLifecycle,
        IOperationLog log,
        INotificationStore notifications,
        [Description("Closed kind: procedure, component, effects, mechanic, event-type, subscription, action, itinerary-advance, campaign, quest, notification, feedback, or story-plan.")]
        string kind,
        [Description("JSON object containing the selected kind's existing tool arguments.")]
        string payload,
        [Description(
            "What you were trying to achieve, in your own words. For kind \"action\" the payload's "
            + "own intent is what gets recorded, because it is also what selects the rule.")]
        string intent = "",
        [Description("Procedure ids consulted before this commit.")] string[]? proceduresUsed = null,
        [Description("Validate without changing state where supported.")] bool dryRun = false,
        CancellationToken cancellationToken = default,
        ICampaignCharacterParticipationAttacher? campaignParticipation = null,
        ICampaignSessionEndValidator? campaignSessionEndValidator = null,
        ICampaignSessionEnder? campaignSessionEnder = null,
        ICampaignSessionCheckpointValidator? campaignSessionCheckpointValidator = null,
        ICampaignSessionCheckpointCreator? campaignSessionCheckpointCreator = null,
        ISystemFeedbackService? feedback = null,
        IStoryPlanCoordinator? storyPlans = null,
        ICampaignQuestContextRunner? campaignQuestContexts = null)
    {
        var normalizedKind = kind?.Trim().ToLowerInvariant() ?? string.Empty;
        var spec = VerbSurface.Commit(normalizedKind);

        if (spec is null)
        {
            return await ToolRunner.RunAsync(
                log,
                "commit",
                intent,
                $"commit:{normalizedKind}",
                proceduresUsed,
                () => Task.FromResult(ToolOutcome.Fail(
                    "UNKNOWN_KIND",
                    $"Unknown commit kind '{kind}'. Valid kinds: "
                    + $"{string.Join(", ", VerbSurface.CommitKindNames)}.",
                    "query(kind: \"capabilities\")",
                    $"Rejected commit kind '{kind}'.")),
                consumesReadEvidence: !dryRun);
        }

        if (dryRun && !spec.SupportsDryRun)
        {
            return await ToolRunner.RunAsync(
                log,
                "commit",
                intent,
                $"commit:{normalizedKind}",
                proceduresUsed,
                () => Task.FromResult(ToolOutcome.Fail(
                    "NOT_SUPPORTED",
                    $"Dry run is not supported for commit kind '{normalizedKind}'. "
                    + (normalizedKind == "action"
                        ? "An action dry-runs its own effects internally before applying them."
                        : "Send the commit itself; it validates before it writes."),
                    VerbSurface.CommitCall(normalizedKind),
                    $"Rejected unsupported dry run for '{normalizedKind}'.")),
                consumesReadEvidence: false);
        }

        if (!TryReadObject(payload, out var document, out var parseError))
        {
            return await InvalidPayloadEnvelope(
                log, normalizedKind, parseError!, intent, proceduresUsed, dryRun);
        }

        var parsedPayload = document!;
        using (parsedPayload)
        {
            using var dispatch = ToolRunner.EnterProtocol("commit", normalizedKind, !dryRun);

            return normalizedKind switch
            {
                "procedure" => await CommitProcedureAsync(
                    procedures, log, parsedPayload.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "component" => await CommitComponentAsync(
                    world, log, parsedPayload.RootElement, intent, proceduresUsed, cancellationToken),
                "effects" => await CommitEffectsAsync(
                    effects, log, parsedPayload.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "mechanic" => await CommitMechanicAsync(
                    mechanics, log, parsedPayload.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "event-type" => await CommitEventTypeAsync(eventTypes, log, parsedPayload.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "subscription" => await CommitSubscriptionAsync(subscriptions, log, parsedPayload.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "action" => await CommitActionAsync(
                    actions, log, parsedPayload.RootElement, intent, proceduresUsed, cancellationToken),
                "itinerary-advance" => await CommitItineraryAdvanceAsync(
                    itineraries, actions, log, parsedPayload.RootElement, proceduresUsed, cancellationToken),
                "campaign" => await CommitCampaignAsync(campaigns, campaignBootstrapper, campaignContinuity, campaignQuestContexts, campaignSessions, campaignSessionStarter, campaignParticipation, campaignSessionEndValidator, campaignSessionEnder, campaignSessionCheckpointValidator, campaignSessionCheckpointCreator, log, parsedPayload.RootElement, intent, proceduresUsed, cancellationToken),
                "quest" => await CommitQuestAsync(quests, questLifecycle, log, parsedPayload.RootElement, intent, proceduresUsed, cancellationToken),
                "notification" => await CommitNotificationAsync(
                    notifications, log, parsedPayload.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "feedback" => await CommitFeedbackAsync(feedback, log, parsedPayload.RootElement, intent, proceduresUsed, cancellationToken),
                "story-plan" => await CommitStoryPlanAsync(storyPlans, log, parsedPayload.RootElement, intent, proceduresUsed, cancellationToken),
                _ => throw new InvalidOperationException($"Unhandled commit kind '{kind}'.")
            };
        }
    }

    private static async Task<ToolEnvelope> CommitProcedureAsync(
        IProcedureStore procedures,
        IOperationLog log,
        JsonElement payload,
        string intent,
        string[]? proceduresUsed,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out ProcedurePayload? value, out var error) ||
            value is null ||
            string.IsNullOrWhiteSpace(value.Id) ||
            string.IsNullOrWhiteSpace(value.Category) ||
            string.IsNullOrWhiteSpace(value.Name) ||
            string.IsNullOrWhiteSpace(value.Description) ||
            string.IsNullOrWhiteSpace(value.Instructions))
        {
            return await InvalidPayloadEnvelope(
                log,
                "procedure",
                error ?? "Procedure payload requires id, category, name, description, and instructions.",
                intent,
                proceduresUsed,
                dryRun);
        }

        return await new ProcedureTools().WriteProcedureAsync(
            procedures,
            log,
            value.Id,
            value.Category,
            value.Name,
            value.Description,
            value.Instructions,
            value.Governs ?? string.Empty,
            value.Constraints ?? string.Empty,
            value.Status,
            value.ChangeNote ?? string.Empty,
            intent,
            proceduresUsed,
            dryRun,
            cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitComponentAsync(
        IWorldStore world,
        IOperationLog log,
        JsonElement payload,
        string intent,
        string[]? proceduresUsed,
        CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out ComponentPayload? value, out var error) ||
            value is null ||
            string.IsNullOrWhiteSpace(value.Id) ||
            string.IsNullOrWhiteSpace(value.Name) ||
            string.IsNullOrWhiteSpace(value.Description))
        {
            return await InvalidPayloadEnvelope(
                log,
                "component",
                error ?? "Component payload requires id, name, and description.",
                intent,
                proceduresUsed,
                dryRun: false);
        }

        return await new WorldTools().DefineComponentAsync(
            world,
            log,
            value.Id,
            value.Name,
            value.Description,
            value.Schema ?? string.Empty,
            intent,
            proceduresUsed,
            cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitEffectsAsync(
        IEffectApplier applier,
        IOperationLog log,
        JsonElement payload,
        string intent,
        string[]? proceduresUsed,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out EffectsPayload? value, out var error) || value?.Effects is null)
        {
            return await InvalidPayloadEnvelope(
                log,
                "effects",
                error ?? "Effects payload requires an effects array.",
                intent,
                proceduresUsed,
                dryRun);
        }

        return await new WorldTools().ApplyEffectsAsync(
            applier,
            log,
            value.Effects,
            intent,
            proceduresUsed,
            dryRun,
            cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitMechanicAsync(
        IMechanicStore mechanics,
        IOperationLog log,
        JsonElement payload,
        string intent,
        string[]? proceduresUsed,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out MechanicPayload? value, out var error) ||
            value is null ||
            string.IsNullOrWhiteSpace(value.Id) ||
            string.IsNullOrWhiteSpace(value.Category) ||
            string.IsNullOrWhiteSpace(value.Name) ||
            string.IsNullOrWhiteSpace(value.Source))
        {
            return await InvalidPayloadEnvelope(
                log,
                "mechanic",
                error ?? "Mechanic payload requires id, category, name, and source.",
                intent,
                proceduresUsed,
                dryRun);
        }

        return await new MechanicTools().WriteMechanicAsync(
            mechanics,
            log,
            value.Id,
            value.Category,
            value.Name,
            value.Description ?? string.Empty,
            value.Matches ?? string.Empty,
            value.Requirements ?? "{}",
            value.Source,
            value.Scope ?? string.Empty,
            value.Status,
            value.ChangeNote ?? string.Empty,
            intent,
            proceduresUsed,
            dryRun,
            cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitActionAsync(
        IActionRunner actions,
        IOperationLog log,
        JsonElement payload,
        string intent,
        string[]? proceduresUsed,
        CancellationToken cancellationToken)
    {
        if (!ClosedObject(payload, ["intent"], ["roleEntityIds", "input", "scope", "seed"]))
        {
            return await InvalidPayloadEnvelope(
                log,
                "action",
                "Action payload requires intent — what the actor is trying to do, in the player's words — "
                    + "and allows only roleEntityIds, input, scope, and seed as optional fields.",
                intent,
                proceduresUsed,
                dryRun: false);
        }

        if (!TryDeserialize(payload, out ActionPayload? value, out var error) ||
            value is null ||
            string.IsNullOrWhiteSpace(value.Intent))
        {
            // Through ToolRunner like every other rejection: a failure nobody recorded is the one
            // thing history cannot show you afterwards (§P3).
            return await InvalidPayloadEnvelope(
                log,
                "action",
                error ?? "Action payload requires intent — what the actor is trying to do, in "
                    + "the player's words.",
                intent,
                proceduresUsed,
                dryRun: false);
        }

        return await new ActionTools().RunActionAsync(
            actions,
            // The payload's intent is the one that both selects the rule and lands in the audit.
            // Falling back means a caller who filled in only the top-level intent is not silently
            // ignored — but the two cannot both be recorded without a kernel change.
            string.IsNullOrWhiteSpace(value.Intent) ? intent : value.Intent,
            value.RoleEntityIds,
            value.Input ?? "{}",
            value.Scope,
            value.Seed,
            proceduresUsed,
            cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitStoryPlanAsync(IStoryPlanCoordinator? coordinator, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, CancellationToken cancellationToken)
    {
        if (coordinator is null)
            return await ToolRunner.RunAsync(log, "commit", intent, "story-plan", proceduresUsed, () => Task.FromResult(ToolOutcome.Fail(
                "STORY_AUDIENCE_DENIED", "Story plans require an explicitly enabled development GM audience.",
                "Enable the development GM audience for the intended campaign.", "Story-plan commit was unavailable.")));
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("operation", out var operation) || operation.ValueKind != JsonValueKind.String)
            return await InvalidPayloadEnvelope(log, "story-plan", "story-plan payload requires operation start or cancel.", intent, proceduresUsed, false);
        return operation.GetString() switch
        {
            "start" when StoryPlanJsonParser.TryParseStart(payload, out var start).Valid && start is not null => await new StoryPlanTools().StartAsync(coordinator, log, start, intent, proceduresUsed, cancellationToken),
            "cancel" when StoryPlanJsonParser.TryParseCancel(payload, out var cancel).Valid && cancel is not null => await new StoryPlanTools().CancelAsync(coordinator, log, cancel, intent, proceduresUsed, cancellationToken),
            _ => await InvalidPayloadEnvelope(log, "story-plan", "story-plan payload has an invalid closed shape.", intent, proceduresUsed, false)
        };
    }

    private static async Task<ToolEnvelope> CommitFeedbackAsync(ISystemFeedbackService? feedback, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, CancellationToken cancellationToken)
    {
        if (feedback is null) return await InvalidPayloadEnvelope(log, "feedback", "Feedback reporting is unavailable because its store is not registered.", intent, proceduresUsed, false);
        if (!ClosedObject(payload, ["operation", "requestToken", "category", "impact", "summary", "observed"], ["expected", "reproductionSteps", "relatedOperationIds", "relatedProcedureIds"])
            || !payload.TryGetProperty("operation", out var operation) || operation.ValueKind != JsonValueKind.String || operation.GetString() != "submit")
            return await InvalidPayloadEnvelope(log, "feedback", "Feedback payload requires operation submit and its exact closed shape.", intent, proceduresUsed, false);
        try
        {
            var request = payload.Deserialize<SystemFeedbackSubmitRequest>(JsonOptions);
            return request is null
                ? await InvalidPayloadEnvelope(log, "feedback", "Feedback payload could not be read.", intent, proceduresUsed, false)
                : await new SystemFeedbackTools().SubmitAsync(feedback, request, intent, proceduresUsed, cancellationToken);
        }
        catch (JsonException ex) { return await InvalidPayloadEnvelope(log, "feedback", ex.Message, intent, proceduresUsed, false); }
    }

    private static async Task<ToolEnvelope> CommitItineraryAdvanceAsync(IModeAwareItineraryReader itineraries, IActionRunner actions, IOperationLog log, JsonElement payload, string[]? proceduresUsed, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out ItineraryAdvancePayload? value, out var error) || value is null || string.IsNullOrWhiteSpace(value.WorldId) || string.IsNullOrWhiteSpace(value.TravellerId) || string.IsNullOrWhiteSpace(value.DestinationLocationId) || string.IsNullOrWhiteSpace(value.ItineraryFingerprint) || value.NextLegIndex is null)
            return await InvalidPayloadEnvelope(log, "itinerary-advance", error ?? "Itinerary advance requires worldId, travellerId, destinationLocationId, itineraryFingerprint, and nextLegIndex.", "Advance one itinerary leg.", proceduresUsed, false);
        return await new ItineraryAdvanceTools().AdvanceAsync(itineraries, actions, log, new ModeAwareItineraryAdvanceRequest(value.WorldId, value.TravellerId, value.DestinationLocationId, value.ItineraryFingerprint, value.NextLegIndex.Value, value.GroundConveyanceId, value.AerialConveyanceId), proceduresUsed ?? [], cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitCampaignAsync(ICampaignBlueprintValidator campaigns, ICampaignBootstrapper bootstrapper, ICampaignContinuityRunner continuity, ICampaignQuestContextRunner? questContexts, ICampaignSessionValidator sessions, ICampaignSessionStarter sessionStarter, ICampaignCharacterParticipationAttacher? participation, ICampaignSessionEndValidator? sessionEndValidator, ICampaignSessionEnder? sessionEnder, ICampaignSessionCheckpointValidator? checkpointValidator, ICampaignSessionCheckpointCreator? checkpointCreator, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, CancellationToken cancellationToken)
    {
        if (!payload.TryGetProperty("operation", out var operation) || operation.ValueKind != JsonValueKind.String)
            return await InvalidPayloadEnvelope(log, "campaign", "Campaign payload requires a closed operation.", "Run a supported campaign operation.", ["procedure.campaign.create", "procedure.campaign.chapter"], false);
        var name = operation.GetString();
        if (name is "initialize-continuity" or "advance-chapter" or "close-chapter" or "conclude-arc") return await CommitContinuityAsync(continuity, log, payload, name, intent, proceduresUsed, cancellationToken);
        if (name == "attach-quest-context") return questContexts is null
            ? await InvalidPayloadEnvelope(log, "campaign", "Campaign quest-context attachment is unavailable because its owner is not registered.", "Attach an active quest to campaign continuity.", ["procedure.campaign.quest-context"], false)
            : await CommitQuestContextAsync(questContexts, log, payload, intent, proceduresUsed, cancellationToken);
        if (name == "validate-session") return await CommitSessionValidationAsync(sessions, log, payload, cancellationToken);
        if (name == "start-session") return await CommitSessionStartAsync(sessionStarter, log, payload, intent, proceduresUsed, cancellationToken);
        if (name == "validate-session-end") return sessionEndValidator is null
            ? await InvalidPayloadEnvelope(log, "campaign", "Campaign session-end validation is unavailable because its owner is not registered.", "Validate a campaign session closure.", ["procedure.campaign.session"], false)
            : await CommitSessionEndValidationAsync(sessionEndValidator, log, payload, cancellationToken);
        if (name == "end-session") return sessionEnder is null
            ? await InvalidPayloadEnvelope(log, "campaign", "Campaign session end is unavailable because its owner is not registered.", "End a campaign session.", ["procedure.campaign.session"], false)
            : await CommitSessionEndAsync(sessionEnder, log, payload, intent, proceduresUsed, cancellationToken);
        if (name == "validate-session-checkpoint") return checkpointValidator is null
            ? await InvalidPayloadEnvelope(log, "campaign", "Campaign session checkpoint validation is unavailable because its owner is not registered.", "Validate an ended campaign session checkpoint.", ["procedure.campaign.session"], false)
            : await CommitSessionCheckpointValidationAsync(checkpointValidator, log, payload, cancellationToken);
        if (name == "checkpoint-session") return checkpointCreator is null
            ? await InvalidPayloadEnvelope(log, "campaign", "Campaign session checkpoint capture is unavailable because its owner is not registered.", "Capture an ended campaign session checkpoint.", ["procedure.campaign.session"], false)
            : await CommitSessionCheckpointAsync(checkpointCreator, log, payload, intent, proceduresUsed, cancellationToken);
        if (name == "attach-character-participation") return participation is null
            ? await InvalidPayloadEnvelope(log, "campaign", "Campaign participation attachment is unavailable because its owner is not registered.", "Attach a character to an active campaign.", ["procedure.campaign.character-participation"], false)
            : await CommitParticipationAttachAsync(participation, log, payload, intent, proceduresUsed, cancellationToken);
        if (!payload.TryGetProperty("blueprint", out var blueprint) || blueprint.ValueKind != JsonValueKind.Object || !ClosedBlueprint(blueprint))
            return await InvalidPayloadEnvelope(log, "campaign", "Campaign validate/create requires a closed blueprint object.", "Validate or create campaign blueprint.", ["procedure.campaign.create"], false);
        var create = name == "create";
        if ((create && !ClosedObject(payload, ["operation", "blueprint", "reviewFingerprint"])) || (!create && (name != "validate" || !ClosedObject(payload, ["operation", "blueprint"]))))
            return await InvalidPayloadEnvelope(log, "campaign", "Campaign operation must be validate/create with its exact closed payload.", "Validate or create campaign blueprint.", ["procedure.campaign.create"], false);
        try
        {
            var value = blueprint.Deserialize<CampaignBlueprint>(JsonOptions);
            if (value is null) return await InvalidPayloadEnvelope(log, "campaign", "Campaign blueprint could not be read.", "Validate or create campaign blueprint.", ["procedure.campaign.create"], false);
            if (!create) return await new CampaignTools().ValidateAsync(campaigns, log, value, cancellationToken);
            var fingerprint = payload.GetProperty("reviewFingerprint");
            if (fingerprint.ValueKind != JsonValueKind.String) return await InvalidPayloadEnvelope(log, "campaign", "Campaign create requires reviewFingerprint as a string.", "Create campaign blueprint.", ["procedure.campaign.create"], false);
            return await new CampaignTools().CreateAsync(bootstrapper, value, fingerprint.GetString() ?? string.Empty, intent, proceduresUsed, cancellationToken);
        }
        catch (JsonException ex) { return await InvalidPayloadEnvelope(log, "campaign", ex.Message, "Validate or create campaign blueprint.", ["procedure.campaign.create"], false); }
    }

    private static async Task<ToolEnvelope> CommitQuestContextAsync(ICampaignQuestContextRunner runner, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, CancellationToken cancellationToken)
    {
        if (!ClosedObject(payload, ["operation", "campaignId", "arcId", "chapterId", "questId", "expectedQuestStatus"]))
            return await InvalidPayloadEnvelope(log, "campaign", "Quest-context attachment requires exactly operation, campaignId, arcId, chapterId, questId, and expectedQuestStatus.", "Attach an active quest to campaign continuity.", ["procedure.campaign.quest-context"], false);
        try
        {
            var request = payload.Deserialize<CampaignQuestContextRequest>(JsonOptions);
            return request is null
                ? await InvalidPayloadEnvelope(log, "campaign", "Quest-context attachment request could not be read.", "Attach an active quest to campaign continuity.", ["procedure.campaign.quest-context"], false)
                : await new CampaignTools().AttachQuestContextAsync(runner, request, intent, proceduresUsed, cancellationToken);
        }
        catch (JsonException ex) { return await InvalidPayloadEnvelope(log, "campaign", ex.Message, "Attach an active quest to campaign continuity.", ["procedure.campaign.quest-context"], false); }
    }

    private static async Task<ToolEnvelope> CommitParticipationAttachAsync(ICampaignCharacterParticipationAttacher attacher, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, CancellationToken cancellationToken)
    {
        if (!ClosedObject(payload, ["operation", "campaignId", "actorId"]))
            return await InvalidPayloadEnvelope(log, "campaign", "Campaign character attachment requires exactly operation, campaignId, and actorId.", "Attach a character to an active campaign.", ["procedure.campaign.character-participation"], false);
        try
        {
            var request = payload.Deserialize<CampaignCharacterParticipationAttachRequest>(JsonOptions);
            return request is null
                ? await InvalidPayloadEnvelope(log, "campaign", "Campaign character attachment request could not be read.", "Attach a character to an active campaign.", ["procedure.campaign.character-participation"], false)
                : await new CampaignTools().AttachCharacterParticipationAsync(attacher, request, intent, proceduresUsed, cancellationToken);
        }
        catch (JsonException ex) { return await InvalidPayloadEnvelope(log, "campaign", ex.Message, "Attach a character to an active campaign.", ["procedure.campaign.character-participation"], false); }
    }

    private static async Task<ToolEnvelope> CommitSessionValidationAsync(ICampaignSessionValidator sessions, IOperationLog log, JsonElement payload, CancellationToken cancellationToken)
    {
        if (!ClosedObject(payload, ["operation", "campaignId", "sessionId"]))
            return await InvalidPayloadEnvelope(log, "campaign", "Session validation requires exactly operation, campaignId, and sessionId.", "Validate campaign session readiness.", ["procedure.campaign.session"], false);
        try
        {
            var request = payload.Deserialize<CampaignSessionValidationRequest>(JsonOptions);
            return request is null
                ? await InvalidPayloadEnvelope(log, "campaign", "Session validation request could not be read.", "Validate campaign session readiness.", ["procedure.campaign.session"], false)
                : await new CampaignTools().ValidateSessionAsync(sessions, log, request, cancellationToken);
        }
        catch (JsonException ex) { return await InvalidPayloadEnvelope(log, "campaign", ex.Message, "Validate campaign session readiness.", ["procedure.campaign.session"], false); }
    }

    private static async Task<ToolEnvelope> CommitSessionStartAsync(ICampaignSessionStarter starter, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, CancellationToken cancellationToken)
    {
        if (!ClosedObject(payload, ["operation", "campaignId", "sessionId"]))
            return await InvalidPayloadEnvelope(log, "campaign", "Session start requires exactly operation, campaignId, and sessionId.", "Validate campaign session readiness.", ["procedure.campaign.session"], false);
        try
        {
            var request = payload.Deserialize<CampaignSessionValidationRequest>(JsonOptions);
            return request is null
                ? await InvalidPayloadEnvelope(log, "campaign", "Session start request could not be read.", "Validate campaign session readiness.", ["procedure.campaign.session"], false)
                : await new CampaignTools().StartSessionAsync(starter, request, intent, proceduresUsed, cancellationToken);
        }
        catch (JsonException ex) { return await InvalidPayloadEnvelope(log, "campaign", ex.Message, "Validate campaign session readiness.", ["procedure.campaign.session"], false); }
    }

    private static async Task<ToolEnvelope> CommitSessionEndValidationAsync(ICampaignSessionEndValidator validator, IOperationLog log, JsonElement payload, CancellationToken cancellationToken)
    {
        if (!ClosedObject(payload, ["operation", "sessionId", "expectedStatus"]))
            return await InvalidPayloadEnvelope(log, "campaign", "Session-end validation requires exactly operation, sessionId, and expectedStatus.", "Validate a campaign session closure.", ["procedure.campaign.session"], false);
        try
        {
            var request = payload.Deserialize<CampaignSessionEndRequest>(JsonOptions);
            return request is null
                ? await InvalidPayloadEnvelope(log, "campaign", "Session-end validation request could not be read.", "Validate a campaign session closure.", ["procedure.campaign.session"], false)
                : await new CampaignTools().ValidateSessionEndAsync(validator, log, request, cancellationToken);
        }
        catch (JsonException ex) { return await InvalidPayloadEnvelope(log, "campaign", ex.Message, "Validate a campaign session closure.", ["procedure.campaign.session"], false); }
    }

    private static async Task<ToolEnvelope> CommitSessionEndAsync(ICampaignSessionEnder ender, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, CancellationToken cancellationToken)
    {
        if (!ClosedObject(payload, ["operation", "sessionId", "expectedStatus"]))
            return await InvalidPayloadEnvelope(log, "campaign", "Session end requires exactly operation, sessionId, and expectedStatus.", "End a campaign session.", ["procedure.campaign.session"], false);
        try
        {
            var request = payload.Deserialize<CampaignSessionEndRequest>(JsonOptions);
            return request is null
                ? await InvalidPayloadEnvelope(log, "campaign", "Session-end request could not be read.", "End a campaign session.", ["procedure.campaign.session"], false)
                : await new CampaignTools().EndSessionAsync(ender, request, intent, proceduresUsed, cancellationToken);
        }
        catch (JsonException ex) { return await InvalidPayloadEnvelope(log, "campaign", ex.Message, "End a campaign session.", ["procedure.campaign.session"], false); }
    }

    private static async Task<ToolEnvelope> CommitSessionCheckpointValidationAsync(ICampaignSessionCheckpointValidator validator, IOperationLog log, JsonElement payload, CancellationToken cancellationToken)
    {
        if (!ClosedObject(payload, ["operation", "sessionId", "expectedStatus"]))
            return await InvalidPayloadEnvelope(log, "campaign", "Session checkpoint validation requires its exact closed request.", "Validate an ended campaign session checkpoint.", ["procedure.campaign.session"], false);
        try
        {
            var request = payload.Deserialize<CampaignSessionCheckpointRequest>(JsonOptions);
            return request is null ? await InvalidPayloadEnvelope(log, "campaign", "Session checkpoint request could not be read.", "Validate an ended campaign session checkpoint.", ["procedure.campaign.session"], false) : await new CampaignTools().ValidateSessionCheckpointAsync(validator, log, request, cancellationToken);
        }
        catch (JsonException ex) { return await InvalidPayloadEnvelope(log, "campaign", ex.Message, "Validate an ended campaign session checkpoint.", ["procedure.campaign.session"], false); }
    }

    private static async Task<ToolEnvelope> CommitSessionCheckpointAsync(ICampaignSessionCheckpointCreator creator, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, CancellationToken cancellationToken)
    {
        if (!ClosedObject(payload, ["operation", "sessionId", "expectedStatus"]))
            return await InvalidPayloadEnvelope(log, "campaign", "Session checkpoint capture requires its exact closed request.", "Capture an ended campaign session checkpoint.", ["procedure.campaign.session"], false);
        try
        {
            var request = payload.Deserialize<CampaignSessionCheckpointRequest>(JsonOptions);
            return request is null ? await InvalidPayloadEnvelope(log, "campaign", "Session checkpoint request could not be read.", "Capture an ended campaign session checkpoint.", ["procedure.campaign.session"], false) : await new CampaignTools().CheckpointSessionAsync(creator, request, intent, proceduresUsed, cancellationToken);
        }
        catch (JsonException ex) { return await InvalidPayloadEnvelope(log, "campaign", ex.Message, "Capture an ended campaign session checkpoint.", ["procedure.campaign.session"], false); }
    }

    private static async Task<ToolEnvelope> CommitContinuityAsync(ICampaignContinuityRunner runner, IOperationLog log, JsonElement payload, string operation, string intent, string[]? proceduresUsed, CancellationToken ct)
    {
        try
        {
            var tools = new CampaignTools();
            return operation switch
            {
                "initialize-continuity" when ClosedObject(payload, ["operation", "seed"]) && ClosedSeed(payload.GetProperty("seed")) => await tools.ContinuityAsync(() => runner.InitializeAsync(payload.GetProperty("seed").Deserialize<CampaignContinuitySeed>(JsonOptions)!, intent, proceduresUsed, ct)),
                "advance-chapter" when ClosedObject(payload, ["operation", "campaignId", "chapterId", "expectedStatus", "closingSummary", "nextChapter"]) && ClosedNextChapter(payload.GetProperty("nextChapter")) => await tools.ContinuityAsync(() => runner.AdvanceAsync(payload.GetProperty("campaignId").GetString() ?? "", payload.GetProperty("chapterId").GetString() ?? "", payload.GetProperty("expectedStatus").GetString() ?? "", payload.GetProperty("closingSummary").GetString() ?? "", payload.GetProperty("nextChapter").Deserialize<CampaignNextChapter>(JsonOptions)!, intent, proceduresUsed, ct)),
                "close-chapter" when ClosedObject(payload, ["operation", "campaignId", "chapterId", "expectedStatus", "closingSummary"]) => await tools.ContinuityAsync(() => runner.CloseAsync(payload.GetProperty("campaignId").GetString() ?? "", payload.GetProperty("chapterId").GetString() ?? "", payload.GetProperty("expectedStatus").GetString() ?? "", payload.GetProperty("closingSummary").GetString() ?? "", intent, proceduresUsed, ct)),
                "conclude-arc" when ClosedObject(payload, ["operation", "campaignId", "arcId", "expectedStatus", "outcome", "closingSummary"]) => await tools.ContinuityAsync(() => runner.ConcludeArcAsync(payload.GetProperty("campaignId").GetString() ?? "", payload.GetProperty("arcId").GetString() ?? "", payload.GetProperty("expectedStatus").GetString() ?? "", payload.GetProperty("outcome").GetString() ?? "", payload.GetProperty("closingSummary").GetString() ?? "", intent, proceduresUsed, ct)),
                _ => await InvalidPayloadEnvelope(log, "campaign", "Continuity operation payload is missing, extra, or malformed fields.", "Run a supported campaign continuity operation.", ["procedure.campaign.chapter"], false)
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return await InvalidPayloadEnvelope(log, "campaign", ex.Message, "Run a supported campaign continuity operation.", ["procedure.campaign.chapter"], false); }
    }

    private static bool ClosedSeed(JsonElement seed) => seed.ValueKind == JsonValueKind.Object && seed.TryGetProperty("chapter", out var chapter) && seed.TryGetProperty("arc", out var arc) && ClosedObject(seed, ["campaignId", "chapter", "arc"]) && ClosedObject(chapter, ["localKey", "title", "partyQuestion"], ["gmContext"]) && ClosedObject(arc, ["localKey", "title", "partyStake"], ["gmContext"]);
    private static bool ClosedNextChapter(JsonElement chapter) => ClosedObject(chapter, ["localKey", "title", "partyQuestion"], ["gmContext"]);

    private static bool ClosedBlueprint(JsonElement blueprint) =>
        ClosedObject(blueprint, ["campaignId", "title", "premise", "partyGoals", "toneAndBoundaries", "rulesetScope", "existingWorldId", "startingLocationId", "references", "initialChapter", "initialArc"], ["futureQuestShapedProblem"]);

    private static async Task<ToolEnvelope> CommitQuestAsync(IQuestCreator quests, IQuestLifecycleRunner lifecycle, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, CancellationToken ct)
    {
        if (payload.TryGetProperty("operation", out var operation) && operation.ValueKind == JsonValueKind.String)
        {
            var operationName = operation.GetString();
            try
            {
                if (operationName is "offer" or "accept" or "reconcile" or "fail" or "reopen-quest" or "archive")
                {
                    if (!ClosedObject(payload, ["operation", "questId", "expectedQuestStatus", "reason"]))
                        return await InvalidPayloadEnvelope(log, "quest", "Quest root lifecycle operation requires its exact closed request.", "Run a supported quest lifecycle operation.", ["procedure.quest.modify"], false);
                    var request = payload.Deserialize<QuestLifecycleRequest>(JsonOptions);
                    return request is null
                        ? await InvalidPayloadEnvelope(log, "quest", "Quest lifecycle request could not be read.", "Run a supported quest lifecycle operation.", ["procedure.quest.modify"], false)
                        : await new QuestTools().TransitionAsync(lifecycle, request, intent, proceduresUsed, ct);
                }
                if (operationName == "set-objective")
                {
                    if (!ClosedObject(payload, ["operation", "questId", "expectedQuestStatus", "objectiveId", "expectedObjectiveStatus", "targetStatus", "reason"]))
                        return await InvalidPayloadEnvelope(log, "quest", "Quest objective setting requires its exact closed request.", "Run a supported quest lifecycle operation.", ["procedure.quest.modify"], false);
                    var request = payload.Deserialize<QuestObjectiveTransitionRequest>(JsonOptions);
                    return request is null
                        ? await InvalidPayloadEnvelope(log, "quest", "Quest objective request could not be read.", "Run a supported quest lifecycle operation.", ["procedure.quest.modify"], false)
                        : await new QuestTools().TransitionObjectiveAsync(lifecycle, request, intent, proceduresUsed, ct);
                }
                if (operationName is "unblock-objective" or "reopen-objective")
                {
                    if (!ClosedObject(payload, ["operation", "questId", "expectedQuestStatus", "objectiveId", "expectedObjectiveStatus", "reason"]))
                        return await InvalidPayloadEnvelope(log, "quest", "Quest objective unblocking or reopening requires its exact closed request.", "Run a supported quest lifecycle operation.", ["procedure.quest.modify"], false);
                    var request = payload.Deserialize<QuestObjectiveTransitionRequest>(JsonOptions);
                    return request is null
                        ? await InvalidPayloadEnvelope(log, "quest", "Quest objective request could not be read.", "Run a supported quest lifecycle operation.", ["procedure.quest.modify"], false)
                        : await new QuestTools().TransitionObjectiveAsync(lifecycle, request, intent, proceduresUsed, ct);
                }
                return await InvalidPayloadEnvelope(log, "quest", "Quest lifecycle supports only offer, accept, set-objective, unblock-objective, reconcile, fail, reopen-objective, reopen-quest, or archive.", "Run a supported quest lifecycle operation.", ["procedure.quest.modify"], false);
            }
            catch (JsonException)
            {
                return await InvalidPayloadEnvelope(log, "quest", "Quest lifecycle request could not be read.", "Run a supported quest lifecycle operation.", ["procedure.quest.modify"], false);
            }
        }
        if (!ClosedObject(payload,["questId","title","premise","summary","visibility","campaignId","arcId","chapterIds","objectives"]))
            return await InvalidPayloadEnvelope(log,"quest","Quest creation requires its exact closed request object.",intent,proceduresUsed,false);
        try
        {
            var request = payload.Deserialize<QuestCreateRequest>(JsonOptions);
            return request is null
                ? await InvalidPayloadEnvelope(log,"quest","Quest request could not be read.",intent,proceduresUsed,false)
                : await new QuestTools().CreateAsync(quests,request,intent,proceduresUsed,ct);
        }
        catch (JsonException)
        {
            return await InvalidPayloadEnvelope(log,"quest","Quest request could not be read.",intent,proceduresUsed,false);
        }
    }

    private static bool ClosedObject(JsonElement value, IReadOnlyCollection<string> required, IReadOnlyCollection<string>? optional = null)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var allowed = required.Concat(optional ?? []).ToHashSet(StringComparer.Ordinal);
        var found = value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        return required.All(found.Contains) && found.All(allowed.Contains);
    }

    private static async Task<ToolEnvelope> CommitEventTypeAsync(IEventTypeStore eventTypes, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, bool dryRun, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out EventTypePayload? value, out var error) || value is null || string.IsNullOrWhiteSpace(value.Id) || string.IsNullOrWhiteSpace(value.Category) || string.IsNullOrWhiteSpace(value.Name) || string.IsNullOrWhiteSpace(value.Schema)) return await InvalidPayloadEnvelope(log, "event-type", error ?? "Event type payload requires id, category, name, and schema.", intent, proceduresUsed, dryRun);
        return await new EventTypeTools().WriteAsync(eventTypes, log, value.Id, value.Category, value.Name, value.Schema, value.Description ?? string.Empty, value.Scope ?? string.Empty, value.Status, value.ChangeNote ?? string.Empty, intent, proceduresUsed, dryRun, cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitSubscriptionAsync(ISubscriptionStore subscriptions, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, bool dryRun, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out SubscriptionPayload? value, out var error) || value is null || string.IsNullOrWhiteSpace(value.Id) || string.IsNullOrWhiteSpace(value.Category) || string.IsNullOrWhiteSpace(value.EventTypeId) || string.IsNullOrWhiteSpace(value.EventMechanicId) || string.IsNullOrWhiteSpace(value.Mode) || !Enum.TryParse<SubscriptionMode>(value.Mode, true, out var mode)) return await InvalidPayloadEnvelope(log, "subscription", error ?? "Subscription payload requires id, category, eventTypeId, eventMechanicId, and mode (guard or reaction).", intent, proceduresUsed, dryRun);
        if (!string.IsNullOrWhiteSpace(value.Status) && !Enum.TryParse<SubscriptionStatus>(value.Status, true, out var _)) return await InvalidPayloadEnvelope(log, "subscription", "status must be draft, active, disabled, or archived.", intent, proceduresUsed, dryRun);
        return await new SubscriptionTools().WriteAsync(subscriptions, log, new WriteSubscriptionRequest { Id = value.Id, Category = value.Category, EventTypeId = value.EventTypeId, EventMechanicId = value.EventMechanicId, Mode = mode, Order = value.Order, FixedRoleEntityIdsJson = value.FixedRoleEntityIdsJson ?? "{}", RoleFromEventPayloadJson = value.RoleFromEventPayloadJson ?? "{}", FanoutSelectorJson = value.FanoutSelectorJson ?? "{}", TrackedEntityIdsJson = value.TrackedEntityIdsJson ?? "[]", PayloadEqualsJson = value.PayloadEqualsJson ?? "{}", MaxExecutionsPerChain = value.MaxExecutionsPerChain ?? 1, Scope = value.Scope ?? string.Empty, Status = string.IsNullOrWhiteSpace(value.Status) ? null : Enum.Parse<SubscriptionStatus>(value.Status, true), ChangeNote = value.ChangeNote ?? string.Empty }, intent, proceduresUsed, dryRun, cancellationToken);
    }

    /// <summary>
    /// The one commit that changes no content anywhere. It moves a notice's delivery state and can
    /// touch nothing else — a call able to edit what a notice says would turn evidence into a draft.
    /// </summary>
    private static async Task<ToolEnvelope> CommitNotificationAsync(INotificationStore notifications, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, bool dryRun, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out NotificationPayload? value, out var error) || value is null || string.IsNullOrWhiteSpace(value.Id) || string.IsNullOrWhiteSpace(value.State)) return await InvalidPayloadEnvelope(log, "notification", error ?? "Notification payload requires id and state (unread, read, or archived).", intent, proceduresUsed, dryRun);
        return await new NotificationTools().SetStateAsync(notifications, log, value.Id, value.State, intent, proceduresUsed, dryRun, cancellationToken);
    }

    /// <summary>
    /// D4: the expected shape travels inside the rejection, not as a pointer to where the shape
    /// lives. A session that guessed wrong then has everything it needs in the same round trip.
    /// </summary>
    private static Task<ToolEnvelope> InvalidPayloadEnvelope(
        IOperationLog log,
        string kind,
        string why,
        string intent,
        string[]? proceduresUsed,
        bool dryRun) =>
        ToolRunner.RunAsync(
            log,
            "commit",
            intent,
            $"commit:{kind}",
            proceduresUsed,
            () => Task.FromResult(ToolOutcome.Fail(
                "INVALID_PAYLOAD",
                $"{why} Expected payload for kind '{kind}': "
                + $"{VerbSurface.Commit(kind)?.Payload ?? "{}"}.",
                VerbSurface.CommitCall(kind, dryRun),
                $"Rejected invalid payload for '{kind}'.")),
            consumesReadEvidence: !dryRun);

    private static bool TryReadObject(
        string payload,
        out JsonDocument? document,
        out string? error)
    {
        document = null;
        error = null;

        if (string.IsNullOrWhiteSpace(payload))
        {
            error = "Payload is required and must be a JSON object.";
            return false;
        }

        try
        {
            document = JsonDocument.Parse(payload);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Payload must be a JSON object, not an array, string, or scalar.";
                document.Dispose();
                document = null;
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Payload is not valid JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryDeserialize<T>(
        JsonElement payload,
        out T? value,
        out string? error)
        where T : class
    {
        try
        {
            value = payload.Deserialize<T>(JsonOptions);
            error = value is null ? "Payload could not be read as the selected object shape." : null;
            return value is not null;
        }
        catch (JsonException ex)
        {
            value = null;
            error = $"Payload does not match the selected kind: {ex.Message}";
            return false;
        }
    }

    private sealed class ProcedurePayload
    {
        public string? Id { get; init; }
        public string? Category { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Instructions { get; init; }
        public string? Governs { get; init; }
        public string? Constraints { get; init; }
        public string? Status { get; init; }
        public string? ChangeNote { get; init; }
    }

    private sealed class ComponentPayload
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Schema { get; init; }
    }

    private sealed class EffectsPayload
    {
        public Effect[]? Effects { get; init; }
    }

    private sealed class MechanicPayload
    {
        public string? Id { get; init; }
        public string? Category { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Matches { get; init; }
        public string? Requirements { get; init; }
        public string? Source { get; init; }
        public string? Scope { get; init; }
        public string? Status { get; init; }
        public string? ChangeNote { get; init; }
    }

    private sealed class ActionPayload
    {
        public string? Intent { get; init; }
        public Dictionary<string, string>? RoleEntityIds { get; init; }
        public string? Input { get; init; }
        public string? Scope { get; init; }
        public long? Seed { get; init; }
    }
    private sealed class ItineraryAdvancePayload { public string? WorldId { get; init; } public string? TravellerId { get; init; } public string? DestinationLocationId { get; init; } public string? ItineraryFingerprint { get; init; } public int? NextLegIndex { get; init; } public string? GroundConveyanceId { get; init; } public string? AerialConveyanceId { get; init; } }
    private sealed class EventTypePayload { public string? Id { get; init; } public string? Category { get; init; } public string? Name { get; init; } public string? Description { get; init; } public string? Schema { get; init; } public string? Scope { get; init; } public string? Status { get; init; } public string? ChangeNote { get; init; } }
    private sealed class NotificationPayload { public string? Id { get; init; } public string? State { get; init; } }

    private sealed class SubscriptionPayload { public string? Id { get; init; } public string? Category { get; init; } public string? EventTypeId { get; init; } public string? EventMechanicId { get; init; } public string? Mode { get; init; } public int Order { get; init; } public string? FixedRoleEntityIdsJson { get; init; } public string? RoleFromEventPayloadJson { get; init; } public string? FanoutSelectorJson { get; init; } public string? TrackedEntityIdsJson { get; init; } public string? PayloadEqualsJson { get; init; } public int? MaxExecutionsPerChain { get; init; } public string? Scope { get; init; } public string? Status { get; init; } public string? ChangeNote { get; init; } }
}
