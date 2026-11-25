using System;
using System.Collections.Generic;
using System.Threading;
using Godot;

namespace Minecraft;

/// <summary>
/// Deterministic coverage for Far-LOD lifecycle scheduling and geometry ownership.
/// Tests exercise the same queue, completion, retry and cell-gating code used by
/// FarLod without relying on nondeterministic worker timing or renderer state.
/// </summary>
public static class FarLodLifecycleTests
{
    public static void Run()
    {
        TestRetainedChunkOwnershipBoundary();
        TestBuildTokenRejectsStaleAndRecreatedTiles();
        TestEveryCompletionHasAnExplicitDisposition();
        TestWorkerFailureRetryPolicyAndQueueCapacity();
        TestInjectedWorkerExceptionReleasesCapacity();
        TestShutdownCancellationIsNotATileFailure();
        TestInFlightReleaseIsIdentitySafe();
        TestStaleQueueEntriesArePrunedAndBounded();
        TestCompletionPathReleasesAndClassifiesAtomically();
        TestEvictedCompletionsDispatchReplacementTiles();
        TestCellGeometryRespectsFullResolutionOwnership();
    }

    private static void TestRetainedChunkOwnershipBoundary()
    {
        Vector2I playerChunk = new(-5, 3);
        Rect2I retained = FarLodLifecycleRules.StreamedChunks(playerChunk, 2, VoxelWorld.UnloadMargin);

        TestAssert.Equal(new Vector2I(-9, -1), retained.Position,
                "Far LOD's retained full-resolution rectangle expands by the two-chunk unload margin");
        TestAssert.Equal(new Vector2I(9, 9), retained.Size,
                "Far LOD keeps the complete square used by chunk unload hysteresis");

        TestAssert.Check(retained.HasPoint(new Vector2I(-9, 3)),
                "negative-X retained boundary remains full resolution");
        TestAssert.Check(retained.HasPoint(new Vector2I(-1, 3)),
                "positive-X retained boundary remains full resolution");
        TestAssert.Check(retained.HasPoint(new Vector2I(-5, -1)),
                "negative-Z retained boundary remains full resolution");
        TestAssert.Check(retained.HasPoint(new Vector2I(-5, 7)),
                "positive-Z retained boundary remains full resolution");
        TestAssert.Check(retained.HasPoint(new Vector2I(-9, -1)),
                "negative corner is included in the retained square");
        TestAssert.Check(!retained.HasPoint(new Vector2I(-10, 3)),
                "chunks beyond the unload hysteresis ring are eligible for Far LOD");
        TestAssert.Check(!retained.HasPoint(new Vector2I(-5, 8)),
                "the retained boundary is symmetric on positive Z");
    }

    private static void TestBuildTokenRejectsStaleAndRecreatedTiles()
    {
        Rect2I originalSkip = new(2, 3, 4, 5);
        Rect2I updatedSkip = new(3, 3, 4, 5);

        TestAssert.Check(FarLodLifecycleRules.IsCurrent(
                        11, 4, originalSkip, 11, 4, originalSkip),
                "matching generation, revision and skip mask may apply");
        TestAssert.Check(!FarLodLifecycleRules.IsCurrent(
                        11, 5, updatedSkip, 11, 4, originalSkip),
                "a changed streamed-area revision rejects an older result");
        TestAssert.Check(!FarLodLifecycleRules.IsCurrent(
                        12, 4, originalSkip, 11, 4, originalSkip),
                "a tile recreated at the same coordinate rejects the previous generation");
        TestAssert.Check(!FarLodLifecycleRules.IsCurrent(
                        11, 4, updatedSkip, 11, 4, originalSkip),
                "a changed skip rectangle rejects a result even if its revision was not advanced");
    }

    private static void TestEveryCompletionHasAnExplicitDisposition()
    {
        TestAssert.Equal(FarLodCompletionDisposition.Discard,
                FarLodLifecycleRules.ClassifyCompletion(false, false, false),
                "an evicted tile completion is discarded without applying its payload");
        TestAssert.Equal(FarLodCompletionDisposition.RebuildLatest,
                FarLodLifecycleRules.ClassifyCompletion(true, false, false),
                "a stale completion requests a build for the current tile state");
        TestAssert.Equal(FarLodCompletionDisposition.RebuildLatest,
                FarLodLifecycleRules.ClassifyCompletion(true, false, true),
                "a failed old-generation task is superseded by the current tile request");
        TestAssert.Equal(FarLodCompletionDisposition.Failed,
                FarLodLifecycleRules.ClassifyCompletion(true, true, true),
                "a failure for the current tile enters bounded retry handling");
        TestAssert.Equal(FarLodCompletionDisposition.Apply,
                FarLodLifecycleRules.ClassifyCompletion(true, true, false),
                "a current successful result, including an empty result, is eligible for apply");
    }

    private static void TestInjectedWorkerExceptionReleasesCapacity()
    {
        const string injectedMessage = "injected Far LOD worker failure";
        FarLodBuildOutcome<string> outcome = FarLodLifecycleRules.CaptureBuild<string>(
                () => throw new InvalidOperationException(injectedMessage));

        TestAssert.Check(outcome.Payload == null,
                "an injected BuildArrays-style exception produces no mesh payload");
        TestAssert.Check(outcome.Failure != null && outcome.Failure.Contains(injectedMessage),
                "the production worker capture seam preserves the exception for main-thread completion");

        Vector2I failedTile = new(4, -6);
        Dictionary<Vector2I, long> inFlight = new() { [failedTile] = 7001 };
        TestAssert.Check(FarLodLifecycleRules.TryCompleteInFlight(
                        inFlight, failedTile, 7001L, true, true, outcome.Failure != null,
                        out FarLodCompletionDisposition disposition),
                "the injected worker exception reaches the exact-request completion path");
        TestAssert.Equal(FarLodCompletionDisposition.Failed, disposition,
                "a captured worker exception enters bounded retry handling");
        TestAssert.Equal(0, inFlight.Count,
                "the captured exception releases its worker slot");

        FarLodWorkQueue queue = new();
        Dictionary<Vector2I, byte> liveTiles = new()
        {
            [new Vector2I(9, 2)] = 0,
        };
        Vector2I replacement = new(9, 2);
        queue.Enqueue(replacement);
        TestAssert.Check(FarLodLifecycleRules.TryDequeueEligibleTile(
                        queue, liveTiles, inFlight, out Vector2I nextTile, out byte _),
                "the production dispatch selector can dequeue work after a failed task releases capacity");
        TestAssert.Equal(replacement, nextTile,
                "the tile queued behind the failed worker is the next dispatched tile");
    }

    private static void TestShutdownCancellationIsNotATileFailure()
    {
        using CancellationTokenSource shutdown = new();
        FarLodBuildOutcome<string> outcome = FarLodLifecycleRules.CaptureBuild<string>(() =>
        {
            // Model an active tile build observing scene shutdown at a safe
            // point after work has started but before it publishes a result.
            shutdown.Cancel();
            shutdown.Token.ThrowIfCancellationRequested();
            return "stale tile payload";
        }, shutdown.Token);

        TestAssert.Check(outcome.Cancelled,
                "Far-LOD shutdown cancellation is represented separately from a worker exception");
        TestAssert.Check(outcome.Payload == null && outcome.Failure == null,
                "a cancelled Far-LOD build produces neither a payload nor a retryable failure");
    }

    private static void TestInFlightReleaseIsIdentitySafe()
    {
        Vector2I tile = new(7, -4);
        Dictionary<Vector2I, long> inFlight = new() { [tile] = 991 };

        TestAssert.Check(!FarLodLifecycleRules.IsMatchingBuildRequest(991, 992),
                "a delayed completion cannot match a later dispatch for the unchanged tile");
        TestAssert.Check(FarLodLifecycleRules.IsMatchingBuildRequest(991, 991),
                "the active request identity matches its own terminal callback");
        TestAssert.Check(!FarLodLifecycleRules.TryReleaseInFlight(inFlight, tile, 992L),
                "a stale completion cannot release a different task registered at the same coordinate");
        TestAssert.Equal(1, inFlight.Count,
                "a mismatched completion leaves the current in-flight entry intact");

        TestAssert.Check(FarLodLifecycleRules.TryReleaseInFlight(inFlight, tile, 991L),
                "a failed or evicted task releases its own in-flight entry");
        TestAssert.Equal(0, inFlight.Count,
                "releasing the completed task frees the worker slot");
        TestAssert.Check(!FarLodLifecycleRules.TryReleaseInFlight(inFlight, tile, 991L),
                "a duplicate terminal callback cannot release the same task twice");
        TestAssert.Check(FarLodLifecycleRules.CanDispatch(inFlight.Count, 1, 3, true),
                "queued work becomes dispatchable immediately after completion releases capacity");
    }

    private static void TestWorkerFailureRetryPolicyAndQueueCapacity()
    {
        int failures = 0;
        int retriesScheduled = 0;
        for (int attempt = 0; attempt < FarLodLifecycleRules.MaxBuildRetries + 1; attempt++)
        {
            failures++;
            if (FarLodLifecycleRules.CanRetry(failures - 1))
            {
                retriesScheduled++;
            }
        }

        TestAssert.Equal(3, failures,
                "one initial attempt plus the retry budget are the only failed attempts");
        TestAssert.Equal(FarLodLifecycleRules.MaxBuildRetries, retriesScheduled,
                "retry exhaustion schedules no additional build");
        TestAssert.Check(!FarLodLifecycleRules.CanRetry(failures - 1),
                "a tile remains exhausted until a new tile revision resets its failure count");

        TestAssert.Check(FarLodLifecycleRules.CanDispatch(2, 1, 3, true),
                "a released worker slot allows queued tile work to progress");
        TestAssert.Check(!FarLodLifecycleRules.CanDispatch(3, 1, 3, true),
                "the worker limit remains enforced");
        TestAssert.Check(!FarLodLifecycleRules.CanDispatch(2, 1, 3, false),
                "scene shutdown never dispatches replacement work");
        TestAssert.Check(!FarLodLifecycleRules.CanDispatch(0, 0, 3, true),
                "an empty queue does not schedule unnecessary work");
    }

    private static void TestStaleQueueEntriesArePrunedAndBounded()
    {
        FarLodWorkQueue queue = new();
        Dictionary<Vector2I, byte> liveTiles = new();
        Dictionary<Vector2I, long> occupiedSlots = new()
        {
            [new Vector2I(-1, 0)] = 1,
            [new Vector2I(-2, 0)] = 2,
            [new Vector2I(-3, 0)] = 3,
        };

        // Simulate repeated player-region changes while every worker slot is
        // occupied. Each update queues a new region, then evicts the old one.
        for (int movement = 0; movement < 20; movement++)
        {
            liveTiles.Clear();
            for (int i = 0; i < 50; i++)
            {
                Vector2I coordinate = new(movement * 100 + i, -movement * 100 - i);
                liveTiles.Add(coordinate, 0);
                if (!occupiedSlots.ContainsKey(coordinate))
                {
                    queue.Enqueue(coordinate);
                }
            }

            TestAssert.Check(!FarLodLifecycleRules.CanDispatch(
                            occupiedSlots.Count, queue.Count, 3, true),
                    "full worker capacity keeps queued movement batches pending");
            queue.Prune(liveTiles);
            TestAssert.Equal(liveTiles.Count, queue.Count,
                    "repeated movement pruning bounds pending entries by live tiles");
        }

        Vector2I duplicate = new(1_900, -1_900);
        TestAssert.Check(!queue.Enqueue(duplicate),
                "a coordinate cannot be inserted twice while already queued");

        for (int i = 0; i < 50; i++)
        {
            TestAssert.Check(queue.TryDequeue(out Vector2I coordinate),
                    "the final live movement batch remains dequeueable");
            TestAssert.Equal(new Vector2I(1_900 + i, -1_900 - i), coordinate,
                    "pruning preserves FIFO order for the newest live region");
        }

        TestAssert.Equal(0, queue.Count,
                "dequeueing the newest region empties the queue");
        TestAssert.Check(!queue.TryDequeue(out _),
                "an empty queue has no phantom stale coordinate");
    }

    private static void TestCompletionPathReleasesAndClassifiesAtomically()
    {
        Vector2I tile = new(4, 9);
        Dictionary<Vector2I, long> inFlight = new() { [tile] = 1001 };

        TestAssert.Check(!FarLodLifecycleRules.TryCompleteInFlight(
                        inFlight, tile, 1000L, true, true, false, out _),
                "an older completion cannot release or classify a newer in-flight request");
        TestAssert.Equal(1, inFlight.Count,
                "a mismatched completion leaves the active request registered");

        TestAssert.Check(FarLodLifecycleRules.TryCompleteInFlight(
                        inFlight, tile, 1001L, true, false, false,
                        out FarLodCompletionDisposition staleDisposition),
                "a matching stale build is completed and released");
        TestAssert.Equal(FarLodCompletionDisposition.RebuildLatest, staleDisposition,
                "a changed tile revision requests the latest build after release");
        TestAssert.Equal(0, inFlight.Count,
                "stale completion releases its own worker slot exactly once");

        inFlight[tile] = 1002;
        TestAssert.Check(FarLodLifecycleRules.TryCompleteInFlight(
                        inFlight, tile, 1002L, true, true, true,
                        out FarLodCompletionDisposition failureDisposition),
                "worker failure follows the same exact-request completion path");
        TestAssert.Equal(FarLodCompletionDisposition.Failed, failureDisposition,
                "a current worker exception reaches bounded retry handling");

        inFlight[tile] = 1003;
        TestAssert.Check(FarLodLifecycleRules.TryCompleteInFlight(
                        inFlight, tile, 1003L, false, false, false,
                        out FarLodCompletionDisposition evictedDisposition),
                "an evicted tile still completes and releases the in-flight entry");
        TestAssert.Equal(FarLodCompletionDisposition.Discard, evictedDisposition,
                "an evicted payload is discarded instead of being applied");

        inFlight[tile] = 1004;
        TestAssert.Check(FarLodLifecycleRules.TryCompleteInFlight(
                        inFlight, tile, 1004L, true, true, false,
                        out FarLodCompletionDisposition emptyPayloadDisposition),
                "a successful empty result follows normal completion bookkeeping");
        TestAssert.Equal(FarLodCompletionDisposition.Apply, emptyPayloadDisposition,
                "a null/empty mesh payload is still a successful current completion");
        TestAssert.Equal(0, inFlight.Count,
                "all terminal outcomes release exactly one matching request");
    }

    private static void TestEvictedCompletionsDispatchReplacementTiles()
    {
        const int buildLimit = 3;
        FarLodWorkQueue queue = new();
        Dictionary<Vector2I, byte> liveTiles = new();
        Dictionary<Vector2I, long> inFlight = new();
        long nextTaskId = 0;

        for (int i = 0; i < 4; i++)
        {
            Vector2I coordinate = new(i, 0);
            liveTiles.Add(coordinate, 0);
            queue.Enqueue(coordinate);
        }

        DispatchQueuedForTest(queue, liveTiles, inFlight, buildLimit, ref nextTaskId);
        TestAssert.Equal(buildLimit, inFlight.Count,
                "initial dispatch fills the bounded worker capacity");
        TestAssert.Equal(1, queue.Count,
                "work beyond the worker limit stays queued");

        KeyValuePair<Vector2I, long>[] oldBuilds = new KeyValuePair<Vector2I, long>[inFlight.Count];
        int index = 0;
        foreach (KeyValuePair<Vector2I, long> build in inFlight)
        {
            oldBuilds[index++] = build;
        }

        liveTiles.Clear();
        queue.Prune(liveTiles);
        for (int i = 10; i < 14; i++)
        {
            Vector2I coordinate = new(i, 0);
            liveTiles.Add(coordinate, 0);
            queue.Enqueue(coordinate);
        }

        foreach (KeyValuePair<Vector2I, long> completed in oldBuilds)
        {
            TestAssert.Check(FarLodLifecycleRules.TryCompleteInFlight(
                            inFlight, completed.Key, completed.Value,
                            liveTiles.ContainsKey(completed.Key), false, false,
                            out FarLodCompletionDisposition disposition),
                    "the evicted task releases only its own in-flight registration");
            TestAssert.Equal(FarLodCompletionDisposition.Discard, disposition,
                    "completion for an evicted tile cannot apply its payload");
            // Mirrors ApplyTile's unconditional finally -> Dispatch path.
            DispatchQueuedForTest(queue, liveTiles, inFlight, buildLimit, ref nextTaskId);
        }

        TestAssert.Equal(buildLimit, inFlight.Count,
                "replacement tiles are dispatched as each evicted task completes");
        TestAssert.Equal(1, queue.Count,
                "only the replacement tile beyond capacity remains queued");
        foreach (Vector2I coordinate in inFlight.Keys)
        {
            TestAssert.Check(coordinate.X >= 10,
                    "no evicted task is reused for replacement tile geometry");
        }
    }

    private static void DispatchQueuedForTest(
            FarLodWorkQueue queue,
            Dictionary<Vector2I, byte> liveTiles,
            Dictionary<Vector2I, long> inFlight,
            int buildLimit,
            ref long nextTaskId)
    {
        while (FarLodLifecycleRules.CanDispatch(inFlight.Count, queue.Count, buildLimit, true))
        {
            if (!FarLodLifecycleRules.TryDequeueEligibleTile(
                    queue, liveTiles, inFlight, out Vector2I coordinate, out byte _))
            {
                break;
            }

            inFlight.Add(coordinate, ++nextTaskId);
        }
    }

    private static void TestCellGeometryRespectsFullResolutionOwnership()
    {
        Rect2I retained = new(-2, -2, 5, 5);

        // A flat-height cell fixture models the full-resolution chunk square
        // and the Far-LOD top-cell emission gate. Exactly one representation
        // must own every cell across all four edges, including negative X/Z.
        for (int chunkZ = -3; chunkZ <= 3; chunkZ++)
        {
            for (int chunkX = -3; chunkX <= 3; chunkX++)
            {
                Vector2I chunk = new(chunkX, chunkZ);
                bool fullResolutionOwnsCell = retained.HasPoint(chunk);
                bool farLodOwnsCell = FarLodLifecycleRules.ShouldEmitCell(
                        retained, chunkX * VoxelWorld.ChunkX, chunkZ * VoxelWorld.ChunkZ,
                        false, true);
                TestAssert.Check(fullResolutionOwnsCell ^ farLodOwnsCell,
                        $"chunk cell ({chunkX},{chunkZ}) has exactly one visual owner");
            }
        }

        Rect2I afterUnload = new(-1, -1, 3, 3);
        Vector2I[] edges =
        [
            new(-2, 0), new(2, 0), new(0, -2), new(0, 2),
        ];
        foreach (Vector2I edge in edges)
        {
            TestAssert.Check(retained.HasPoint(edge),
                    $"edge chunk {edge} starts inside the retained full-resolution square");
            TestAssert.Check(!afterUnload.HasPoint(edge),
                    $"edge chunk {edge} is no longer retained after the view moves");
            TestAssert.Check(FarLodLifecycleRules.ShouldEmitCell(
                            afterUnload, edge.X * VoxelWorld.ChunkX,
                            edge.Y * VoxelWorld.ChunkZ, false, true),
                    $"Far LOD restores geometry for unloaded edge chunk {edge}");
        }

        TestAssert.Check(!FarLodLifecycleRules.ShouldEmitCell(
                        retained, -1, -1, false, true),
                "negative-coordinate cells inside retained chunks emit no duplicate LOD geometry");
        TestAssert.Check(FarLodLifecycleRules.ShouldEmitCell(
                        retained, -49, -49, false, true),
                "negative-coordinate cells beyond the retained square remain eligible");
        TestAssert.Check(!FarLodLifecycleRules.ShouldEmitCell(
                        retained, 48, 48, true, false),
                "sky cells without an island do not emit LOD geometry");
        TestAssert.Check(FarLodLifecycleRules.ShouldEmitCell(
                        retained, 48, 48, true, true),
                "sky cells with an island remain eligible for LOD geometry");
    }
}
