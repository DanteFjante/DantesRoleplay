using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Acornima;
using Acornima.Ast;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Interactions;
using DantesRoleplay.Notifications;
using DantesRoleplay.Mechanics;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace DantesRoleplay.Mechanics;

/// <summary>
/// Runs a mechanic's JavaScript in a Jint interpreter with no way out.
///
/// ARCHITECTURE.md §2 names arbitrary AI-written JavaScript as the major risk of this whole design.
/// Three things answer it, and all three are in this file:
///
/// 1. <b>No CLR access.</b> <c>AllowClr()</c> is never called. A Jint engine with CLR access is not
///    a sandbox — it is a second way to call anything this process can call, including the file
///    system and the database. There is no configuration flag anywhere that turns this on.
/// 2. <b>Nothing but strings crosses the boundary.</b> Data goes in as a JSON string and comes back
///    as a JSON string. Not one .NET object is ever handed to the script, so there is no object
///    graph for it to walk from, and helpers like the random source are implemented in JavaScript
///    rather than as delegates into C#. This costs a serialisation round trip and is worth it.
/// 3. <b>Every limit is set on the first run, not after something hangs.</b> Statements, wall clock,
///    memory, recursion — see <see cref="ExecutionLimits"/>.
///
/// A fresh engine per run, so nothing a mechanic leaves behind can be seen by the next one. The
/// fixed, trusted harness and bounded mechanic programs use Jint's immutable prepared form, which
/// is explicitly safe to share across engines and threads. No live engine, context or JavaScript
/// value is cached.
/// </summary>
public sealed class JintMechanicEngine : IMechanicEngine
{
    internal const string DisablePreparedProgramCacheSwitch =
        "DantesRoleplay.Mechanics.DisablePreparedProgramCache";

    private const int DefaultPreparedProgramCountLimit = 256;
    private const long DefaultPreparedProgramSourceBytesLimit = 16 * 1024 * 1024;
    private const long DefaultPreparedProgramComplexityLimit = 1_000_000;
    private const int DefaultMaximumConcurrentPreparations = 2;
    private const int DefaultMaximumPendingPreparations = 32;
    private static readonly TimeSpan StandalonePreparationTimeout = TimeSpan.FromSeconds(5);

    internal const int MaximumMechanicSourceBytes = 256 * 1024;
    internal const int MaximumMechanicTokens = 50_000;
    internal const int MaximumMechanicLexicalNesting = 64;
    internal const int MaximumMechanicExpressionComplexity = 128;
    internal const int MaximumMechanicAstNodes = 100_000;
    internal const int MaximumMechanicAstDepth = 128;

    private const string MechanicWrapperPrefix =
        "globalThis.__mechanic = (function (ctx) {\n\"use strict\";\n";

    private const string MechanicWrapperSuffix = "\n});";

    private const string ParserConfiguration =
        "script;strict=true;allow-return-outside-function=true;allow-new-target-outside-function=true;" +
        "regex-timeout=100ms;source-bytes=262144;tokens=50000;lexical-depth=64;expression-complexity=128;" +
        "ast-nodes=100000;ast-depth=128;fold-constants=false;compile-regex=false;" +
        "tolerant=false;flat-control-block-reset=true";

    private static readonly Meter RuntimeMeter = new("DantesRoleplay.Mechanics.JintMechanicEngine");
    private static readonly Histogram<double> PreparationDuration = RuntimeMeter.CreateHistogram<double>(
        "dantesroleplay.mechanic.preparation.duration", "ms");
    private static readonly Histogram<double> ContextConstructionDuration = RuntimeMeter.CreateHistogram<double>(
        "dantesroleplay.mechanic.context_construction.duration", "ms");
    private static readonly Histogram<double> ExecutionDuration = RuntimeMeter.CreateHistogram<double>(
        "dantesroleplay.mechanic.execution.duration", "ms");

    private static readonly JsonSerializerOptions Json = new()
    {
        // Going IN: camelCase, because the other side is JavaScript. Without this the projection
        // arrives as ctx.roles.subject.Name and every mechanic an LLM writes reads `.name` and
        // gets undefined — a silent wrong answer rather than an error, in every rule at once.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // But NOT the dictionary keys. Those are component definition ids and role names chosen by
        // the author, and quietly recasing someone's identifier is how "Stats" stops matching
        // "stats" for reasons nobody can find.
        DictionaryKeyPolicy = null,

        // Coming OUT: accept whatever case the mechanic used.
        PropertyNameCaseInsensitive = true
    };

    private static readonly Prepared<Script> PreparedHarness =
        Engine.PrepareScript(Harness, strict: true);

    private static readonly Prepared<Script> PreparedServiceBindingHarness =
        Engine.PrepareScript(ServiceBindingHarness, strict: true);

    private static readonly PreparedProgramCache StandalonePreparationCoordinator = new(
        retentionEnabled: false,
        countLimit: 1,
        sourceBytesLimit: 1,
        complexityLimit: 1,
        maximumConcurrentPreparations: DefaultMaximumConcurrentPreparations,
        maximumPendingPreparations: DefaultMaximumPendingPreparations,
        preparationStarted: null);

    private readonly PreparedProgramCache _preparedPrograms;
    private readonly Action<MechanicRunMeasurements>? _measurementObserver;

    public JintMechanicEngine()
        : this(!IsPreparedProgramCacheDisabled(), null)
    {
    }

    internal JintMechanicEngine(
        bool preparedProgramCacheEnabled,
        Action<MechanicRunMeasurements>? measurementObserver = null,
        int preparedProgramCountLimit = DefaultPreparedProgramCountLimit,
        long preparedProgramSourceBytesLimit = DefaultPreparedProgramSourceBytesLimit,
        long preparedProgramComplexityLimit = DefaultPreparedProgramComplexityLimit,
        int maximumConcurrentPreparations = DefaultMaximumConcurrentPreparations,
        int maximumPendingPreparations = DefaultMaximumPendingPreparations,
        Action<string>? preparationStarted = null)
    {
        if (preparedProgramCountLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(preparedProgramCountLimit));
        if (preparedProgramSourceBytesLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(preparedProgramSourceBytesLimit));
        if (preparedProgramComplexityLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(preparedProgramComplexityLimit));
        if (maximumConcurrentPreparations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentPreparations));
        if (maximumPendingPreparations < maximumConcurrentPreparations)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingPreparations));

        _preparedPrograms = new PreparedProgramCache(
            preparedProgramCacheEnabled,
            preparedProgramCountLimit,
            preparedProgramSourceBytesLimit,
            preparedProgramComplexityLimit,
            maximumConcurrentPreparations,
            maximumPendingPreparations,
            preparationStarted);
        _measurementObserver = measurementObserver;
    }

    internal PreparedProgramCacheStatistics PreparedProgramCacheStatistics =>
        _preparedPrograms.Statistics;

    public Task<MechanicRunResult> RunAsync(
        string source,
        MechanicProjection projection,
        ExecutionLimits limits,
        CancellationToken cancellationToken = default) =>
        RunCore(source, projection, limits, serviceCapabilities: null, cancellationToken);

    /// <summary>
    /// Runs the same prepared mechanic on the same isolated engine while exposing one invocation-
    /// bound read-only service capability. The capability is internal host wiring and never becomes
    /// part of the general mechanic contract.
    /// </summary>
    internal Task<MechanicRunResult> RunServiceAsync(
        string source,
        MechanicProjection projection,
        ExecutionLimits limits,
        IApplicationReadOnlyServiceCapabilities serviceCapabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceCapabilities);
        return RunCore(source, projection, limits, serviceCapabilities, cancellationToken);
    }

    private Task<MechanicRunResult> RunCore(
        string source,
        MechanicProjection projection,
        ExecutionLimits limits,
        IApplicationReadOnlyServiceCapabilities? serviceCapabilities,
        CancellationToken cancellationToken)
    {
        limits ??= ExecutionLimits.Default;

        if (string.IsNullOrWhiteSpace(source))
        {
            return Task.FromResult(MechanicRunResult.Failed("The mechanic has no source."));
        }

        var stopwatch = Stopwatch.StartNew();
        var preparationElapsed = TimeSpan.Zero;
        var contextConstructionElapsed = TimeSpan.Zero;
        var executionElapsed = TimeSpan.Zero;
        var preparationCacheHit = false;

        // Jint is synchronous and the limits are enforced on the running thread, so there is
        // nothing to await. Returning a Task keeps the interface honest about the possibility of
        // an out-of-process engine later without pretending this one is asynchronous now.
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            Prepared<Script> preparedMechanic;
            var phaseStart = Stopwatch.GetTimestamp();
            try
            {
                (preparedMechanic, preparationCacheHit) = GetPreparedMechanic(
                    source,
                    cancellationToken,
                    limits.Timeout - stopwatch.Elapsed);
            }
            finally
            {
                preparationElapsed = Stopwatch.GetElapsedTime(phaseStart);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfTimeoutElapsed(stopwatch.Elapsed, limits.Timeout);

            Engine engine;
            string payload;
            phaseStart = Stopwatch.GetTimestamp();
            try
            {
                payload = JsonSerializer.Serialize(
                    new
                    {
                        stateSpaceId = projection.StateSpaceId,
                        execution = projection.Execution,
                        audience = projection.Audience,
                        authorizedObserver = projection.AuthorizedObserver,
                        roles = projection.Roles,
                        objects = projection.Objects,
                        references = projection.References,
                        graphSnapshots = projection.GraphSnapshots,
                        input = projection.Input,
                        seed = projection.Seed,
                        children = projection.Children,
                        @event = projection.Event,
                        eventEntities = projection.EventEntities
                    },
                    Json);

                // The trusted harness must not turn a deeply nested serialized context into a way
                // around the sandbox recursion limit merely by switching to native JSON revivers.
                // Input and event are nested JSON strings inside the outer serialized payload.
                if (ExceedsJsonDepth(payload) || ExceedsJsonDepth(projection.Input) || ExceedsJsonDepth(projection.Event))
                {
                    return Task.FromResult(MechanicRunResult.Failed(
                        $"The mechanic context is nested more than {MaximumJsonDepth} levels."));
                }

                cancellationToken.ThrowIfCancellationRequested();
                var executionTimeout = limits.Timeout - stopwatch.Elapsed;
                if (executionTimeout <= TimeSpan.Zero) throw new TimeoutException();

                engine = new Engine(options =>
                {
                    // Deliberately NOT options.AllowClr(). See the class comment. If a future change
                    // needs a .NET type inside a mechanic, that is a design conversation, not a flag.
                    options.LimitMemory(limits.MemoryBytes);
                    options.TimeoutInterval(executionTimeout);
                    options.MaxStatements(limits.MaxStatements);
                    options.LimitRecursion(limits.MaxRecursionDepth);
                    options.CancellationToken(cancellationToken);

                    // Strict mode, so `total = 5` without a declaration is an error rather than a
                    // silent global. The author is an LLM and the error message is how it learns.
                    options.Strict();
                });

                engine.SetValue("__payload", payload);
                engine.SetValue("__maxLog", limits.MaxLogLines);
            }
            finally
            {
                contextConstructionElapsed = Stopwatch.GetElapsedTime(phaseStart);
            }

            string completion;
            phaseStart = Stopwatch.GetTimestamp();
            try
            {
                // The mechanic definition and trusted harness are separate scripts. The function's
                // lexical environment is the fresh realm's global environment, so it cannot close
                // over harness locals such as the log buffer or random state.
                engine.Execute(preparedMechanic);
                if (serviceCapabilities is not null)
                    BindServiceCapabilities(engine, serviceCapabilities, stopwatch, limits.Timeout,
                        cancellationToken);
                completion = engine.Evaluate(PreparedHarness).AsString();
            }
            finally
            {
                executionElapsed = Stopwatch.GetElapsedTime(phaseStart);
            }

            stopwatch.Stop();

            return Task.FromResult(Interpret(completion, projection.Seed, limits, (int)stopwatch.ElapsedMilliseconds));
        }
        catch (StatementsCountOverflowException)
        {
            return Task.FromResult(MechanicRunResult.Failed(
                $"The mechanic executed more than {limits.MaxStatements:N0} statements and was stopped. " +
                "This is almost always a loop that never ends.",
                "statements"));
        }
        catch (MemoryLimitExceededException)
        {
            return Task.FromResult(MechanicRunResult.Failed(
                $"The mechanic used more than {limits.MemoryBytes / (1024 * 1024)}MB and was stopped.",
                "memory"));
        }
        catch (RecursionDepthOverflowException)
        {
            return Task.FromResult(MechanicRunResult.Failed(
                $"The mechanic recursed deeper than {limits.MaxRecursionDepth} calls and was stopped.",
                "recursion"));
        }
        catch (ExecutionCanceledException)
        {
            return Task.FromResult(MechanicRunResult.Failed("The mechanic was cancelled.", "cancelled"));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(MechanicRunResult.Failed("The mechanic was cancelled.", "cancelled"));
        }
        catch (TimeoutException)
        {
            return Task.FromResult(MechanicRunResult.Failed(
                $"The mechanic ran for longer than {limits.Timeout.TotalSeconds} seconds and was stopped.",
                "timeout"));
        }
        catch (JavaScriptException ex)
        {
            // The mechanic threw. Expected, not exceptional — the author is writing code mid-session
            // and this message is how the rule gets fixed.
            return Task.FromResult(MechanicRunResult.Failed($"The mechanic threw: {ex.Message}"));
        }
        catch (Exception ex)
        {
            // Parse errors land here, as does anything Jint reports that is not one of the above.
            // Never rethrown: a broken mechanic must not be able to take down the caller.
            return Task.FromResult(MechanicRunResult.Failed($"The mechanic could not run: {ex.Message}"));
        }
        finally
        {
            try
            {
                RecordMeasurements(new MechanicRunMeasurements(
                    preparationElapsed,
                    contextConstructionElapsed,
                    executionElapsed,
                    preparationCacheHit));
            }
            catch
            {
                // Diagnostics are observational. A broken listener must never replace a mechanic's
                // deterministic result or weaken its failure handling.
            }
        }
    }

    private static void BindServiceCapabilities(
        Engine engine,
        IApplicationReadOnlyServiceCapabilities capabilities,
        Stopwatch invocation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var bridge = new ServiceCallbackBridge(capabilities, invocation, timeout, cancellationToken);
        var read = new ClrFunction(engine, "serviceRead", (_, arguments) =>
            (JsValue)bridge.Read(RequiredString(arguments, 0), RequiredString(arguments, 1)), 2);
        var action = new ClrFunction(engine, "serviceAction", (_, arguments) =>
            (JsValue)bridge.Action(RequiredString(arguments, 0), RequiredString(arguments, 1)), 2);
        var progressAttempt = new ClrFunction(engine, "serviceProgressAttempt", (_, arguments) =>
            (JsValue)bridge.ProgressAttempt(RequiredString(arguments, 0)), 1);
        var progress = new ClrFunction(engine, "serviceProgress", (_, arguments) =>
            (JsValue)bridge.Progress(RequiredString(arguments, 0)), 1);
        var unavailable = InteractionInvocationResult.Unavailable(
            "SERVICE_CAPABILITY_UNAVAILABLE",
            "This service capability is unavailable in the read-only runtime.").ToJson();

        var binder = engine.Evaluate(PreparedServiceBindingHarness);
        engine.Invoke(binder, read, action, progressAttempt, progress,
            capabilities is IApplicationActionServiceCapabilities, (JsValue)unavailable);
    }

    private static string RequiredString(JsValue[] arguments, int index)
    {
        if (index >= arguments.Length || !arguments[index].IsString())
            throw new InvalidOperationException("Service callbacks accept only string arguments.");
        return arguments[index].AsString();
    }

    private sealed class ServiceCallbackBridge(
        IApplicationReadOnlyServiceCapabilities capabilities,
        Stopwatch invocation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        public string Read(string alias, string inputJson)
        {
            using var wait = RemainingWait();
            var task = capabilities.ReadAsync(alias, inputJson, wait.Token);
            var result = task.WaitAsync(wait.Token).ConfigureAwait(false).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfTimeoutElapsed(invocation.Elapsed, timeout);
            return result.ToJson();
        }

        public string Action(string alias, string inputJson)
        {
            if (capabilities is not IApplicationActionServiceCapabilities actions)
                throw new InvalidOperationException("The action service capability is unavailable.");
            using var wait = RemainingWait();
            // The state-changing adapter owns cancellation reconciliation. Waiting for that result
            // prevents an unknown commit from continuing after the engine has returned.
            var result = actions.ActionAsync(alias, inputJson, wait.Token)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfTimeoutElapsed(invocation.Elapsed, timeout);
            return result.ToJson();
        }

        public string Progress(string dataJson)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfTimeoutElapsed(invocation.Elapsed, timeout);
            var disposition = capabilities is IApplicationReadOnlyServiceProgressAttemptSink sink
                ? sink.WriteProgress(dataJson)
                : capabilities.TryWriteProgress(dataJson);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfTimeoutElapsed(invocation.Elapsed, timeout);
            return disposition switch
            {
                ApplicationServiceProgressDisposition.Accepted => "accepted",
                ApplicationServiceProgressDisposition.Backpressured => "backpressured",
                ApplicationServiceProgressDisposition.Closed => "closed",
                _ => throw new InvalidOperationException("The progress disposition is unsupported.")
            };
        }

        public string ProgressAttempt(string attempt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfTimeoutElapsed(invocation.Elapsed, timeout);
            if (!int.TryParse(attempt, out var parsed) || parsed is < 1 or > 33)
                throw new InvalidOperationException("The progress attempt is invalid.");
            if (capabilities is IApplicationReadOnlyServiceProgressAttemptSink sink)
                sink.BeginProgressAttempt();
            return "ok";
        }

        private CancellationTokenSource RemainingWait()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = timeout - invocation.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException();
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (remaining <= TimeSpan.FromMilliseconds(int.MaxValue)) source.CancelAfter(remaining);
            return source;
        }
    }

    private static bool IsPreparedProgramCacheDisabled() =>
        AppContext.TryGetSwitch(DisablePreparedProgramCacheSwitch, out var disabled) && disabled;

    private static void ThrowIfTimeoutElapsed(TimeSpan elapsed, TimeSpan timeout)
    {
        if (elapsed >= timeout) throw new TimeoutException();
    }

    private (Prepared<Script> Program, bool CacheHit) GetPreparedMechanic(
        string source,
        CancellationToken cancellationToken,
        TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) throw new TimeoutException();
        ValidateMechanicSourceSize(source);
        var key = new PreparedProgramKey(
            source,
            MechanicWrapperPrefix,
            MechanicWrapperSuffix,
            PreparationConfigurationKey);

        var (prepared, cacheHit) = _preparedPrograms.GetOrPrepare(
            key,
            new PreparationBudget(cancellationToken, remaining),
            static (cacheKey, budget) => PrepareMechanicProgramCore(cacheKey.Source, budget));
        return (prepared.Program, cacheHit);
    }

    /// <summary>
    /// Validates a mechanic body before embedding it, then prepares the exact executable wrapper
    /// used at runtime. Activation validation can call this same internal boundary and discard the
    /// result; it must not duplicate a looser parse.
    /// </summary>
    internal static Prepared<Script> PrepareMechanicProgram(string source)
    {
        ValidateMechanicSourceSize(source);
        var key = new PreparedProgramKey(
            source,
            MechanicWrapperPrefix,
            MechanicWrapperSuffix,
            PreparationConfigurationKey);
        return StandalonePreparationCoordinator.GetOrPrepare(
            key,
            new PreparationBudget(CancellationToken.None, StandalonePreparationTimeout),
            static (cacheKey, budget) => PrepareMechanicProgramCore(cacheKey.Source, budget)).Program.Program;
    }

    internal static MechanicProgramComplexity InspectMechanicProgramComplexity(string source)
        => ParseAndMeasure(
            source,
            new PreparationBudget(CancellationToken.None, StandalonePreparationTimeout));

    private static PreparedMechanicProgram PrepareMechanicProgramCore(
        string source,
        PreparationBudget budget)
    {
        ArgumentNullException.ThrowIfNull(source);
        budget.ThrowIfExpired();
        var complexity = ParseAndMeasure(source, budget);
        budget.ThrowIfExpired();

        var program = Engine.PrepareScript(
            MechanicWrapperPrefix + source + MechanicWrapperSuffix,
            "prepared-mechanic.js",
            strict: true,
            new ScriptPreparationOptions
            {
                // Constant folding runs before a constrained engine exists. A tiny literal
                // BigInt shift can otherwise allocate and retain an enormous cached value here.
                FoldConstants = false,
                ParsingOptions = new ScriptParsingOptions
                {
                    AllowReturnOutsideFunction = false,
                    CompileRegex = false,
                    RegexTimeout = TimeSpan.FromMilliseconds(100),
                    Tolerant = false
                }
            });
        budget.ThrowIfExpired();
        return new PreparedMechanicProgram(program, complexity);
    }

    private static MechanicProgramComplexity ParseAndMeasure(string source, PreparationBudget budget)
    {
        ArgumentNullException.ThrowIfNull(source);
        var sourceBytes = ValidateMechanicSourceSize(source);

        var lexical = PreflightTokens(source, budget);
        var nodeCount = 0;
#pragma warning disable CS0618 // Acornima 1.6 still uses this to bound RegExp validation work.
        var parser = new Parser(new ParserOptions
        {
            AllowReturnOutsideFunction = true,
            AllowNewTargetOutsideFunction = true,
            RegexTimeout = TimeSpan.FromMilliseconds(100),
            OnNode = (Node node, in OnNodeContext _) =>
            {
                if (++nodeCount > MaximumMechanicAstNodes)
                    throw new InvalidOperationException(
                        $"The mechanic syntax tree exceeds {MaximumMechanicAstNodes:N0} nodes.");
                budget.ThrowIfExpired();
            }
        });
#pragma warning restore CS0618
        var script = parser.ParseScript(source, "mechanic-source.js", strict: true);
        var astDepth = MeasureAstDepth(script, budget);
        return new MechanicProgramComplexity(
            sourceBytes,
            lexical.TokenCount,
            lexical.MaximumNesting,
            lexical.MaximumExpressionComplexity,
            nodeCount,
            astDepth);
    }

    private static int ValidateMechanicSourceSize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > MaximumMechanicSourceBytes)
            throw new InvalidOperationException(
                $"The mechanic source exceeds {MaximumMechanicSourceBytes:N0} characters.");
        var sourceBytes = Encoding.UTF8.GetByteCount(source);
        if (sourceBytes > MaximumMechanicSourceBytes)
            throw new InvalidOperationException(
                $"The mechanic source exceeds {MaximumMechanicSourceBytes:N0} UTF-8 bytes.");
        return sourceBytes;
    }

    private static MechanicLexicalComplexity PreflightTokens(string source, PreparationBudget budget)
    {
#pragma warning disable CS0618 // Acornima 1.6 still uses this to bound RegExp validation work.
        var tokenizer = new Tokenizer(
            source,
            SourceType.Script,
            "mechanic-source.js",
            new TokenizerOptions { RegexTimeout = TimeSpan.FromMilliseconds(100) });
#pragma warning restore CS0618
        var context = new TokenizerContext(
            strict: true,
            ignoreEscapeSequenceInKeyword: false,
            requireValidEscapeSequenceInTemplate: false);
        var count = 0;
        var nesting = 0;
        var maximumNesting = 0;
        var expressionComplexity = 0;
        var maximumExpressionComplexity = 0;
        var parentExpressionComplexity = new Stack<int>();
        int? completedBlockBaseline = null;

        while (true)
        {
            budget.ThrowIfExpired();
            var token = tokenizer.GetToken(in context);
            if (token.Kind == TokenKind.EOF)
                return new MechanicLexicalComplexity(count, maximumNesting, maximumExpressionComplexity);
            if (++count > MaximumMechanicTokens)
                throw new InvalidOperationException(
                    $"The mechanic source exceeds {MaximumMechanicTokens:N0} lexical tokens.");
            var tokenText = token.Value as string ?? token.KindText;
            var punctuator = token.Kind == TokenKind.Punctuator;
            var keyword = token.Kind == TokenKind.Keyword;
            var contextualUnary = token.Kind == TokenKind.Identifier && tokenText is "await" or "yield";

            // A new control statement after a completed block is a sibling, not another parser
            // recursion level. Preserve the count through `else` so deep else-if chains remain
            // bounded before parsing, and retain every enclosing lexical scope's baseline.
            if (completedBlockBaseline is { } baseline && keyword
                && tokenText is "if" or "for" or "while" or "do")
                expressionComplexity = baseline;
            completedBlockBaseline = null;

            switch (punctuator ? tokenText : null)
            {
                case "(":
                case "[":
                case "{":
                case "${":
                    parentExpressionComplexity.Push(expressionComplexity);
                    if (++nesting > MaximumMechanicLexicalNesting)
                        throw new InvalidOperationException(
                            $"The mechanic source nests more than {MaximumMechanicLexicalNesting:N0} lexical levels.");
                    maximumNesting = Math.Max(maximumNesting, nesting);
                    break;
                case ")":
                case "]":
                case "}":
                    nesting = Math.Max(0, nesting - 1);
                    if (parentExpressionComplexity.TryPop(out var parent))
                        expressionComplexity = Math.Max(parent, expressionComplexity);
                    if (tokenText == "}")
                        completedBlockBaseline = parentExpressionComplexity.TryPeek(out var enclosing) ? enclosing : 0;
                    break;
            }

            // These tokens build recursive expression/statement shapes in ordinary recursive
            // descent parsers even without brackets (for example !!!!!!!!!x or a=b=c). Bound the
            // chain before asking either Acornima parse to construct an AST.
            var recursivePunctuator = punctuator && tokenText is
                "!" or "~" or "+" or "-" or "=" or "+=" or "-=" or "*=" or "/="
                or "%=" or "**=" or "&=" or "|=" or "^=" or "<<=" or ">>=" or ">>>="
                or "&&=" or "||=" or "??=" or "=>" or "?" or ":" or "**";
            var recursiveKeyword = keyword && tokenText is
                "new" or "delete" or "typeof" or "void" or "if" or "else" or "do" or "for" or "while" or "with";
            if (recursivePunctuator || recursiveKeyword || contextualUnary)
            {
                if (++expressionComplexity > MaximumMechanicExpressionComplexity)
                    throw new InvalidOperationException(
                        $"The mechanic expression exceeds {MaximumMechanicExpressionComplexity:N0} recursive operators.");
                maximumExpressionComplexity = Math.Max(maximumExpressionComplexity, expressionComplexity);
            }
            else if (punctuator && tokenText is ";" or ",")
            {
                expressionComplexity = parentExpressionComplexity.TryPeek(out var parent)
                    ? parent
                    : 0;
            }
        }
    }

    private static int MeasureAstDepth(Script script, PreparationBudget budget)
    {
        var maximum = 0;
        var pending = new Stack<(Node Node, int Depth)>();
        pending.Push((script, 1));
        while (pending.TryPop(out var current))
        {
            budget.ThrowIfExpired();
            if (current.Depth > MaximumMechanicAstDepth)
                throw new InvalidOperationException(
                    $"The mechanic syntax tree exceeds {MaximumMechanicAstDepth:N0} levels.");
            maximum = Math.Max(maximum, current.Depth);
            foreach (var child in current.Node.ChildNodes)
                pending.Push((child, current.Depth + 1));
        }

        return maximum;
    }

    private void RecordMeasurements(MechanicRunMeasurements measurements)
    {
        var cacheTag = new KeyValuePair<string, object?>(
            "mechanic.preparation.cache_hit", measurements.PreparationCacheHit);
        PreparationDuration.Record(measurements.Preparation.TotalMilliseconds, cacheTag);
        ContextConstructionDuration.Record(measurements.ContextConstruction.TotalMilliseconds);
        ExecutionDuration.Record(measurements.Execution.TotalMilliseconds);
        _measurementObserver?.Invoke(measurements);
    }

    private static string PreparationConfigurationKey { get; } = string.Join(
        '|',
        ParserConfiguration,
        typeof(Parser).Assembly.GetName().FullName,
        typeof(Engine).Assembly.GetName().FullName,
        Environment.Version.ToString());

    /// <summary>Identity of the actual pure wrapper, harness and preparation configuration.</summary>
    internal static string PureExecutionPolicyFingerprint => InteractionCanonicalJson.Fingerprint(
        "dantes-roleplay/jint-pure-execution-policy/v1",
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            preparation = PreparationConfigurationKey,
            wrapperPrefix = MechanicWrapperPrefix,
            wrapperSuffix = MechanicWrapperSuffix,
            harness = Harness
        })));

    private const int MaximumJsonDepth = 64;

    private static bool ExceedsJsonDepth(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json),
                new JsonReaderOptions { MaxDepth = 1_024 });
            while (reader.Read())
                if (reader.CurrentDepth > MaximumJsonDepth) return true;
            return false;
        }
        catch (JsonException)
        {
            // Preserve the harness's established malformed-input result rather than conflating
            // syntax errors with the explicit depth fence.
            return false;
        }
    }

    private static MechanicRunResult Interpret(
        string completion,
        long seed,
        ExecutionLimits limits,
        int elapsed)
    {
        var raw = JsonSerializer.Deserialize<HarnessResult>(completion, Json);

        if (raw is null)
        {
            return MechanicRunResult.Failed("The mechanic returned something that could not be read.");
        }

        var log = raw.Log ?? [];

        if (!string.IsNullOrEmpty(raw.Error))
        {
            return MechanicRunResult.Failed(raw.Error, log: log);
        }

        if (raw.Output is null)
        {
            return MechanicRunResult.Failed(
                "The mechanic returned nothing. It must return an object, e.g. " +
                "{ narration: \"...\", effects: [] }.",
                log: log);
        }

        var effects = raw.Output.Effects ?? [];

        if (effects.Count > limits.MaxEffects)
        {
            return MechanicRunResult.Failed(
                $"The mechanic proposed {effects.Count} effects; the limit is {limits.MaxEffects}. " +
                "A rule returning this many is usually looping.",
                "effects",
                log);
        }

        var events = raw.Output.Events ?? [];

        if (events.Count > limits.MaxEvents)
        {
            return MechanicRunResult.Failed(
                $"The mechanic declared {events.Count} events; the limit is {limits.MaxEvents}. " +
                "An event is an announcement, not a change — a rule making this many has probably " +
                "confused the two.",
                "events",
                log);
        }

        var notifications = raw.Output.Notifications ?? [];

        if (notifications.Count > limits.MaxNotifications)
        {
            return MechanicRunResult.Failed(
                $"The mechanic raised {notifications.Count} notifications; the limit is "
                + $"{limits.MaxNotifications}. Somebody has to read these.",
                "notifications",
                log);
        }

        return new MechanicRunResult
        {
            Ok = true,
            Output = new MechanicOutput
            {
                Effects = effects,
                Events = events,
                Notifications = notifications,
                Narration = raw.Output.Narration ?? string.Empty,
                Data = raw.Output.Data ?? "{}",
                HasData = raw.Output.Data is not null
                ,Decision = raw.Output.Decision ?? string.Empty
                ,Code = raw.Output.Code ?? string.Empty
                ,Reason = raw.Output.Reason ?? string.Empty
            },
            Log = log,
            Seed = seed,
            ElapsedMilliseconds = elapsed
        };
    }

    private sealed class HarnessResult
    {
        public HarnessOutput? Output { get; set; }

        public List<string>? Log { get; set; }

        public string? Error { get; set; }
    }

    private sealed class HarnessOutput
    {
        public List<Effect>? Effects { get; set; }

        public List<DeclaredEvent>? Events { get; set; }

        public List<DeclaredNotification>? Notifications { get; set; }

        public string? Narration { get; set; }

        public string? Data { get; set; }
        public string? Decision { get; set; }
        public string? Code { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>
    /// The JavaScript that wraps every mechanic.
    ///
    /// <c>ctx.random</c> is seeded and implemented here rather than delegating to .NET, for two
    /// reasons. It keeps the string-only boundary intact, and it makes the sequence reproducible
    /// from the seed alone — a rule that decides outcomes by chance is unreviewable unless the
    /// chance can be replayed, and replay is the whole reason the seed is recorded.
    ///
    /// mulberry32: small, fast, well-distributed enough for a game, and short enough to read.
    /// A game's own conventions for rolling anything are written on top of this, in JavaScript,
    /// where the game belongs (§3.11).
    /// </summary>
    /// <summary>
    /// Creates the authored service surface from native functions passed as values. The native
    /// functions are captured only in this trusted closure and are never installed on the global
    /// object. JSON and other intrinsics are captured before authored code can replace them.
    /// </summary>
    private const string ServiceBindingHarness = """
        (function (readNative, actionNative, progressAttemptNative, progressNative, actionEnabled, unavailableJson) {
          var safeParse = JSON.parse;
          var safeStringify = JSON.stringify;
          var safeString = String;
          var safeKeys = Object.keys;
          var safeFreeze = Object.freeze;
          var safeIsFrozen = Object.isFrozen;
          var safeIsArray = Array.isArray;
          var progressAttempts = 0;

          function freezeDeep(value) {
            if (value && typeof value === 'object' && !safeIsFrozen(value)) {
              var keys = safeKeys(value);
              for (var i = 0; i < keys.length; i++) freezeDeep(value[keys[i]]);
              safeFreeze(value);
            }
            return value;
          }

          function inputJson(value, name) {
            if (value === null || typeof value !== 'object' || safeIsArray(value)) {
              throw new TypeError(name + ' input must be an object.');
            }
            return safeStringify(value);
          }

          var unavailable = freezeDeep(safeParse(unavailableJson));
          function unsupported() { return unavailable; }
          var action = actionEnabled ? function (alias, input) {
            if (typeof alias !== 'string') throw new TypeError('services.action alias must be a string.');
            return freezeDeep(safeParse(actionNative(safeString(alias), inputJson(input, 'services.action'))));
          } : unsupported;
          var services = {
            read: function (alias, input) {
              if (typeof alias !== 'string') throw new TypeError('services.read alias must be a string.');
              return freezeDeep(safeParse(readNative(safeString(alias), inputJson(input, 'services.read'))));
            },
            progress: function (data) {
              progressAttempts++;
              progressAttemptNative(safeString(progressAttempts));
              if (progressAttempts > 32) throw new RangeError('services.progress attempt limit exceeded.');
              return progressNative(inputJson(data, 'services.progress'));
            },
            action: action,
            workflow: unsupported,
            wait: unsupported,
            job: unsupported,
            ai: unsupported
          };
          safeFreeze(services);
          Object.defineProperty(globalThis, '__boundServices', {
            value: services, writable: false, configurable: true, enumerable: false
          });
        })
        """;

    private const string Harness = """
        (function () {
          var log = [];
          var safeJsonParse = JSON.parse;
          var safeJsonStringify = JSON.stringify;
          var services = typeof globalThis.__boundServices === 'undefined'
            ? null : globalThis.__boundServices;
          delete globalThis.__boundServices;

          function makeRandom(seed) {
            var a = (seed >>> 0) || 1;
            return function () {
              a |= 0; a = (a + 0x6D2B79F5) | 0;
              var t = Math.imul(a ^ (a >>> 15), 1 | a);
              t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
              return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
            };
          }

          try {
            var payload = safeJsonParse(__payload);
            var random = makeRandom(payload.seed);

            function freezeDeep(value) {
              if (value && typeof value === 'object' && !Object.isFrozen(value)) {
                for (var key in Object.freeze(value)) {
                  if (value[key] && typeof value[key] === 'object') freezeDeep(value[key]);
                }
              }
              return value;
            }

            var ctx = {
              stateSpaceId: payload.stateSpaceId || '',
              execution: payload.execution === null || payload.execution === undefined
                ? null : freezeDeep(payload.execution),
              audience: payload.audience === null || payload.audience === undefined
                ? null : freezeDeep(payload.audience),
              roles: freezeDeep(payload.roles || {}),
              objects: freezeDeep(payload.objects || {}),
              references: freezeDeep(payload.references || {}),
              graphSnapshots: freezeDeep(payload.graphSnapshots || {}),
              input: freezeDeep(safeJsonParse(payload.input || '{}')),
              seed: payload.seed,
              children: freezeDeep(payload.children || {}),
              event: freezeDeep(safeJsonParse(payload.event || '{}')),
              eventEntities: freezeDeep(payload.eventEntities || {}),

              random: random,

              // Inclusive both ends, because every table-top convention is inclusive and an
              // off-by-one here would be invisible in play and wrong in every rule at once.
              randomInt: function (min, max) {
                min = Math.ceil(min); max = Math.floor(max);
                if (max < min) { throw new Error('randomInt: max (' + max + ') is below min (' + min + ')'); }
                return min + Math.floor(random() * (max - min + 1));
              },

              log: function (message) {
                if (log.length < __maxLog) { log.push(String(message)); }
              },

              // Everything a mechanic may read is already in its declared roles and objects.
              // There is no fetch, query or store here: that absence makes a mechanic a pure
              // function of its frozen inputs, and therefore reviewable.
              effects: []
            };

            if (services !== null) {
              Object.defineProperty(ctx, 'services', {
                value: services, writable: false, configurable: false, enumerable: true
              });
            }

            if (payload.authorizedObserver !== null && payload.authorizedObserver !== undefined) {
              Object.defineProperty(ctx, 'authorizedObserver', {
                value: freezeDeep(payload.authorizedObserver), writable: false, configurable: false, enumerable: true
              });
            }
            var output = __mechanic(ctx);

            // Returning the effects is the documented way; pushing to ctx.effects is the other
            // way an author reaches for. Accept both rather than failing on a reasonable guess.
            if (output === undefined || output === null) {
              output = ctx.effects.length > 0 ? { effects: ctx.effects } : null;
            } else if (!output.effects && ctx.effects.length > 0) {
              output.effects = ctx.effects;
            }

            if (output && output.data !== undefined && typeof output.data !== 'string') {
              output.data = safeJsonStringify(output.data);
            }

            // Only strings cross this boundary, so a declared event's payload is stringified here
            // rather than making every rule author remember to do it. An author who already passed
            // a string is left alone — double-encoding it would produce a payload that validates
            // against no schema at all.
            if (output && output.events && output.events.length) {
              for (var i = 0; i < output.events.length; i++) {
                var declared = output.events[i];
                if (declared && declared.payload !== undefined && typeof declared.payload !== 'string') {
                  declared.payload = safeJsonStringify(declared.payload);
                }
              }
            }

            return safeJsonStringify({ output: output, log: log });
          } catch (e) {
            return safeJsonStringify({
              error: (e && e.message) ? String(e.message) : String(e),
              log: log
            });
          }
        })();
        """;
}

internal interface IApplicationReadOnlyServiceProgressAttemptSink
{
    void BeginProgressAttempt();
    ApplicationServiceProgressDisposition WriteProgress(string dataJson);
}

internal interface IApplicationActionServiceCapabilities : IApplicationReadOnlyServiceCapabilities
{
    Task<InteractionInvocationResult> ActionAsync(
        string alias,
        string inputJson,
        CancellationToken cancellationToken = default);
}

internal readonly record struct MechanicRunMeasurements(
    TimeSpan Preparation,
    TimeSpan ContextConstruction,
    TimeSpan Execution,
    bool PreparationCacheHit);

internal readonly record struct PreparedProgramCacheStatistics(
    int EntryCount,
    long RetainedSourceBytes,
    long RetainedComplexity,
    long PreparationCount,
    long PreparationFailureCount,
    long CacheHitCount,
    long EvictionCount,
    int PendingPreparations,
    int ActivePreparations,
    int PeakPendingPreparations,
    int PeakActivePreparations);

internal readonly record struct MechanicProgramComplexity(
    int SourceBytes,
    int TokenCount,
    int LexicalNesting,
    int ExpressionComplexity,
    int NodeCount,
    int AstDepth)
{
    public long RetainedWork => checked((long)TokenCount + NodeCount);
}

internal readonly record struct MechanicLexicalComplexity(
    int TokenCount,
    int MaximumNesting,
    int MaximumExpressionComplexity);

internal readonly record struct PreparedMechanicProgram(
    Prepared<Script> Program,
    MechanicProgramComplexity Complexity);

internal readonly record struct PreparedProgramKey(
    string Source,
    string WrapperPrefix,
    string WrapperSuffix,
    string ParserRuntimeConfiguration);

internal readonly struct PreparationBudget
{
    private readonly long _deadline;

    public PreparationBudget(CancellationToken cancellationToken, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new TimeoutException();
        CancellationToken = cancellationToken;
        var availableTicks = timeout.TotalSeconds * Stopwatch.Frequency;
        var now = Stopwatch.GetTimestamp();
        _deadline = availableTicks >= long.MaxValue - now
            ? long.MaxValue
            : now + (long)availableTicks;
    }

    public CancellationToken CancellationToken { get; }

    public TimeSpan Remaining
    {
        get
        {
            var ticks = _deadline - Stopwatch.GetTimestamp();
            return ticks <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(ticks / (double)Stopwatch.Frequency);
        }
    }

    public void ThrowIfExpired()
    {
        CancellationToken.ThrowIfCancellationRequested();
        if (Stopwatch.GetTimestamp() >= _deadline) throw new TimeoutException();
    }
}

/// <summary>
/// Coordinates bounded preparation and retains a small LRU of immutable, thread-safe Jint
/// programs. Cache locks protect only indexes/counters; tokenization, parsing, preparation and test
/// hooks always run outside them. Source text is part of the key so a hash collision cannot select
/// a different executable.
/// </summary>
internal sealed class PreparedProgramCache
{
    private readonly object _gate = new();
    private readonly Dictionary<PreparedProgramKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _leastRecentlyUsed = [];
    private readonly Dictionary<PreparedProgramKey, PreparationFlight> _flights = [];
    private readonly SemaphoreSlim _activeSlots;
    private readonly bool _retentionEnabled;
    private readonly int _countLimit;
    private readonly long _sourceBytesLimit;
    private readonly long _complexityLimit;
    private readonly int _pendingLimit;
    private readonly Action<string>? _preparationStarted;
    private long _retainedSourceBytes;
    private long _retainedComplexity;
    private long _preparationCount;
    private long _preparationFailureCount;
    private long _cacheHitCount;
    private long _evictionCount;
    private int _pendingPreparations;
    private int _activePreparations;
    private int _peakPendingPreparations;
    private int _peakActivePreparations;

    public PreparedProgramCache(
        bool retentionEnabled,
        int countLimit,
        long sourceBytesLimit,
        long complexityLimit,
        int maximumConcurrentPreparations,
        int maximumPendingPreparations,
        Action<string>? preparationStarted)
    {
        _retentionEnabled = retentionEnabled;
        _countLimit = countLimit;
        _sourceBytesLimit = sourceBytesLimit;
        _complexityLimit = complexityLimit;
        _pendingLimit = maximumPendingPreparations;
        _preparationStarted = preparationStarted;
        _activeSlots = new SemaphoreSlim(maximumConcurrentPreparations, maximumConcurrentPreparations);
    }

    public PreparedProgramCacheStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return new PreparedProgramCacheStatistics(
                    _entries.Count,
                    _retainedSourceBytes,
                    _retainedComplexity,
                    _preparationCount,
                    _preparationFailureCount,
                    _cacheHitCount,
                    _evictionCount,
                    _pendingPreparations,
                    _activePreparations,
                    _peakPendingPreparations,
                    _peakActivePreparations);
            }
        }
    }

    public (PreparedMechanicProgram Program, bool CacheHit) GetOrPrepare(
        PreparedProgramKey key,
        PreparationBudget budget,
        Func<PreparedProgramKey, PreparationBudget, PreparedMechanicProgram> prepare)
    {
        while (true)
        {
            try { return GetOrPrepareAttempt(key, budget, prepare); }
            catch (PreparationLeaderAbortedException)
            {
                // A leader's cancellation/deadline is personal. Healthy followers rejoin or
                // become the next leader under their original budgets and bounded admission.
                budget.ThrowIfExpired();
            }
        }
    }

    private (PreparedMechanicProgram Program, bool CacheHit) GetOrPrepareAttempt(
        PreparedProgramKey key,
        PreparationBudget budget,
        Func<PreparedProgramKey, PreparationBudget, PreparedMechanicProgram> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        budget.ThrowIfExpired();

        PreparationFlight flight;
        bool leader;
        lock (_gate)
        {
            if (_retentionEnabled && _entries.TryGetValue(key, out var existing))
            {
                _leastRecentlyUsed.Remove(existing);
                _leastRecentlyUsed.AddFirst(existing);
                _cacheHitCount++;
                return (existing.Value.Program, true);
            }

            if (_pendingPreparations >= _pendingLimit)
                throw new InvalidOperationException("The mechanic preparation queue is at capacity.");
            _pendingPreparations++;
            _peakPendingPreparations = Math.Max(_peakPendingPreparations, _pendingPreparations);

            if (_retentionEnabled && _flights.TryGetValue(key, out var existingFlight))
            {
                flight = existingFlight;
                leader = false;
            }
            else
            {
                flight = new PreparationFlight();
                leader = true;
                if (_retentionEnabled) _flights.Add(key, flight);
            }
        }

        try
        {
            if (!leader)
            {
                var shared = flight.Wait(budget);
                lock (_gate) _cacheHitCount++;
                return (shared, true);
            }

            try
            {
                EnterActiveSlot(budget);
                PreparedMechanicProgram program;
                try
                {
                    budget.ThrowIfExpired();
                    _preparationStarted?.Invoke(key.Source);
                    budget.ThrowIfExpired();
                    program = prepare(key, budget);
                }
                finally
                {
                    ExitActiveSlot();
                }

                lock (_gate)
                {
                    _preparationCount++;
                    if (_retentionEnabled)
                    {
                        _flights.Remove(key);
                        Retain(key, program);
                    }
                }
                flight.Complete(program);
                return (program, false);
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _preparationFailureCount++;
                    if (_retentionEnabled) _flights.Remove(key);
                }
                flight.Fail(exception);
                throw;
            }
        }
        finally
        {
            lock (_gate) _pendingPreparations--;
        }
    }

    private void EnterActiveSlot(PreparationBudget budget)
    {
        budget.ThrowIfExpired();
        if (!_activeSlots.Wait(budget.Remaining, budget.CancellationToken)) throw new TimeoutException();
        lock (_gate)
        {
            _activePreparations++;
            _peakActivePreparations = Math.Max(_peakActivePreparations, _activePreparations);
        }
    }

    private void ExitActiveSlot()
    {
        lock (_gate) _activePreparations--;
        _activeSlots.Release();
    }

    private void Retain(PreparedProgramKey key, PreparedMechanicProgram program)
    {
        var node = _leastRecentlyUsed.AddFirst(new Entry(key, program));
        _entries.Add(key, node);
        _retainedSourceBytes += program.Complexity.SourceBytes;
        _retainedComplexity += program.Complexity.RetainedWork;

        while (_entries.Count > _countLimit
               || _retainedSourceBytes > _sourceBytesLimit
               || _retainedComplexity > _complexityLimit)
        {
            var victim = _leastRecentlyUsed.Last!;
            _leastRecentlyUsed.RemoveLast();
            _entries.Remove(victim.Value.Key);
            _retainedSourceBytes -= victim.Value.Program.Complexity.SourceBytes;
            _retainedComplexity -= victim.Value.Program.Complexity.RetainedWork;
            _evictionCount++;
        }
    }

    private sealed record Entry(PreparedProgramKey Key, PreparedMechanicProgram Program);

    private sealed class PreparationFlight
    {
        private readonly ManualResetEventSlim _completed = new(false);
        private PreparedMechanicProgram _program;
        private ExceptionDispatchInfo? _failure;

        public void Complete(PreparedMechanicProgram program)
        {
            _program = program;
            _completed.Set();
        }

        public void Fail(Exception exception)
        {
            _failure = ExceptionDispatchInfo.Capture(exception);
            _completed.Set();
        }

        public PreparedMechanicProgram Wait(PreparationBudget budget)
        {
            budget.ThrowIfExpired();
            if (!_completed.Wait(budget.Remaining, budget.CancellationToken)) throw new TimeoutException();
            budget.ThrowIfExpired();
            if (_failure?.SourceException is OperationCanceledException or TimeoutException)
                throw new PreparationLeaderAbortedException();
            _failure?.Throw();
            return _program;
        }
    }

    private sealed class PreparationLeaderAbortedException : Exception;
}
