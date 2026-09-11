using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Acornima;
using Acornima.Ast;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Notifications;
using DantesRoleplay.Mechanics;
using Jint;
using Jint.Runtime;

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

    private const string MechanicWrapperPrefix =
        "globalThis.__mechanic = (function (ctx) {\n\"use strict\";\n";

    private const string MechanicWrapperSuffix = "\n});";

    private const string ParserConfiguration =
        "script;strict=true;allow-return-outside-function=true;allow-new-target-outside-function=true";

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

    private readonly PreparedProgramCache? _preparedPrograms;
    private readonly Action<MechanicRunMeasurements>? _measurementObserver;

    public JintMechanicEngine()
        : this(!IsPreparedProgramCacheDisabled(), null)
    {
    }

    internal JintMechanicEngine(
        bool preparedProgramCacheEnabled,
        Action<MechanicRunMeasurements>? measurementObserver = null,
        int preparedProgramCountLimit = DefaultPreparedProgramCountLimit,
        long preparedProgramSourceBytesLimit = DefaultPreparedProgramSourceBytesLimit)
    {
        if (preparedProgramCountLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(preparedProgramCountLimit));
        if (preparedProgramSourceBytesLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(preparedProgramSourceBytesLimit));

        _preparedPrograms = preparedProgramCacheEnabled
            ? new PreparedProgramCache(preparedProgramCountLimit, preparedProgramSourceBytesLimit)
            : null;
        _measurementObserver = measurementObserver;
    }

    internal PreparedProgramCacheStatistics PreparedProgramCacheStatistics =>
        _preparedPrograms?.Statistics ?? default;

    public Task<MechanicRunResult> RunAsync(
        string source,
        MechanicProjection projection,
        ExecutionLimits limits,
        CancellationToken cancellationToken = default)
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
                (preparedMechanic, preparationCacheHit) = GetPreparedMechanic(source);
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

    private static bool IsPreparedProgramCacheDisabled() =>
        AppContext.TryGetSwitch(DisablePreparedProgramCacheSwitch, out var disabled) && disabled;

    private static void ThrowIfTimeoutElapsed(TimeSpan elapsed, TimeSpan timeout)
    {
        if (elapsed >= timeout) throw new TimeoutException();
    }

    private (Prepared<Script> Program, bool CacheHit) GetPreparedMechanic(string source)
    {
        var key = new PreparedProgramKey(
            source,
            MechanicWrapperPrefix,
            MechanicWrapperSuffix,
            PreparationConfigurationKey);

        return _preparedPrograms is null
            ? (PrepareMechanicProgram(source), false)
            : _preparedPrograms.GetOrPrepare(key, static cacheKey => PrepareMechanicProgram(cacheKey.Source));
    }

    /// <summary>
    /// Validates a mechanic body before embedding it, then prepares the exact executable wrapper
    /// used at runtime. Activation validation can call this same internal boundary and discard the
    /// result; it must not duplicate a looser parse.
    /// </summary>
    internal static Prepared<Script> PrepareMechanicProgram(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Parse only attacker-controlled text first. Allowing return/new.target gives this script
        // parse the relevant function-body grammar without giving a stray brace or comment any
        // trusted wrapper text to consume. Strict wrapper preparation below remains the final
        // executable grammar check.
        var parser = new Parser(new ParserOptions
        {
            AllowReturnOutsideFunction = true,
            AllowNewTargetOutsideFunction = true
        });
        parser.ParseScript(source, "mechanic-source.js", strict: true);

        return Engine.PrepareScript(
            MechanicWrapperPrefix + source + MechanicWrapperSuffix,
            "prepared-mechanic.js",
            strict: true);
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
    private const string Harness = """
        (function () {
          var log = [];

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
            var payload = JSON.parse(__payload);
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
              input: freezeDeep(JSON.parse(payload.input || '{}')),
              seed: payload.seed,
              children: freezeDeep(payload.children || {}),
              event: freezeDeep(JSON.parse(payload.event || '{}')),
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
              output.data = JSON.stringify(output.data);
            }

            // Only strings cross this boundary, so a declared event's payload is stringified here
            // rather than making every rule author remember to do it. An author who already passed
            // a string is left alone — double-encoding it would produce a payload that validates
            // against no schema at all.
            if (output && output.events && output.events.length) {
              for (var i = 0; i < output.events.length; i++) {
                var declared = output.events[i];
                if (declared && declared.payload !== undefined && typeof declared.payload !== 'string') {
                  declared.payload = JSON.stringify(declared.payload);
                }
              }
            }

            return JSON.stringify({ output: output, log: log });
          } catch (e) {
            return JSON.stringify({
              error: (e && e.message) ? String(e.message) : String(e),
              log: log
            });
          }
        })();
        """;
}

internal readonly record struct MechanicRunMeasurements(
    TimeSpan Preparation,
    TimeSpan ContextConstruction,
    TimeSpan Execution,
    bool PreparationCacheHit);

internal readonly record struct PreparedProgramCacheStatistics(
    int EntryCount,
    long RetainedSourceBytes,
    long PreparationCount,
    long CacheHitCount,
    long EvictionCount);

internal readonly record struct PreparedProgramKey(
    string Source,
    string WrapperPrefix,
    string WrapperSuffix,
    string ParserRuntimeConfiguration)
{
    public long SourceBytes => checked((long)Source.Length * sizeof(char));
}

/// <summary>
/// A small synchronized LRU for immutable, thread-safe Jint prepared programs. Source text is part
/// of the key so hash collisions cannot select a different executable. Programs larger than the
/// total source budget still run, but are prepared afresh and never retained.
/// </summary>
internal sealed class PreparedProgramCache(int countLimit, long sourceBytesLimit)
{
    private readonly object _gate = new();
    private readonly Dictionary<PreparedProgramKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _leastRecentlyUsed = [];
    private long _retainedSourceBytes;
    private long _preparationCount;
    private long _cacheHitCount;
    private long _evictionCount;

    public PreparedProgramCacheStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return new PreparedProgramCacheStatistics(
                    _entries.Count,
                    _retainedSourceBytes,
                    _preparationCount,
                    _cacheHitCount,
                    _evictionCount);
            }
        }
    }

    public (Prepared<Script> Program, bool CacheHit) GetOrPrepare(
        PreparedProgramKey key,
        Func<PreparedProgramKey, Prepared<Script>> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);

        if (key.SourceBytes > sourceBytesLimit)
        {
            var uncached = prepare(key);
            lock (_gate) _preparationCount++;
            return (uncached, false);
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _leastRecentlyUsed.Remove(existing);
                _leastRecentlyUsed.AddFirst(existing);
                _cacheHitCount++;
                return (existing.Value.Program, true);
            }

            // Preparation stays inside the lock so concurrent first callers cannot parse and
            // retain duplicate programs for the same exact source.
            var program = prepare(key);
            _preparationCount++;
            var node = _leastRecentlyUsed.AddFirst(new Entry(key, program));
            _entries.Add(key, node);
            _retainedSourceBytes += key.SourceBytes;

            while (_entries.Count > countLimit || _retainedSourceBytes > sourceBytesLimit)
            {
                var victim = _leastRecentlyUsed.Last!;
                _leastRecentlyUsed.RemoveLast();
                _entries.Remove(victim.Value.Key);
                _retainedSourceBytes -= victim.Value.Key.SourceBytes;
                _evictionCount++;
            }

            return (program, false);
        }
    }

    private sealed record Entry(PreparedProgramKey Key, Prepared<Script> Program);
}
