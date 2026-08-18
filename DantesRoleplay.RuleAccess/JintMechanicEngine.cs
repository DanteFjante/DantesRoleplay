using System.Diagnostics;
using System.Text.Json;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using Jint;
using Jint.Runtime;

namespace DantesRoleplay.RuleAccess;

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
/// A fresh engine per run, so nothing a mechanic leaves behind can be seen by the next one.
/// </summary>
public sealed class JintMechanicEngine : IMechanicEngine
{
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

        // Jint is synchronous and the limits are enforced on the running thread, so there is
        // nothing to await. Returning a Task keeps the interface honest about the possibility of
        // an out-of-process engine later without pretending this one is asynchronous now.
        try
        {
            var engine = new Engine(options =>
            {
                // Deliberately NOT options.AllowClr(). See the class comment. If a future change
                // needs a .NET type inside a mechanic, that is a design conversation, not a flag.
                options.LimitMemory(limits.MemoryBytes);
                options.TimeoutInterval(limits.Timeout);
                options.MaxStatements(limits.MaxStatements);
                options.LimitRecursion(limits.MaxRecursionDepth);
                options.CancellationToken(cancellationToken);

                // Strict mode, so `total = 5` without a declaration is an error rather than a
                // silent global. The author is an LLM and the error message is how it learns.
                options.Strict();
            });

            var payload = JsonSerializer.Serialize(
                new
                {
                    roles = projection.Roles,
                    input = projection.Input,
                    seed = projection.Seed,
                    children = projection.Children
                },
                Json);

            engine.SetValue("__payload", payload);
            engine.SetValue("__source", source);
            engine.SetValue("__maxLog", limits.MaxLogLines);

            var completion = engine.Evaluate(Harness).AsString();

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

        return new MechanicRunResult
        {
            Ok = true,
            Output = new MechanicOutput
            {
                Effects = effects,
                Narration = raw.Output.Narration ?? string.Empty,
                Data = raw.Output.Data ?? "{}"
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

        public string? Narration { get; set; }

        public string? Data { get; set; }
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
              if (!value || typeof value !== 'object' || Object.isFrozen(value)) { return value; }
              Object.keys(value).forEach(function (key) { freezeDeep(value[key]); });
              return Object.freeze(value);
            }

            var ctx = {
              roles: payload.roles,
              input: JSON.parse(payload.input || '{}'),
              seed: payload.seed,
              children: freezeDeep(payload.children || {}),

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

              // Everything a mechanic may read is already in ctx.roles. There is no fetch, no
              // query and no store here on purpose: that absence is what makes a mechanic a pure
              // function of what it declared, and therefore reviewable.
              effects: []
            };

            var fn = new Function('ctx', __source);
            var output = fn(ctx);

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
