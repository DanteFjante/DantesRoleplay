using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Play.Tests;

public sealed class PlayRecordingTests : IDisposable
{
    private readonly SqliteFixture fixture = new();

    [Fact]
    public void Conversation_resumes_with_verbatim_messages_situation_and_truths()
    {
        string conversationId;
        using (var db = fixture.CreateContext())
        {
            RegisterStateSpace(db);
            var store = new ApplicationPlayRecordStore(db);
            var conversation = store.ResumeOrCreate(Identity());
            conversationId = conversation.Id;
            var playerText = "I say, \"The bell rang twice.\"\nThen I wait.";
            conversation = store.AppendMessage(conversation.Id,
                new("player", playerText, null, DateTime.UtcNow), "ready");
            conversation = store.AppendNarrative(conversation.Id, new(
                new("assistant", "Orban answers word for word: \"I heard it too.\"", "NARRATION_COMPLETED", DateTime.UtcNow),
                new(PlaySituationTransitions.Replace, PlaySituationKinds.Conversation,
                    "The player is speaking with Orban beside the old bell.",
                    [new("Orban", "actor.thalorien.brackenford.orban")],
                    new("Old bell tower", "location.thalorien.brackenford.bell-tower")),
                [new("The old bell rang twice.", ["location.thalorien.brackenford.bell-tower"])]), "ready");

            Assert.Equal(playerText, conversation.RecentMessages[0].Text);
            Assert.Equal("Orban answers word for word: \"I heard it too.\"", conversation.RecentMessages[1].Text);
            Assert.Equal(PlaySituationKinds.Conversation, conversation.CurrentSituation!.Kind);
            Assert.Equal("Orban", Assert.Single(conversation.CurrentSituation.Participants).Name);
            Assert.Equal("The old bell rang twice.", Assert.Single(conversation.KnownTruths).Statement);
        }

        using (var db = fixture.CreateContext())
        {
            var resumed = new ApplicationPlayRecordStore(db).ResumeOrCreate(Identity());
            Assert.Equal(conversationId, resumed.Id);
            Assert.Equal(2, resumed.TotalMessageCount);
            Assert.Equal("I say, \"The bell rang twice.\"\nThen I wait.", resumed.RecentMessages[0].Text);
            Assert.Equal(PlaySituationKinds.Conversation, resumed.CurrentSituation!.Kind);
            Assert.Equal("The old bell rang twice.", Assert.Single(resumed.KnownTruths).Statement);
        }
    }

    [Fact]
    public void Replacing_a_situation_preserves_history_and_message_paging()
    {
        using var db = fixture.CreateContext();
        RegisterStateSpace(db);
        var store = new ApplicationPlayRecordStore(db);
        var conversation = store.ResumeOrCreate(Identity());
        conversation = store.AppendNarrative(conversation.Id, new(
            new("assistant", "Orban begins speaking.", null, DateTime.UtcNow),
            new(PlaySituationTransitions.Replace, PlaySituationKinds.Conversation,
                "A conversation begins.", [new("Orban", null)]), []), "ready");
        var conversationSituation = conversation.CurrentSituation!.Id;
        conversation = store.AppendMessage(conversation.Id,
            new("player", "I draw my sword.", null, DateTime.UtcNow), "ready");
        conversation = store.AppendNarrative(conversation.Id, new(
            new("assistant", "The ambushers rush from the hedges.", null, DateTime.UtcNow),
            new(PlaySituationTransitions.Replace, PlaySituationKinds.Combat,
                "Combat with the roadside ambushers.", [new("Roadside ambushers", null)]),
            [new("Ambushers were concealed beside the road.", [])]), "ready");

        Assert.Equal(PlaySituationKinds.Combat, conversation.CurrentSituation!.Kind);
        var prior = db.ApplicationPlaySituations.Single(value => value.Id == conversationSituation);
        Assert.Equal(PlaySituationStatuses.Completed, prior.Status);
        Assert.NotNull(prior.CompletedAtUtc);
        var latest = store.GetMessages(Identity().PrincipalId, Identity().ApplicationId,
            conversation.Id, null, 2);
        Assert.Equal([2, 3], latest.Messages.Select(value => value.Ordinal).ToArray());
        Assert.Equal(2, latest.NextBeforeOrdinal);
        var earlier = store.GetMessages(Identity().PrincipalId, Identity().ApplicationId,
            conversation.Id, latest.NextBeforeOrdinal, 2);
        Assert.Equal(1, Assert.Single(earlier.Messages).Ordinal);
        Assert.Null(earlier.NextBeforeOrdinal);
    }

    [Fact]
    public void Duplicate_truth_text_is_not_recorded_twice()
    {
        using var db = fixture.CreateContext();
        RegisterStateSpace(db);
        var store = new ApplicationPlayRecordStore(db);
        var conversation = store.ResumeOrCreate(Identity());
        var statement = "The northern gate is barred.";
        conversation = store.AppendNarrative(conversation.Id,
            new(new("assistant", statement, null, DateTime.UtcNow), null, [new(statement, [])]), "ready");
        conversation = store.AppendNarrative(conversation.Id,
            new(new("assistant", statement, null, DateTime.UtcNow), null,
                [new("  the northern gate IS barred.  ", [])]), "ready");

        Assert.Single(conversation.KnownTruths);
        Assert.Equal(statement, conversation.KnownTruths[0].Statement);
    }

    [Fact]
    public async Task Concurrent_contexts_share_one_database_gate_and_preserve_message_order()
    {
        string conversationId;
        using (var setup = fixture.CreateContext())
        {
            RegisterStateSpace(setup);
            conversationId = new ApplicationPlayRecordStore(setup).ResumeOrCreate(Identity()).Id;
        }
        using var firstDb = fixture.CreateContext();
        using var secondDb = fixture.CreateContext();
        var first = new ApplicationPlayRecordStore(firstDb);
        var second = new ApplicationPlayRecordStore(secondDb);
        Assert.Same(ApplicationPlayWriteCoordinator.For(firstDb), ApplicationPlayWriteCoordinator.For(secondDb));
        using var start = new ManualResetEventSlim();

        await Task.WhenAll(
            Task.Run(() => { start.Wait(); first.AppendMessage(conversationId,
                new("player", "First concurrent message.", null, DateTime.UtcNow), "ready"); }),
            Task.Run(() => { start.Wait(); second.AppendMessage(conversationId,
                new("assistant", "Second concurrent message.", null, DateTime.UtcNow), "ready"); }),
            Task.Run(() => start.Set()));

        using var verify = fixture.CreateContext();
        var result = new ApplicationPlayRecordStore(verify).GetMessages(
            Identity().PrincipalId, Identity().ApplicationId, conversationId, null, 10);
        Assert.Equal([1, 2], result.Messages.Select(value => value.Ordinal).ToArray());
        Assert.Equal(2, result.Messages.Select(value => value.Text).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, new ApplicationPlayRecordStore(verify).Get(
            Identity().PrincipalId, Identity().ApplicationId, conversationId)!.Revision);
    }

    [Fact]
    public void Play_write_coordination_is_bounded_and_not_one_global_database_gate()
    {
        var stripes = new HashSet<int>();
        for (var index = 0; index < 128; index++)
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"play-stripe-{index}.db")}")
                .Options;
            using var db = new DantesRoleplayDbContext(options);
            stripes.Add(ApplicationPlayWriteCoordinator.StripeFor(db));
        }

        Assert.InRange(stripes.Count, 2, ApplicationPlayWriteCoordinator.StripeCount);
    }

    private static PlayConversationIdentity Identity() => new(
        "principal.fixture", "play-fixture", "play-fixture-space", "session.fixture");

    private static void RegisterStateSpace(DantesRoleplayDbContext db)
    {
        if (db.ApplicationPlayConversations.Any() || db.Set<ApplicationStateSpaceRecord>().Any()) return;
        var applications = new SqliteApplicationRegistry(db);
        var application = ApplicationIdentifier.Parse("play-fixture");
        var revision = applications.Register(new(application, "Play Fixture", "", []));
        new SqliteStateSpaceRegistry(db, applications).Create(
            new("play-fixture-space", revision, new string('A', 64)));
    }

    public void Dispose() => fixture.Dispose();
}
