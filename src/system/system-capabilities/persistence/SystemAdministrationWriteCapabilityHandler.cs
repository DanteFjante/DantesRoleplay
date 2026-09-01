using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.ComponentTypeAdministration;
using DantesRoleplay.Ecs;
using DantesRoleplay.LegacyStateAdoption;
using DantesRoleplay.Projections;
using DantesRoleplay.RegistryAdministration;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;
using DantesRoleplay.StateSpaceAdministration;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>
/// Generic system-administration capability adapter. It derives current-state authority and calls
/// only the existing typed owner; it never accepts request tokens or expectations from a model.
/// </summary>
public sealed class SystemAdministrationWriteCapabilityHandler : ISystemWriteCapabilityHandler
{
    private readonly string _id;
    private readonly IApplicationRegistry _applications;
    private readonly ISourceRegistry _sources;
    private readonly IAllowedSourceRootCatalog _roots;
    private readonly IRegistryAdministrationService _registrations;
    private readonly IApplicationComponentTypeRegistry _componentTypes;
    private readonly IComponentTypeAdministrationService _componentAdministration;
    private readonly IBoundedJsonSchemaValidator _schemas;
    private readonly IApplicationPreviewService _previews;
    private readonly IApplicationActivationService _activations;
    private readonly IProjectionImpactService _impacts;
    private readonly IStateSpaceAdministrationService _stateSpaces;
    private readonly ILegacyStateAdoptionService _legacy;
    private readonly IApplicationExtensionRegistry _extensions;

    public SystemAdministrationWriteCapabilityHandler(
        string id,
        IApplicationRegistry applications,
        ISourceRegistry sources,
        IAllowedSourceRootCatalog roots,
        IRegistryAdministrationService registrations,
        IApplicationComponentTypeRegistry componentTypes,
        IComponentTypeAdministrationService componentAdministration,
        IBoundedJsonSchemaValidator schemas,
        IApplicationPreviewService previews,
        IApplicationActivationService activations,
        IProjectionImpactService impacts,
        IStateSpaceAdministrationService stateSpaces,
        ILegacyStateAdoptionService legacy,
        IApplicationExtensionRegistry? extensions = null)
    {
        _id = id;
        _applications = applications;
        _sources = sources;
        _roots = roots;
        _registrations = registrations;
        _componentTypes = componentTypes;
        _componentAdministration = componentAdministration;
        _schemas = schemas;
        _previews = previews;
        _activations = activations;
        _impacts = impacts;
        _stateSpaces = stateSpaces;
        _legacy = legacy;
        _extensions = extensions ?? new EmptyApplicationExtensionRegistry();
        Registration = BuildRegistration(id, roots.ListIds(128));
    }

    public SystemCapabilityRegistration Registration { get; }

    public async Task<SystemCapabilityWritePreflight> PreflightAsync(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return _id switch
            {
                SystemCapabilityIds.ApplicationRegister => PreflightApplication(input),
                SystemCapabilityIds.SourceRegister => PreflightSource(input, earlierSteps),
                SystemCapabilityIds.ExtensionRegister => PreflightExtension(input, earlierSteps),
                SystemCapabilityIds.ComponentTypeRegister => PreflightComponentType(input, earlierSteps),
                SystemCapabilityIds.ApplicationActivate => await PreflightActivationAsync(
                    input, earlierSteps, cancellationToken),
                SystemCapabilityIds.StateSpaceCreate => PreflightStateSpaceCreate(input, earlierSteps),
                SystemCapabilityIds.StateSpaceUpgrade => PreflightStateSpaceUpgrade(input, earlierSteps),
                SystemCapabilityIds.StateSpaceAdoptLegacy => PreflightLegacy(input, earlierSteps),
                _ => Fail("SYSTEM_CAPABILITY_UNKNOWN", "The write capability is not registered.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (Known(exception))
        {
            return Fail(Code(exception), SafeMessage(exception.Message));
        }
        catch (Exception)
        {
            return Fail("SYSTEM_CAPABILITY_PREFLIGHT_FAILED", "The capability preflight is unavailable.");
        }
    }

    public async Task<SystemCapabilityWriteHandlerResult> ExecuteAsync(
        JsonElement input,
        SystemCapabilityWriteExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return _id switch
            {
                SystemCapabilityIds.ApplicationRegister => await ExecuteApplicationAsync(input, context, cancellationToken),
                SystemCapabilityIds.SourceRegister => await ExecuteSourceAsync(input, context, cancellationToken),
                SystemCapabilityIds.ExtensionRegister => await ExecuteExtensionAsync(input, context, cancellationToken),
                SystemCapabilityIds.ComponentTypeRegister => await ExecuteComponentTypeAsync(input, context, cancellationToken),
                SystemCapabilityIds.ApplicationActivate => await ExecuteActivationAsync(input, context, cancellationToken),
                SystemCapabilityIds.StateSpaceCreate => await ExecuteStateSpaceCreateAsync(input, context, cancellationToken),
                SystemCapabilityIds.StateSpaceUpgrade => await ExecuteStateSpaceUpgradeAsync(input, context, cancellationToken),
                SystemCapabilityIds.StateSpaceAdoptLegacy => await ExecuteLegacyAsync(input, context, cancellationToken),
                _ => Failure("SYSTEM_CAPABILITY_UNKNOWN", "The write capability is not registered.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (Known(exception))
        {
            return Failure(Code(exception), SafeMessage(exception.Message));
        }
        catch (Exception)
        {
            return Failure("SYSTEM_CAPABILITY_WRITE_FAILED",
                "The system administration transaction failed without a safe result.");
        }
    }

    private SystemCapabilityWritePreflight PreflightApplication(JsonElement input)
    {
        var registration = Application(input);
        var existing = _applications.Get(registration.Id);
        var described = _applications.Describe(registration.Id);
        if (described is not null && (described.DisplayName != registration.DisplayName
            || described.Description != registration.Description))
            return Fail("REGISTRATION_CONFLICT", "Application display metadata is immutable.");
        if (registration.BaseApplications.Any(id => _applications.Get(id) is null))
            return Fail("APPLICATION_UNKNOWN", "Every base application must already be registered.");
        return Ready(new
        {
            applicationId = registration.Id.Value,
            current = existing?.Fingerprint,
            bases = registration.BaseApplications.Select(id => _applications.Get(id)!.Fingerprint).ToArray()
        }, $"Register immutable application '{registration.Id.Value}'.",
            [$"application:{registration.Id.Value}"]);
    }

    private SystemCapabilityWritePreflight PreflightSource(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlier)
    {
        var registration = Source(input);
        if (!_roots.ListIds(128).Contains(registration.AllowedRootId, StringComparer.Ordinal))
            return Fail("SOURCE_ROOT_UNKNOWN", "The selected allowed-root ID is not configured.");
        var app = _applications.Get(registration.ApplicationId);
        var prerequisite = Earlier(earlier, SystemCapabilityIds.ApplicationRegister, registration.ApplicationId.Value);
        if (app is null && prerequisite.Count > 0)
            return Deferred(new { registration.ApplicationId.Value, registration.SourceId, roots = _roots.ListIds(128) },
                $"Register source '{registration.SourceId}' after its application exists.",
                [$"application:{registration.ApplicationId.Value}", $"source:{registration.ApplicationId.Value}/{registration.SourceId}"],
                prerequisite);
        if (app is null) return Fail("APPLICATION_UNKNOWN", "The source application is not registered.");
        var existing = _sources.Get(registration.ApplicationId, registration.SourceId);
        if (existing is not null && SourceRegistrationFingerprint.Compute(existing) != SourceRegistrationFingerprint.Compute(registration))
            return Fail("REGISTRATION_CONFLICT", "The source ID already has different immutable metadata.");
        return Ready(new
        {
            applicationFingerprint = app.Fingerprint,
            current = existing is null ? null : SourceRegistrationFingerprint.Compute(existing),
            roots = _roots.ListIds(128)
        }, $"Register immutable source '{registration.SourceId}' for '{registration.ApplicationId.Value}'.",
            [$"application:{registration.ApplicationId.Value}", $"source:{registration.ApplicationId.Value}/{registration.SourceId}"]);
    }

    private SystemCapabilityWritePreflight PreflightExtension(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlier)
    {
        var registration = Extension(input);
        var dependencies = Earlier(earlier, SystemCapabilityIds.SourceRegister,
            registration.ApplicationId.Value);
        if (dependencies.Count > 0)
            return Deferred(new { applicationId = registration.ApplicationId.Value, registration.ExtensionId },
                $"Register extension '{registration.ExtensionId}' after its sources exist.",
                [$"application:{registration.ApplicationId.Value}",
                 $"extension:{registration.ApplicationId.Value}/{registration.ExtensionId}"], dependencies);
        if (_applications.Get(registration.ApplicationId) is null)
            return Fail("APPLICATION_UNKNOWN", "The extension application is not registered.");
        ApplicationExtensionRegistration normalized;
        try
        {
            normalized = ApplicationExtensionValidation.Normalize(registration, _sources,
                _extensions.For(registration.ApplicationId)
                    .Where(value => value.ExtensionId != registration.ExtensionId).ToArray());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Fail("INVALID_EXTENSION", SafeMessage(exception.Message));
        }
        var fingerprint = ApplicationExtensionRegistrationFingerprint.Compute(normalized);
        var current = _extensions.Get(registration.ApplicationId, registration.ExtensionId);
        if (current is not null
            && ApplicationExtensionRegistrationFingerprint.Compute(current) != fingerprint)
            return Fail("REGISTRATION_CONFLICT", "The extension ID already has different immutable metadata.");
        return Ready(new
        {
            current = current is null ? null : fingerprint,
            registrationFingerprint = fingerprint,
            sourceIds = normalized.SourceIds,
            namespaceIds = normalized.NamespaceIds
        }, $"Register immutable extension '{registration.ExtensionId}'.",
            [$"application:{registration.ApplicationId.Value}",
             $"extension:{registration.ApplicationId.Value}/{registration.ExtensionId}"]);
    }

    private SystemCapabilityWritePreflight PreflightComponentType(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlier)
    {
        var request = ComponentType(input);
        var compiled = _schemas.Compile(request.SchemaJson);
        if (!compiled.IsAccepted)
            return Fail("COMPONENT_SCHEMA_INVALID", "The component schema is not accepted by the bounded profile.");
        var app = _applications.Get(request.ApplicationId);
        var prerequisite = Earlier(earlier, SystemCapabilityIds.ApplicationRegister, request.ApplicationId.Value);
        if (app is null && prerequisite.Count > 0)
            return Deferred(new { request.ApplicationId.Value, request.QualifiedTypeId, compiled.SchemaHash },
                $"Register component type '{request.QualifiedTypeId}' after its application exists.",
                [$"application:{request.ApplicationId.Value}", $"component-type:{request.QualifiedTypeId}"], prerequisite);
        if (app is null) return Fail("APPLICATION_UNKNOWN", "The component-type application is not registered.");
        var current = _componentTypes.GetLatest(request.QualifiedTypeId);
        var impact = _impacts.Analyze(request.ApplicationId);
        return Ready(new
        {
            applicationFingerprint = app.Fingerprint,
            currentSchemaHash = current?.SchemaHash,
            targetSchemaHash = compiled.SchemaHash,
            dependencyGraphFingerprint = impact.GraphFingerprint
        }, $"Register a version of component type '{request.QualifiedTypeId}'.",
            [$"application:{request.ApplicationId.Value}", $"component-type:{request.QualifiedTypeId}",
             $"dependency-graph:{request.ApplicationId.Value}#{impact.GraphFingerprint}"]);
    }

    private async Task<SystemCapabilityWritePreflight> PreflightActivationAsync(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlier,
        CancellationToken cancellationToken)
    {
        var appId = App(input);
        var dependencies = Earlier(earlier,
            [SystemCapabilityIds.ApplicationRegister, SystemCapabilityIds.SourceRegister,
             SystemCapabilityIds.ExtensionRegister,
             SystemCapabilityIds.ComponentTypeRegister], appId.Value);
        if (dependencies.Count > 0)
            return Deferred(new { applicationId = appId.Value, dependencies },
                $"Build and activate '{appId.Value}' after earlier registration steps.",
                [$"application:{appId.Value}", $"activation:{appId.Value}"], dependencies);
        var app = _applications.Get(appId);
        if (app is null) return Fail("APPLICATION_UNKNOWN", "The activation application is not registered.");
        var selectedExtensions = OptionalExtensionIds(input);
        var selectedSources = OptionalSourceIds(input);
        var preview = selectedExtensions is not null && selectedSources is not null
            ? await _previews.PreviewAsync(appId, selectedSources, selectedExtensions, cancellationToken)
            : selectedExtensions is not null
                ? await _previews.PreviewExtensionsAsync(appId, selectedExtensions, cancellationToken)
            : selectedSources is null
                ? await _previews.PreviewAsync(appId, cancellationToken)
                : await _previews.PreviewAsync(appId, selectedSources, cancellationToken);
        if (!preview.IsValid) return Fail("PREVIEW_INVALID", "The current source overlay preview is invalid.");
        var current = _activations.Current(appId);
        return Ready(new
        {
            applicationFingerprint = app.Fingerprint,
            preview.PreviewFingerprint,
            currentActiveFingerprint = current?.ActivationFingerprint,
            preview.ResolutionFingerprint,
            extensionIds = preview.ExtensionIds,
            baseSourceIds = selectedSources,
            sourceIds = preview.Sources.Select(value => value.SourceId).ToArray()
        }, $"Activate the exact current source preview for '{appId.Value}'.",
            [$"application:{appId.Value}", $"activation:{appId.Value}"]);
    }

    private SystemCapabilityWritePreflight PreflightStateSpaceCreate(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlier)
    {
        var (app, stateSpaceId) = StateSpace(input);
        var scope = StateSpaceScope(input);
        if (_stateSpaces.Get(stateSpaceId) is not null)
            return Fail("STATE_SPACE_EXISTS", "The state-space ID already exists.");
        var dependencies = Earlier(earlier, SystemCapabilityIds.ApplicationActivate, app.Value);
        if (dependencies.Count > 0)
            return Deferred(new { applicationId = app.Value, stateSpaceId,
                    scope = EcsComponentRolePolicyParser.ScopeName(scope), dependencies },
                $"Create state space '{stateSpaceId}' after activation.",
                [$"application:{app.Value}", $"state-space:{stateSpaceId}"], dependencies);
        var active = _activations.Current(app);
        if (active is null) return Fail("ACTIVATION_REQUIRED", "The application must be active first.");
        return Ready(new { active.ActivationFingerprint, stateSpaceId,
                scope = EcsComponentRolePolicyParser.ScopeName(scope) },
            $"Create empty state space '{stateSpaceId}' for '{app.Value}'.",
            [$"application:{app.Value}", $"state-space:{stateSpaceId}"]);
    }

    private SystemCapabilityWritePreflight PreflightStateSpaceUpgrade(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlier)
    {
        var (app, stateSpaceId) = StateSpace(input);
        var current = _stateSpaces.Get(stateSpaceId);
        if (current is null) return Fail("STATE_SPACE_UNKNOWN", "The state space does not exist.");
        if (current.ApplicationId != app)
            return Fail("STATE_SPACE_APPLICATION_MISMATCH", "The state space belongs to another application.");
        var dependencies = Earlier(earlier, SystemCapabilityIds.ApplicationActivate, app.Value);
        if (dependencies.Count > 0)
            return Deferred(new { current.BindingFingerprint, dependencies },
                $"Upgrade state space '{stateSpaceId}' after activation.",
                [$"application:{app.Value}", $"state-space:{stateSpaceId}"], dependencies);
        var active = _activations.Current(app);
        if (active is null) return Fail("ACTIVATION_REQUIRED", "The application must be active first.");
        return Ready(new { current.BindingFingerprint, active.ActivationFingerprint },
            $"Upgrade empty state space '{stateSpaceId}' to the current activation.",
            [$"application:{app.Value}", $"state-space:{stateSpaceId}"]);
    }

    private SystemCapabilityWritePreflight PreflightLegacy(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlier)
    {
        var request = Legacy(input, "");
        if (_stateSpaces.Get(request.StateSpaceId) is not null)
            return Fail("STATE_SPACE_EXISTS", "Legacy adoption requires a new state-space ID.");
        var dependencies = Earlier(earlier,
            [SystemCapabilityIds.ApplicationRegister, SystemCapabilityIds.ComponentTypeRegister,
             SystemCapabilityIds.ApplicationActivate], request.ApplicationId.Value);
        if (dependencies.Count > 0)
            return Deferred(new
            {
                applicationId = request.ApplicationId.Value,
                request.StateSpaceId,
                componentMappings = request.ComponentMappings.Count,
                relationshipMappings = request.RelationshipMappings.Count
            }, $"Adopt legacy state into '{request.StateSpaceId}' after earlier contract and activation steps.",
                [$"application:{request.ApplicationId.Value}", $"state-space:{request.StateSpaceId}"], dependencies);
        var active = _activations.Current(request.ApplicationId);
        if (active is null) return Fail("ACTIVATION_REQUIRED", "The application must be active first.");
        return Ready(new
        {
            active.ActivationFingerprint,
            request.StateSpaceId,
            mappings = Hash(new { request.ComponentMappings, request.RelationshipMappings })
        }, $"Adopt the complete legacy graph into new state space '{request.StateSpaceId}'.",
            [$"application:{request.ApplicationId.Value}", $"state-space:{request.StateSpaceId}"]);
    }

    private async Task<SystemCapabilityWriteHandlerResult> ExecuteApplicationAsync(
        JsonElement input, SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken)
    {
        var registration = Application(input);
        var expected = OptionalText(ExecutionEvidence(context), "current");
        var owner = new RegistryAdministrationContext(context.RequestToken, expected, context.Intent,
            context.ProceduresUsed, context.AuthorizationEvidence);
        _ = await _registrations.PreviewApplicationAsync(registration, owner, cancellationToken);
        var receipt = await _registrations.RegisterApplicationAsync(registration, owner, cancellationToken);
        var current = _applications.Get(registration.Id)
            ?? throw new InvalidOperationException("Application read-back was unavailable.");
        var data = SystemCapabilityJson.Element(new
        {
            receipt.Outcome,
            application = new { id = current.ApplicationId.Value, current.Revision, current.Fingerprint }
        });
        return Success(data, receipt.OperationId);
    }

    private async Task<SystemCapabilityWriteHandlerResult> ExecuteSourceAsync(
        JsonElement input, SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken)
    {
        var registration = Source(input);
        if (!_roots.ListIds(128).Contains(registration.AllowedRootId, StringComparer.Ordinal))
            return Failure("SOURCE_ROOT_UNKNOWN", "The selected allowed-root ID is not configured.");
        var expected = OptionalText(ExecutionEvidence(context), "current");
        var owner = new RegistryAdministrationContext(context.RequestToken, expected, context.Intent,
            context.ProceduresUsed, context.AuthorizationEvidence);
        _ = await _registrations.PreviewSourceAsync(registration, owner, cancellationToken);
        var receipt = await _registrations.RegisterSourceAsync(registration, owner, cancellationToken);
        var current = _sources.Get(registration.ApplicationId, registration.SourceId)
            ?? throw new InvalidOperationException("Source read-back was unavailable.");
        var fingerprint = SourceRegistrationFingerprint.Compute(current);
        var data = SystemCapabilityJson.Element(new
        {
            receipt.Outcome,
            source = new { applicationId = current.ApplicationId.Value, current.SourceId, fingerprint }
        });
        return Success(data, receipt.OperationId);
    }

    private async Task<SystemCapabilityWriteHandlerResult> ExecuteExtensionAsync(
        JsonElement input, SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken)
    {
        var registration = Extension(input);
        var expected = OptionalText(ExecutionEvidence(context), "current");
        var owner = new RegistryAdministrationContext(context.RequestToken, expected, context.Intent,
            context.ProceduresUsed, context.AuthorizationEvidence);
        _ = await _registrations.PreviewExtensionAsync(registration, owner, cancellationToken);
        var receipt = await _registrations.RegisterExtensionAsync(registration, owner, cancellationToken);
        var current = _extensions.Get(registration.ApplicationId, registration.ExtensionId)
            ?? throw new InvalidOperationException("Extension read-back was unavailable.");
        return Success(SystemCapabilityJson.Element(new
        {
            receipt.Outcome,
            extension = new
            {
                applicationId = current.ApplicationId.Value,
                current.ExtensionId,
                fingerprint = ApplicationExtensionRegistrationFingerprint.Compute(current)
            }
        }), receipt.OperationId);
    }

    private async Task<SystemCapabilityWriteHandlerResult> ExecuteComponentTypeAsync(
        JsonElement input, SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken)
    {
        var request = ComponentType(input);
        var expected = OptionalText(ExecutionEvidence(context), "currentSchemaHash");
        var owner = new ComponentTypeAdministrationContext(context.RequestToken, expected, context.Intent,
            context.ProceduresUsed, context.AuthorizationEvidence);
        _ = await _componentAdministration.PreviewAsync(request, owner, cancellationToken);
        var receipt = await _componentAdministration.RegisterAsync(request, owner, cancellationToken);
        var current = _componentTypes.GetLatest(request.QualifiedTypeId)
            ?? throw new InvalidOperationException("Component-type read-back was unavailable.");
        var data = SystemCapabilityJson.Element(new
        {
            receipt.Outcome,
            componentType = new
            {
                applicationId = current.Owner.Value,
                current.QualifiedId,
                current.Version,
                current.ProfileId,
                current.SchemaHash
            }
        });
        return Success(data, receipt.OperationId);
    }

    private async Task<SystemCapabilityWriteHandlerResult> ExecuteActivationAsync(
        JsonElement input, SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken)
    {
        var app = App(input);
        var evidence = ExecutionEvidence(context);
        var hasBaseSources = evidence.TryGetProperty("baseSourceIds", out var baseSources)
            && baseSources.ValueKind == JsonValueKind.Array;
        var request = new ApplicationActivationRequest(
            app, Text(evidence, "previewFingerprint"), OptionalText(evidence, "currentActiveFingerprint"),
            hasBaseSources
                ? baseSources.EnumerateArray().Select(value => value.GetString()!).ToArray()
                : evidence.GetProperty("sourceIds").EnumerateArray().Select(value => value.GetString()!).ToArray())
        {
            ExtensionIds = evidence.GetProperty("extensionIds").EnumerateArray()
                .Select(value => value.GetString()!).ToArray()
        };
        if (request.ExtensionIds.Count != 0 && !hasBaseSources)
            request = request with { SourceIds = null };
        var owner = new ApplicationActivationContext(context.RequestToken, context.Intent,
            context.ProceduresUsed, context.AuthorizationEvidence);
        _ = await _activations.PreviewAsync(request, owner, cancellationToken);
        var receipt = await _activations.ActivateAsync(request, owner, cancellationToken);
        var current = _activations.Current(app)
            ?? throw new InvalidOperationException("Activation read-back was unavailable.");
        var data = SystemCapabilityJson.Element(new
        {
            receipt.Outcome,
            activation = new
            {
                applicationId = app.Value,
                current.ActivationRevision,
                current.ApplicationRevision,
                current.ApplicationFingerprint,
                current.PreviewFingerprint,
                current.ResolutionFingerprint,
                current.ActivationFingerprint,
                current.DependencyGraphFingerprint,
                current.DependencyCoverageVersion,
                current.DependencyCoverageComplete,
                sourceCount = current.Sources.Count,
                sourceIds = current.Sources.Select(value => value.SourceId).ToArray(),
                extensionIds = current.Extensions.Select(value => value.ExtensionId).ToArray(),
                winnerCount = current.Winners.Count
            }
        });
        return Success(data, receipt.OperationId);
    }

    private async Task<SystemCapabilityWriteHandlerResult> ExecuteStateSpaceCreateAsync(
        JsonElement input, SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken)
    {
        var (app, stateSpaceId) = StateSpace(input);
        var evidence = ExecutionEvidence(context);
        var request = new StateSpaceCreationRequest(
            stateSpaceId, app, Text(evidence, "activationFingerprint"), null)
        {
            Scope = EcsComponentRolePolicyParser.ParseScope(Text(evidence, "scope"))
        };
        var owner = new StateSpaceCreationContext(context.RequestToken, context.Intent,
            context.ProceduresUsed, context.AuthorizationEvidence);
        _ = await _stateSpaces.PreviewCreateAsync(request, owner, cancellationToken);
        var receipt = await _stateSpaces.CreateAsync(request, owner, cancellationToken);
        var current = _stateSpaces.Get(stateSpaceId)
            ?? throw new InvalidOperationException("State-space read-back was unavailable.");
        return Success(SystemCapabilityJson.Element(new
        {
            receipt.Outcome,
            binding = Binding(current)
        }), receipt.OperationId);
    }

    private async Task<SystemCapabilityWriteHandlerResult> ExecuteStateSpaceUpgradeAsync(
        JsonElement input, SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken)
    {
        var (app, stateSpaceId) = StateSpace(input);
        var evidence = ExecutionEvidence(context);
        var request = new StateSpaceUpgradeRequest(
            stateSpaceId, app, Text(evidence, "activationFingerprint"), Text(evidence, "bindingFingerprint"));
        var owner = new StateSpaceUpgradeContext(context.RequestToken, context.Intent,
            context.ProceduresUsed, context.AuthorizationEvidence);
        _ = await _stateSpaces.PreviewUpgradeAsync(request, owner, cancellationToken);
        var receipt = await _stateSpaces.UpgradeAsync(request, owner, cancellationToken);
        var current = _stateSpaces.Get(stateSpaceId)
            ?? throw new InvalidOperationException("State-space read-back was unavailable.");
        return Success(SystemCapabilityJson.Element(new
        {
            receipt.Outcome,
            previousBinding = Binding(receipt.PreviousBinding),
            binding = Binding(current),
            receipt.Compatibility
        }), receipt.OperationId);
    }

    private async Task<SystemCapabilityWriteHandlerResult> ExecuteLegacyAsync(
        JsonElement input, SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken)
    {
        var app = App(input);
        var request = Legacy(input, Text(ExecutionEvidence(context), "activationFingerprint"));
        var owner = new LegacyStateAdoptionContext(context.RequestToken, context.Intent,
            context.ProceduresUsed, context.AuthorizationEvidence);
        _ = await _legacy.PreviewAsync(request, owner, cancellationToken);
        var receipt = await _legacy.AdoptAsync(request, owner, cancellationToken);
        var current = _stateSpaces.Get(request.StateSpaceId)
            ?? throw new InvalidOperationException("Adopted state-space read-back was unavailable.");
        return Success(SystemCapabilityJson.Element(new
        {
            receipt.Outcome,
            receipt.StateSpaceId,
            applicationId = receipt.ApplicationId.Value,
            receipt.Inventory,
            binding = Binding(current)
        }), receipt.OperationId);
    }

    private static object Binding(StateSpaceBindingSummary value) => new
    {
        value.StateSpaceId,
        applicationId = value.ApplicationId.Value,
        value.ApplicationRevision,
        value.ApplicationFingerprint,
        value.ActiveFingerprint,
        value.ResolutionFingerprint,
        scope = EcsComponentRolePolicyParser.ScopeName(value.Scope),
        value.BindingRevision,
        value.BindingFingerprint
    };

    private static ApplicationRegistration Application(JsonElement input) => new(
        ApplicationIdentifier.Parse(Text(input, "applicationId")),
        Text(input, "displayName"),
        input.GetProperty("description").GetString()!,
        input.GetProperty("baseApplications").EnumerateArray()
            .Select(value => ApplicationIdentifier.Parse(value.GetString()!)).ToArray());

    private static SourceRegistration Source(JsonElement input) => new(
        ApplicationIdentifier.Parse(Text(input, "applicationId")),
        Text(input, "sourceId"), Text(input, "allowedRootId"),
        Text(input, "relativePathOrGlob"),
        Text(input, "trust") == "trusted" ? SourceTrust.Trusted : SourceTrust.Untrusted,
        input.GetProperty("precedence").GetInt32(), Text(input, "logicalIdentity"));

    private static ApplicationExtensionRegistration Extension(JsonElement input) => new(
        App(input), Text(input, "extensionId"), Text(input, "displayName"), Text(input, "description"),
        Text(input, "classification"),
        Strings(input, "sourceIds"), Strings(input, "namespaceIds"),
        Strings(input, "dependencies"), Strings(input, "conflictsWith"),
        Strings(input, "higherPriorityThan"), input.GetProperty("overridesBase").GetBoolean());

    private static ComponentTypeRegistrationRequest ComponentType(JsonElement input) => new(
        ApplicationIdentifier.Parse(Text(input, "applicationId")),
        Text(input, "qualifiedTypeId"), input.GetProperty("schemaJson").GetString()!);

    private static ApplicationIdentifier App(JsonElement input) =>
        ApplicationIdentifier.Parse(Text(input, "applicationId"));

    private static (ApplicationIdentifier ApplicationId, string StateSpaceId) StateSpace(JsonElement input) =>
        (App(input), Text(input, "stateSpaceId"));

    private static EcsStateSpaceScope StateSpaceScope(JsonElement input) =>
        input.TryGetProperty("scope", out var scope)
            ? EcsComponentRolePolicyParser.ParseScope(scope.GetString()!)
            : EcsStateSpaceScope.Runtime;

    private static LegacyStateAdoptionRequest Legacy(JsonElement input, string activeFingerprint) => new(
        Text(input, "stateSpaceId"), App(input), activeFingerprint,
        input.GetProperty("componentMappings").EnumerateArray().Select(value =>
            new LegacyComponentTypeMapping(Text(value, "legacyDefinitionId"), new EcsComponentReference(
                Text(value, "qualifiedTypeId"), value.GetProperty("typeVersion").GetInt32(),
                Text(value, "schemaHash")))).ToArray(),
        input.GetProperty("relationshipMappings").EnumerateArray().Select(value =>
            new LegacyRelationshipKindMapping(Text(value, "legacyKind"), Text(value, "qualifiedKind"))).ToArray());

    private static string Text(JsonElement input, string name) => input.GetProperty(name).GetString()!;

    private static string? OptionalText(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static IReadOnlyList<string>? OptionalSourceIds(JsonElement input) =>
        input.TryGetProperty("sourceIds", out var value)
            ? Array.AsReadOnly(value.EnumerateArray().Select(item => item.GetString()!).ToArray())
            : null;

    private static IReadOnlyList<string>? OptionalExtensionIds(JsonElement input) =>
        input.TryGetProperty("extensionIds", out var value)
            ? Array.AsReadOnly(value.EnumerateArray().Select(item => item.GetString()!).ToArray())
            : null;

    private static IReadOnlyList<string> Strings(JsonElement input, string name) =>
        Array.AsReadOnly(input.GetProperty(name).EnumerateArray()
            .Select(value => value.GetString()!).ToArray());

    private static JsonElement ExecutionEvidence(SystemCapabilityWriteExecutionContext context)
    {
        using var document = JsonDocument.Parse(context.ExecutionEvidenceJson);
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<string> Earlier(
        IReadOnlyList<SystemCapabilityEarlierStep> values,
        string capabilityId,
        string applicationId) => Earlier(values, [capabilityId], applicationId);

    private static IReadOnlyList<string> Earlier(
        IReadOnlyList<SystemCapabilityEarlierStep> values,
        IReadOnlyList<string> capabilityIds,
        string applicationId) => values.Where(value => capabilityIds.Contains(value.CapabilityId, StringComparer.Ordinal)
            && ApplicationId(value.InputJson) == applicationId).Select(value => value.StepId).ToArray();

    private static string ApplicationId(string inputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            return document.RootElement.TryGetProperty("applicationId", out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        }
        catch (JsonException) { return ""; }
    }

    private static SystemCapabilityWritePreflight Ready(object evidence, string summary, string[] affected)
    {
        var value = JsonSerializer.SerializeToElement(evidence, SystemCapabilityJson.Web);
        return SystemCapabilityWritePreflight.Ready(
            Hash(value), summary, Array.AsReadOnly(affected), value.GetRawText());
    }

    private static SystemCapabilityWritePreflight Deferred(
        object evidence, string summary, string[] affected, IReadOnlyList<string> dependencies) =>
        DeferredEvidence(evidence, summary, affected, dependencies);

    private static SystemCapabilityWritePreflight DeferredEvidence(
        object evidence, string summary, string[] affected, IReadOnlyList<string> dependencies)
    {
        var value = JsonSerializer.SerializeToElement(evidence, SystemCapabilityJson.Web);
        return SystemCapabilityWritePreflight.Deferred(
            Hash(value), summary, Array.AsReadOnly(affected), dependencies, value.GetRawText());
    }

    private static SystemCapabilityWritePreflight Fail(string code, string message) =>
        SystemCapabilityWritePreflight.Failure(code, message, "Inspect current system capabilities and retry.");

    private static SystemCapabilityWriteHandlerResult Success(JsonElement data, string operationId) =>
        SystemCapabilityWriteHandlerResult.Success(data, operationId, Hash(data));

    private static SystemCapabilityWriteHandlerResult Failure(string code, string message) =>
        SystemCapabilityWriteHandlerResult.Failure(code, message, "Inspect current system state and prepare a new plan.");

    private static string Hash(object value) => Hash(JsonSerializer.SerializeToElement(value, SystemCapabilityJson.Web));
    private static string Hash(JsonElement value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(value.GetRawText())));

    private static bool Known(Exception exception) => exception is
        ArgumentException or RegistryAdministrationException or ComponentTypeAdministrationException or
        ApplicationPreviewException or ApplicationActivationException or ProjectionImpactException or
        StateSpaceAdministrationException or LegacyStateAdoptionException;

    private static string Code(Exception exception) => exception switch
    {
        RegistryAdministrationException value => value.Code,
        ComponentTypeAdministrationException value => value.Code,
        ApplicationPreviewException value => value.Code,
        ApplicationActivationException value => value.Code,
        ProjectionImpactException value => value.Code,
        StateSpaceAdministrationException value => value.Code,
        LegacyStateAdoptionException value => value.Code,
        _ => "INVALID_PAYLOAD"
    };

    private static string SafeMessage(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 300 && !value.Any(char.IsControl)
            ? value : "The system capability rejected its current input.";

    private static SystemCapabilityRegistration BuildRegistration(
        string id,
        IReadOnlyList<string> rootIds)
    {
        var (owner, description, input, output) = id switch
        {
            SystemCapabilityIds.ApplicationRegister => ("registry-administration",
                "Register immutable application metadata.", ApplicationInput, ApplicationOutput),
            SystemCapabilityIds.SourceRegister => ("registry-administration",
                $"Register an immutable allowed-root-relative source. Configured root IDs: {RootDescription(rootIds)}.",
                SourceInput, SourceOutput),
            SystemCapabilityIds.ExtensionRegister => ("registry-administration",
                "Register immutable application-extension sources, namespaces, compatibility, and precedence.",
                ExtensionInput, ExtensionOutput),
            SystemCapabilityIds.ComponentTypeRegister => ("component-type-administration",
                "Compile and register one versioned application-owned component-type schema.", ComponentTypeInput, ComponentTypeOutput),
            SystemCapabilityIds.ApplicationActivate => ("application-activation",
                "Activate one exact valid deterministic application-extension set.", ActivationInput, ActivationOutput),
            SystemCapabilityIds.StateSpaceCreate => ("state-space-administration",
                "Create one empty runtime or application-publication state space bound to the current active application.", StateSpaceInput, StateSpaceOutput),
            SystemCapabilityIds.StateSpaceUpgrade => ("state-space-administration",
                "Upgrade one empty state space to the current active application binding.", StateSpaceInput, StateSpaceUpgradeOutput),
            SystemCapabilityIds.StateSpaceAdoptLegacy => ("legacy-state-adoption",
                "Copy the complete legacy ECS graph into one new state space using explicit exact mappings.", LegacyInput, LegacyOutput),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown system write capability.")
        };
        return new(id, 1, owner, description, SystemCapabilityMode.Write, input, output,
            ["procedure.system.use"], PrivateOperatorCapability.Modify,
            SystemCapabilitySensitivity.PrivateOperatorMetadata, true, true);
    }

    private static string RootDescription(IReadOnlyList<string> roots)
    {
        var value = roots.Count == 0 ? "none" : string.Join(", ", roots.Take(32));
        return value.Length <= 240 ? value : value[..240];
    }

    private const string ApplicationInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId","displayName","description","baseApplications"],"properties":{
      "applicationId":{"type":"string","minLength":1,"maxLength":63},"displayName":{"type":"string","minLength":1,"maxLength":200},
      "description":{"type":"string","maxLength":2000},"baseApplications":{"type":"array","maxItems":32,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":63}}}}
    """;
    private const string SourceInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId","sourceId","allowedRootId","relativePathOrGlob","trust","precedence","logicalIdentity"],"properties":{
      "applicationId":{"type":"string","minLength":1,"maxLength":63},"sourceId":{"type":"string","minLength":1,"maxLength":200},
      "allowedRootId":{"type":"string","minLength":1,"maxLength":63},"relativePathOrGlob":{"type":"string","minLength":1,"maxLength":1000},
      "trust":{"enum":["trusted","untrusted"]},"precedence":{"type":"integer","minimum":-1000000,"maximum":1000000},
      "logicalIdentity":{"type":"string","minLength":1,"maxLength":200}}}
    """;
    private const string ExtensionInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId","extensionId","displayName","description","classification","sourceIds","namespaceIds","dependencies","conflictsWith","higherPriorityThan","overridesBase"],"properties":{
      "applicationId":{"type":"string","minLength":1,"maxLength":63},"extensionId":{"type":"string","pattern":"^[a-z][a-z0-9-]{0,62}$"},
      "displayName":{"type":"string","minLength":1,"maxLength":120},"description":{"type":"string","minLength":1,"maxLength":2000},
      "classification":{"enum":["homebrew","compatibility","third-party"]},
      "sourceIds":{"type":"array","minItems":1,"maxItems":100,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}},
      "namespaceIds":{"type":"array","minItems":1,"maxItems":100,"uniqueItems":true,"items":{"type":"string","minLength":3,"maxLength":200}},
      "dependencies":{"type":"array","maxItems":100,"uniqueItems":true,"items":{"type":"string","maxLength":63}},
      "conflictsWith":{"type":"array","maxItems":100,"uniqueItems":true,"items":{"type":"string","maxLength":63}},
      "higherPriorityThan":{"type":"array","maxItems":100,"uniqueItems":true,"items":{"type":"string","maxLength":63}},
      "overridesBase":{"type":"boolean"}}}
    """;
    private const string ComponentTypeInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId","qualifiedTypeId","schemaJson"],"properties":{
      "applicationId":{"type":"string","minLength":1,"maxLength":63},"qualifiedTypeId":{"type":"string","minLength":3,"maxLength":200},
      "schemaJson":{"type":"string","maxLength":65536}}}
    """;
    private const string ApplicationIdInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63}}}
    """;
    private const string ActivationInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"extensionIds":{"type":"array","maxItems":100,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":63}},"sourceIds":{"type":"array","minItems":1,"maxItems":100,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}}}}
    """;
    private const string StateSpaceInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId","stateSpaceId"],"properties":{
      "applicationId":{"type":"string","minLength":1,"maxLength":63},"stateSpaceId":{"type":"string","minLength":1,"maxLength":200},
      "scope":{"enum":["runtime-state-space","application-publication"]}}}
    """;
    private const string LegacyInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId","stateSpaceId","componentMappings","relationshipMappings"],"properties":{
      "applicationId":{"type":"string","minLength":1,"maxLength":63},"stateSpaceId":{"type":"string","minLength":1,"maxLength":200},
      "componentMappings":{"type":"array","maxItems":256,"items":{"type":"object","additionalProperties":false,"required":["legacyDefinitionId","qualifiedTypeId","typeVersion","schemaHash"],"properties":{
        "legacyDefinitionId":{"type":"string","minLength":1,"maxLength":200},"qualifiedTypeId":{"type":"string","minLength":3,"maxLength":200},
        "typeVersion":{"type":"integer","minimum":1},"schemaHash":{"type":"string","minLength":64,"maxLength":64}}}},
      "relationshipMappings":{"type":"array","maxItems":256,"items":{"type":"object","additionalProperties":false,"required":["legacyKind","qualifiedKind"],"properties":{
        "legacyKind":{"type":"string","minLength":1,"maxLength":100},"qualifiedKind":{"type":"string","minLength":3,"maxLength":200}}}}}}
    """;

    private const string ApplicationOutput = """
    {"type":"object","additionalProperties":false,"required":["outcome","application"],"properties":{"outcome":{"type":"string","minLength":1,"maxLength":80},"application":{"type":"object","additionalProperties":false,"required":["id","revision","fingerprint"],"properties":{"id":{"type":"string","minLength":1,"maxLength":63},"revision":{"type":"integer","minimum":1},"fingerprint":{"type":"string","minLength":64,"maxLength":64}}}}}
    """;
    private const string SourceOutput = """
    {"type":"object","additionalProperties":false,"required":["outcome","source"],"properties":{"outcome":{"type":"string","minLength":1,"maxLength":80},"source":{"type":"object","additionalProperties":false,"required":["applicationId","sourceId","fingerprint"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"sourceId":{"type":"string","minLength":1,"maxLength":200},"fingerprint":{"type":"string","minLength":64,"maxLength":64}}}}}
    """;
    private const string ExtensionOutput = """
    {"type":"object","additionalProperties":false,"required":["outcome","extension"],"properties":{"outcome":{"type":"string","minLength":1,"maxLength":80},"extension":{"type":"object","additionalProperties":false,"required":["applicationId","extensionId","fingerprint"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"extensionId":{"type":"string","minLength":1,"maxLength":63},"fingerprint":{"type":"string","minLength":64,"maxLength":64}}}}}
    """;
    private const string ComponentTypeOutput = """
    {"type":"object","additionalProperties":false,"required":["outcome","componentType"],"properties":{"outcome":{"type":"string","minLength":1,"maxLength":80},"componentType":{"type":"object","additionalProperties":false,"required":["applicationId","qualifiedId","version","profileId","schemaHash"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"qualifiedId":{"type":"string","minLength":3,"maxLength":200},"version":{"type":"integer","minimum":1},"profileId":{"type":"string","minLength":1,"maxLength":120},"schemaHash":{"type":"string","minLength":64,"maxLength":64}}}}}
    """;
    private const string ActivationOutput = """
    {"type":"object","additionalProperties":false,"required":["outcome","activation"],"properties":{"outcome":{"type":"string","minLength":1,"maxLength":80},"activation":{"type":"object","additionalProperties":false,"required":["applicationId","activationRevision","applicationRevision","applicationFingerprint","previewFingerprint","resolutionFingerprint","activationFingerprint","dependencyGraphFingerprint","dependencyCoverageVersion","dependencyCoverageComplete","sourceCount","sourceIds","extensionIds","winnerCount"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"activationRevision":{"type":"integer","minimum":1},"applicationRevision":{"type":"integer","minimum":1},"applicationFingerprint":{"type":"string","minLength":64,"maxLength":64},"previewFingerprint":{"type":"string","minLength":64,"maxLength":64},"resolutionFingerprint":{"type":"string","minLength":64,"maxLength":64},"activationFingerprint":{"type":"string","minLength":64,"maxLength":64},"dependencyGraphFingerprint":{"type":"string","minLength":64,"maxLength":64},"dependencyCoverageVersion":{"type":"string","minLength":1,"maxLength":120},"dependencyCoverageComplete":{"type":"boolean"},"sourceCount":{"type":"integer","minimum":0},"sourceIds":{"type":"array","maxItems":100,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}},"extensionIds":{"type":"array","maxItems":100,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":63}},"winnerCount":{"type":"integer","minimum":0}}}}}
    """;
    private const string BindingDefinition = """
    {"type":"object","additionalProperties":false,"required":["stateSpaceId","applicationId","applicationRevision","applicationFingerprint","activeFingerprint","resolutionFingerprint","scope","bindingRevision","bindingFingerprint"],"properties":{"stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"applicationId":{"type":"string","minLength":1,"maxLength":63},"applicationRevision":{"type":"integer","minimum":1},"applicationFingerprint":{"type":"string","minLength":64,"maxLength":64},"activeFingerprint":{"type":"string","minLength":64,"maxLength":64},"resolutionFingerprint":{"type":"string","minLength":64,"maxLength":64},"scope":{"enum":["runtime-state-space","application-publication"]},"bindingRevision":{"type":"integer","minimum":1},"bindingFingerprint":{"type":"string","minLength":64,"maxLength":64}}}
    """;
    private static readonly string StateSpaceOutput = """
    {"$defs":{"binding":__BINDING_SCHEMA__},"type":"object","additionalProperties":false,"required":["outcome","binding"],"properties":{"outcome":{"type":"string","minLength":1,"maxLength":80},"binding":{"$ref":"#/$defs/binding"}}}
    """.Replace("__BINDING_SCHEMA__", BindingDefinition, StringComparison.Ordinal);
    private static readonly string StateSpaceUpgradeOutput = """
    {"$defs":{"binding":__BINDING_SCHEMA__},"type":"object","additionalProperties":false,"required":["outcome","previousBinding","binding","compatibility"],"properties":{"outcome":{"type":"string","minLength":1,"maxLength":80},"previousBinding":{"$ref":"#/$defs/binding"},"binding":{"$ref":"#/$defs/binding"},"compatibility":{"type":"object","additionalProperties":false,"required":["code","entityCount","componentCount","dependencyCoverageVersion","dependencyCoverageComplete"],"properties":{"code":{"type":"string","minLength":1,"maxLength":100},"entityCount":{"type":"integer","minimum":0},"componentCount":{"type":"integer","minimum":0},"dependencyCoverageVersion":{"type":"string","minLength":1,"maxLength":120},"dependencyCoverageComplete":{"type":"boolean"}}}}}
    """.Replace("__BINDING_SCHEMA__", BindingDefinition, StringComparison.Ordinal);
    private static readonly string LegacyOutput = """
    {"$defs":{"binding":__BINDING_SCHEMA__},"type":"object","additionalProperties":false,"required":["outcome","stateSpaceId","applicationId","inventory","binding"],"properties":{"outcome":{"type":"string","minLength":1,"maxLength":80},"stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"applicationId":{"type":"string","minLength":1,"maxLength":63},"inventory":{"type":"object","additionalProperties":false,"required":["entityCount","componentCount","containmentCount","relationshipCount","sourceFingerprint","evidenceFingerprint"],"properties":{"entityCount":{"type":"integer","minimum":0},"componentCount":{"type":"integer","minimum":0},"containmentCount":{"type":"integer","minimum":0},"relationshipCount":{"type":"integer","minimum":0},"sourceFingerprint":{"type":"string","minLength":64,"maxLength":64},"evidenceFingerprint":{"type":"string","minLength":64,"maxLength":64}}},"binding":{"$ref":"#/$defs/binding"}}}
    """.Replace("__BINDING_SCHEMA__", BindingDefinition, StringComparison.Ordinal);
}
