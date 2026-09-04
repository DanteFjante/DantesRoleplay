using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024ClockBridgeTests
{
    [Fact]
    public async Task Clock_advance_preserves_calendar_and_moves_one_coordinate_forward()
    {
        var result = await RunAsync("{\"minutes\":60}",
            "{\"calendarId\":\"calendar.fixture\",\"currentMinute\":100,\"revision\":7}");

        Assert.True(result.Ok, result.Error);
        var effect = Assert.Single(result.Output.Effects);
        Assert.Equal("clock.advance", effect.Type);
        Assert.Equal("world.fixture", effect.EntityId);
        Assert.Equal("game.core.world.clock", effect.DefinitionId);
        Assert.Equal(
            "{\"calendarId\":\"calendar.fixture\",\"currentMinute\":160,\"revision\":8}",
            effect.Data);
        Assert.Equal("calendar.fixture", effect.CalendarId);
        Assert.Equal(100, effect.PreviousMinute);
        Assert.Equal(60, effect.DeltaMinutes);
        Assert.Equal(160, effect.ResultingMinute);
        Assert.Equal(7, effect.PreviousClockRevision);
        Assert.Equal(8, effect.ResultingClockRevision);
        Assert.Equal("game.core.world.clock.advanced", effect.EventTypeId);
        Assert.Equal("world.fixture", effect.SubjectEntityId);
        Assert.Equal("dnd2024.mechanic.world.clock.advance", effect.ActivityId);
        Assert.Empty(result.Output.Events);
        Assert.Empty(result.Output.Notifications);
    }

    [Theory]
    [InlineData("{\"minutes\":0}", "{\"calendarId\":\"calendar.fixture\",\"currentMinute\":100,\"revision\":7}")]
    [InlineData("{\"minutes\":1,\"currentMinute\":0}", "{\"calendarId\":\"calendar.fixture\",\"currentMinute\":100,\"revision\":7}")]
    [InlineData("{\"minutes\":1}", "{\"calendarId\":\"calendar.fixture\",\"currentMinute\":1000000000,\"revision\":7}")]
    [InlineData("{\"minutes\":1}", "{\"calendarId\":\"calendar.fixture\",\"currentMinute\":100,\"revision\":2147483647}")]
    public async Task Clock_advance_rejects_non_forward_or_caller_derived_state(
        string input,
        string clock)
    {
        var result = await RunAsync(input, clock);

        Assert.False(result.Ok);
        Assert.Empty(result.Output.Effects);
    }

    private static async Task<MechanicRunResult> RunAsync(string input, string clock)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(), "catalog", "applications", "dnd2024", "mechanics", "world",
            "dnd2024.mechanic.world.clock.advance.js"));
        return await new JintMechanicEngine().RunAsync(source, new MechanicProjection
        {
            Input = input,
            Roles = new()
            {
                ["world"] = new EntityProjection("world.fixture", "Fixture World",
                    new Dictionary<string, string>
                    {
                        ["game.core.world.root"] =
                            "{\"status\":\"active\",\"summary\":\"Fixture.\",\"visibility\":\"party\"}",
                        ["game.core.world.clock"] = clock
                    })
            }
        }, ExecutionLimits.Default);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
