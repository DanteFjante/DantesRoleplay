using System.Text.Json;

namespace DantesRoleplay.Interactions.Tests;

public sealed class InteractionTaskAgendaTests
{
    [Fact]
    public void Single_preserves_the_exact_bounded_intent_in_one_task_and_batch()
    {
        var agenda = InteractionTaskAgenda.Single("Orban attacks the caravan driver.");

        var task = Assert.Single(agenda.Tasks);
        Assert.Equal("Orban attacks the caravan driver.", task.IntentText);
        Assert.Empty(task.DependsOn);
        Assert.Equal("Orban attacks the caravan driver.", Assert.Single(task.Batches).IntentText);
    }

    [Fact]
    public void Strict_agenda_accepts_ordered_tasks_and_has_a_canonical_fingerprint()
    {
        const string first = """
            {"tasks":[{"intentText":"Prepare","dependsOn":[],"batches":[{"intentText":"Inspect"},{"intentText":"Prepare"}]},{"intentText":"Finish","dependsOn":[1],"batches":[{"intentText":"Finish"}]}]}
            """;
        const string reordered = """
            {"tasks":[{"batches":[{"intentText":"Inspect"},{"intentText":"Prepare"}],"dependsOn":[],"intentText":"Prepare"},{"dependsOn":[1],"intentText":"Finish","batches":[{"intentText":"Finish"}]}]}
            """;

        var agenda = InteractionTaskAgenda.Parse(first);
        var equivalent = InteractionTaskAgenda.Parse(reordered);

        Assert.Equal(2, agenda.Tasks.Count);
        Assert.Equal([1], agenda.Tasks[1].DependsOn);
        Assert.Equal(2, agenda.Tasks[0].Batches.Count);
        Assert.Equal(agenda.Fingerprint, equivalent.Fingerprint);
    }

    [Theory]
    [InlineData("{\"tasks\":[]}")]
    [InlineData("{\"tasks\":[{\"intentText\":\"A\",\"dependsOn\":[1],\"batches\":[{\"intentText\":\"A\"}]}]}")]
    [InlineData("{\"tasks\":[{\"intentText\":\"A\",\"dependsOn\":[],\"batches\":[],\"extra\":true}]}")]
    [InlineData("{\"tasks\":[{\"intentText\":\" A\",\"dependsOn\":[],\"batches\":[{\"intentText\":\"A\"}]}]}")]
    [InlineData("{\"tasks\":[{\"intentText\":\"A\",\"dependsOn\":[],\"batches\":[{\"intentText\":\"A\",\"tool\":\"x\"}]}]}")]
    public void Malformed_agendas_fail_closed(string json)
    {
        var error = Assert.Throws<InteractionContractException>(() => InteractionTaskAgenda.Parse(json));
        Assert.Equal("TASK_AGENDA_INVALID", error.Code);
    }

    [Fact]
    public void Exact_bounds_pass_and_total_batch_limit_plus_one_fails()
    {
        var exact = new
        {
            tasks = Enumerable.Range(1, 4).Select(task => new
            {
                intentText = $"Task {task}",
                dependsOn = task == 1 ? Array.Empty<int>() : new[] { task - 1 },
                batches = Enumerable.Range(1, 4).Select(batch => new { intentText = $"Batch {task}.{batch}" })
            })
        };
        Assert.Equal(16, InteractionTaskAgenda.Parse(JsonSerializer.Serialize(exact))
            .Tasks.Sum(task => task.Batches.Count));

        var tooMany = new
        {
            tasks = Enumerable.Range(1, 5).Select(task => new
            {
                intentText = $"Task {task}",
                dependsOn = Array.Empty<int>(),
                batches = Enumerable.Range(1, task == 5 ? 1 : 4)
                    .Select(batch => new { intentText = $"Batch {task}.{batch}" })
            })
        };
        Assert.Equal("TASK_AGENDA_INVALID",
            Assert.Throws<InteractionContractException>(() =>
                InteractionTaskAgenda.Parse(JsonSerializer.Serialize(tooMany))).Code);
    }

    [Fact]
    public void Task_dependency_batch_and_utf8_text_boundaries_are_exact()
    {
        var twoThousandBytes = new string('é', 1_000);
        var exact = new
        {
            tasks = Enumerable.Range(1, 8).Select(task => new
            {
                intentText = task == 1 ? twoThousandBytes : $"Task {task}",
                dependsOn = task == 8 ? new[] { 1, 2, 3, 4 } : Array.Empty<int>(),
                batches = new[] { new { intentText = $"Batch {task}.1" }, new { intentText = $"Batch {task}.2" } }
            })
        };
        Assert.Equal(8, InteractionTaskAgenda.Parse(JsonSerializer.Serialize(exact)).Tasks.Count);

        var tooManyTasks = new
        {
            tasks = Enumerable.Range(1, 9).Select(task => new
            {
                intentText = $"Task {task}", dependsOn = Array.Empty<int>(),
                batches = new[] { new { intentText = "Batch" } }
            })
        };
        Assert.Throws<InteractionContractException>(() =>
            InteractionTaskAgenda.Parse(JsonSerializer.Serialize(tooManyTasks)));

        var tooManyDependencies = new
        {
            tasks = Enumerable.Range(1, 6).Select(task => new
            {
                intentText = $"Task {task}",
                dependsOn = task == 6 ? new[] { 1, 2, 3, 4, 5 } : Array.Empty<int>(),
                batches = new[] { new { intentText = "Batch" } }
            })
        };
        Assert.Throws<InteractionContractException>(() =>
            InteractionTaskAgenda.Parse(JsonSerializer.Serialize(tooManyDependencies)));

        var tooManyBytes = new
        {
            tasks = new[] { new { intentText = twoThousandBytes + "a", dependsOn = Array.Empty<int>(),
                batches = new[] { new { intentText = "Batch" } } } }
        };
        Assert.Throws<InteractionContractException>(() =>
            InteractionTaskAgenda.Parse(JsonSerializer.Serialize(tooManyBytes)));
    }
}
