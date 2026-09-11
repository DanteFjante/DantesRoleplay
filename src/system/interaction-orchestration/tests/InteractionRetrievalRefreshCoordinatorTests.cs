using DantesRoleplay.Interactions;

namespace DantesRoleplay.Tests;

public sealed class InteractionRetrievalRefreshCoordinatorTests
{
    [Fact]
    public async Task Active_generation_capacity_is_finite_and_released_after_completion()
    {
        var coordinator = new InteractionRetrievalRefreshCoordinator();
        var release = new TaskCompletionSource<InteractionFeatureRebuildResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owners = Enumerable.Range(0, InteractionRetrievalRefreshCoordinator.MaximumGenerations)
            .Select(index => coordinator.RunAsync(index.ToString(), () => release.Task, CancellationToken.None)).ToArray();
        try
        {
            var overflow = await coordinator.RunAsync("overflow", () => throw new InvalidOperationException(), CancellationToken.None);
            Assert.Equal("VECTOR_REFRESH_BUSY", overflow.AvailabilityCode);
        }
        finally { release.TrySetResult(new(true, 0, "complete")); }
        await Task.WhenAll(owners);
        var available = await coordinator.RunAsync("overflow", () => Task.FromResult(new InteractionFeatureRebuildResult(true, 0, "overflow")), CancellationToken.None);
        Assert.True(available.Rebuilt);
    }

    [Fact]
    public async Task Waiter_capacity_is_finite_and_cancellation_releases_waiter_slots()
    {
        var coordinator = new InteractionRetrievalRefreshCoordinator();
        var release = new TaskCompletionSource<InteractionFeatureRebuildResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = coordinator.RunAsync("generation", () => release.Task, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        var waiters = Enumerable.Range(0, InteractionRetrievalRefreshCoordinator.MaximumWaitersPerGeneration)
            .Select(_ => coordinator.RunAsync("generation", () => throw new InvalidOperationException(), cancelled.Token)).ToArray();
        try
        {
            Assert.Equal("VECTOR_REFRESH_BUSY", (await coordinator.RunAsync("generation",
                () => throw new InvalidOperationException(), CancellationToken.None)).AvailabilityCode);
            cancelled.Cancel();
            foreach (var waiter in waiters) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
            var replacement = coordinator.RunAsync("generation", () => throw new InvalidOperationException(), CancellationToken.None);
            Assert.False(replacement.IsCompleted);
            release.TrySetResult(new(true, 0, "generation"));
            Assert.True((await replacement).Rebuilt);
        }
        finally { cancelled.Cancel(); release.TrySetResult(new(true, 0, "generation")); }
        await owner;
    }

    [Fact]
    public async Task Waiter_timeout_preserves_owner_and_owner_failure_permits_retry()
    {
        var coordinator = new InteractionRetrievalRefreshCoordinator();
        var release = new TaskCompletionSource<InteractionFeatureRebuildResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = coordinator.RunAsync("generation", () => release.Task, CancellationToken.None);
        try
        {
            var waiter = await coordinator.RunAsync("generation", () => throw new InvalidOperationException(), CancellationToken.None);
            Assert.Equal("VECTOR_REFRESH_BUSY", waiter.AvailabilityCode);
            Assert.False(owner.IsCompleted);
        }
        finally { release.TrySetResult(new(false, 0, AvailabilityCode: "FIXTURE_FAILURE")); }
        Assert.False((await owner).Rebuilt);
        Assert.True((await coordinator.RunAsync("generation", () => Task.FromResult(new InteractionFeatureRebuildResult(true, 0, "generation")), CancellationToken.None)).Rebuilt);
    }
}
