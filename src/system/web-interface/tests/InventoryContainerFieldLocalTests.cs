using System.Text.Json;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Tests;

public sealed class InventoryContainerFieldLocalTests
{
    [Fact]
    public async Task Jint_keeps_additive_fields_and_malformed_optional_neighbor_visible()
    {
        var mechanic = ReadMechanic();
        var good = new ContainedProjection("item.bag", "Travel Bag", "carried",
            new Dictionary<string, string>
            {
                ["dnd2024.core.definition-link"] = Json(new
                {
                    definition = new { entityId = "definition.bag", label = "Ignored" },
                    producerMetadata = new { version = 3 }
                }),
                ["dnd2024.item.quantity"] = Json(new { current = 2, unit = "count" }),
                ["dnd2024.item.equipment"] = Json(new
                {
                    equippedBy = new { entityId = "actor.test", displayName = "Ignored" },
                    slots = new[] { new { entityId = "slot.belt", label = "Ignored" } },
                    future = true
                })
            });
        var malformed = new ContainedProjection("item.unknown-facet", "Sealed Parcel", "carried",
            new Dictionary<string, string>
            {
                ["dnd2024.core.definition-link"] = Json(new
                {
                    definition = new { entityId = "definition.knife" },
                    future = "ignored"
                }),
                ["dnd2024.item.quantity"] = "{\"current\":\"unknown\"}",
                ["dnd2024.item.equipment"] = "not-json",
                ["dnd2024.item.container"] = "not-json"
            });
        var projection = new MechanicProjection
        {
            Input = "{}",
            Roles =
            {
                ["subject"] = new EntityProjection("actor.test", "Container", new Dictionary<string, string>(),
                    Contains: [good, malformed])
            },
            References =
            {
                ["definition.bag"] = new ReferencedEntityProjection("definition.bag",
                    new Dictionary<string, string>
                    {
                        ["dnd2024.item.container"] = Json(new { capacity = new { itemCount = 10 }, future = true })
                    }, "Travel Bag"),
                ["definition.knife"] = new ReferencedEntityProjection("definition.knife",
                    new Dictionary<string, string>(), "Sealed Parcel")
            }
        };

        var assembled = await SnapshotObjectTestHarness.AssembleAsync(mechanic, projection);
        var result = await new JintMechanicEngine().RunAsync(mechanic.Source, assembled, ExecutionLimits.Default);

        Assert.True(result.Ok, result.Error);
        Assert.Empty(result.Output.Effects);
        Assert.Empty(result.Output.Events);
        Assert.Empty(result.Output.Notifications);
        using var output = JsonDocument.Parse(result.Output.Data);
        var items = output.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(2, items[0].GetProperty("quantity").GetInt32());
        Assert.Equal("slot.belt", items[0].GetProperty("equipmentSlots")[0].GetProperty("id").GetString());
        Assert.True(items[0].GetProperty("isContainer").GetBoolean());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("quantity").ValueKind);
        Assert.False(items[1].TryGetProperty("equipmentSlots", out _));
        Assert.False(items[1].TryGetProperty("isContainer", out _));
        Assert.Equal("partial", output.RootElement.GetProperty("state").GetString());
        Assert.Contains("source-incomplete", output.RootElement.GetProperty("reasons").EnumerateArray()
            .Select(value => value.GetString()));
    }

    private static MechanicFile ReadMechanic()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DantesRoleplay.slnx")))
            root = root.Parent;
        var path = Path.Combine(root!.FullName, "catalog", "applications", "dnd2024", "mechanics", "data",
            "dnd2024.mechanic.inventory-container-page.project");
        return MechanicFile.Parse(File.ReadAllText(path + ".md"), path + ".md", File.ReadAllText(path + ".js"));
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
