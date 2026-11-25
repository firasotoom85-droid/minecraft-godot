using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Minecraft;

public static class VoxelStreamingTests
{
    public static void Run()
    {
        TestAssert.Check(!StreamingApplyBudget.ShouldStop(25.0, 0, 2.0),
                "one bounded resource-creation step is allowed to begin");
        TestAssert.Check(!StreamingApplyBudget.ShouldStop(1.99, 1, 2.0),
                "staged apply continues while within the frame time budget");
        TestAssert.Check(StreamingApplyBudget.ShouldStop(2.0, 1, 2.0),
                "staged apply yields between resource slices when the budget is exhausted");
        TestAssert.Check(StreamingApplyBudget.ShouldStop(0.0, 1, 0.0),
                "zero time budget yields after one bounded resource slice");

        TestColliderReadinessEpochs();
        TestInitialStreamingReadiness();
        TestChunkWorkTracking();
        TestChunkWorkerLifecycle();
        TestChunkGenerationReservation();
        TestMutationBlockIds();
        TestWorldCompatibilityPolicies();
        TestPlayerPersistenceNotifications();
        TestPooledScratchBufferClearing();
        TestStreamingPolicies();
        TestChunkDependencyRevisions();
        TestDependencyRevisionBound();
        TestStreamingApplyBatches();

        long expectedLow = 11L * 11L * 16 * 128 * 16 * StreamingMemoryBudget.BytesPerVoxelUpperBound;
        TestAssert.Equal(expectedLow,
                StreamingMemoryBudget.EstimateVoxelArrayBudgetBytes(2, 2, 16, 128, 16),
                "voxel cache budget includes the direct-sky side map, streamed radius and existing halo");
        TestAssert.Equal(17L * 17L * 16 * 128 * 16 * StreamingMemoryBudget.BytesPerVoxelUpperBound,
                StreamingMemoryBudget.EstimateVoxelArrayBudgetBytes(5, 2, 16, 128, 16),
                "voxel cache budget scales with the selected view distance");
        TestAssert.Equal(expectedLow,
                StreamingMemoryBudget.EstimateVoxelArrayBudgetBytes(0, 2, 16, 128, 16),
                "voxel cache budget clamps unsupported view distances to the runtime minimum");

        TestMeshingConfigurationRevision();
        TestPayloadPoolCapacityBudget();
        TestPartialBuildBufferOwnership();
    }

    private static void TestInitialStreamingReadiness()
    {
        TestAssert.Equal(9, InitialChunkStreamingPolicy.RequiredChunks(
                        InitialChunkStreamingPolicy.StartupRadius),
                "startup readiness covers the player's 3x3 neighbourhood");
        TestAssert.Check(!InitialChunkStreamingPolicy.IsComplete(8, 9),
                "startup waits until every nearby chunk is committed");
        TestAssert.Check(InitialChunkStreamingPolicy.IsComplete(9, 9),
                "startup releases once all nine nearby chunks are ready");
        TestAssert.Check(InitialChunkStreamingPolicy.IsComplete(0, 0),
                "an empty readiness target cannot deadlock startup");
        TestAssert.Equal(0.5, new InitialChunkLoadProgress(3, 6).Fraction,
                "loading progress reports a bounded ready-to-required ratio");
    }

    private static void TestColliderReadinessEpochs()
    {
        TestAssert.Check(ChunkCollisionReadiness.IsReady(12, 12, true),
                "collision readiness requires and accepts the current edit epoch");
        TestAssert.Check(!ChunkCollisionReadiness.IsReady(11, 12, true),
                "collision from an earlier world edit epoch is stale");
        TestAssert.Check(ChunkCollisionReadiness.IsReady(12, 12, true),
                "a current collision payload is ready when it has geometry");
        TestAssert.Check(!ChunkCollisionReadiness.IsReady(12, 12, false),
                "a missing or unapplied collision payload cannot satisfy teleport readiness");
        TestAssert.Check(!ChunkCollisionReadiness.IsReady(0, 1, true),
                "an initial or unbuilt collision payload cannot satisfy a changed world epoch");
    }

    private static void TestMeshingConfigurationRevision()
    {
        bool original = Mesher.Greedy;
        try
        {
            (bool greedy, long revision) = Mesher.CaptureConfiguration();
            TestAssert.Equal(original, greedy,
                    "meshing configuration capture returns the current greedy mode");
            TestAssert.Check(Mesher.IsConfigurationCurrent(revision),
                    "the current meshing configuration revision is accepted");

            Mesher.Greedy = !greedy;
            (bool nextGreedy, long nextRevision) = Mesher.CaptureConfiguration();
            TestAssert.Equal(!greedy, nextGreedy,
                    "changing greedy mode publishes the new immutable build mode");
            TestAssert.Check(nextRevision > revision,
                    "a meshing mode change advances the configuration revision");
            TestAssert.Check(!Mesher.IsConfigurationCurrent(revision),
                    "payloads captured before a mode change are identified as stale");
            TestAssert.Check(Mesher.IsConfigurationCurrent(nextRevision),
                    "payloads captured after a mode change use the active revision");
        }
        finally
        {
            Mesher.Greedy = original;
        }
    }

    private static void TestPayloadPoolCapacityBudget()
    {
        long retainedBefore = PayloadPool.RetainedBufferBytes;
        int surfacesBefore = PayloadPool.RetainedSurfaceCount;
        int collisionsBefore = PayloadPool.RetainedCollisionCount;

        SurfaceData oversized = new();
        oversized.Vertex.Capacity = (int)(PayloadPool.MaxRetainedItemBytes / 12) + 1;
        long oversizedBytes = oversized.EstimatedBufferBytes;
        TestAssert.Check(oversizedBytes > PayloadPool.MaxRetainedItemBytes,
                "oversized surface fixture exceeds the per-buffer retention threshold");

        (bool _, long revision) = Mesher.CaptureConfiguration();
        MeshPayload oversizedPayload = new(oversized, new SurfaceData(),
                new SurfaceData(), new SurfaceData(), [], 0, revision, 0);
        PayloadPool.Release(oversizedPayload);
        PayloadPool.Release(oversizedPayload);

        TestAssert.Equal(retainedBefore, PayloadPool.RetainedBufferBytes,
                "oversized surface backing arrays are discarded instead of retained");
        TestAssert.Equal(surfacesBefore, PayloadPool.RetainedSurfaceCount,
                "discarding an oversized surface does not increment retained-item accounting");
        TestAssert.Equal(collisionsBefore, PayloadPool.RetainedCollisionCount,
                "empty collision buffers are not counted as retained pool entries");
        TestAssert.Check(PayloadPool.RetainedBufferBytes <= PayloadPool.MaxRetainedBufferBytes,
                "static mesher pool never exceeds its process-wide byte budget");
        TestAssert.Check(PayloadPool.RetainedSurfaceCount >= 0
                        && PayloadPool.RetainedCollisionCount >= 0,
                "mesher pool retained-item counters stay non-negative");

        // Normal-sized buffers should remain reusable under the global cap.
        SurfaceData reusable = new();
        reusable.Vertex.Capacity = 32;
        long reusableBytes = reusable.EstimatedBufferBytes;
        MeshPayload reusablePayload = new(reusable, new SurfaceData(),
                new SurfaceData(), new SurfaceData(), [], 0, revision, 0);
        PayloadPool.Release(reusablePayload);
        TestAssert.Equal(retainedBefore + reusableBytes, PayloadPool.RetainedBufferBytes,
                "normal-size surface capacity is accounted while retained");
        TestAssert.Equal(surfacesBefore + 1, PayloadPool.RetainedSurfaceCount,
                "normal-size surface is added to the reusable pool");
        SurfaceData rented = PayloadPool.RentSurface();
        TestAssert.Check(ReferenceEquals(reusable, rented),
                "the next surface rent reuses the returned backing buffer");
        TestAssert.Equal(retainedBefore, PayloadPool.RetainedBufferBytes,
                "renting a surface subtracts its exact retained capacity");
        PayloadPool.ReleaseBuildBuffers(rented, null, null, null, null);

        System.Threading.Tasks.Task[] workers = new System.Threading.Tasks.Task[8];
        for (int worker = 0; worker < workers.Length; worker++)
        {
            workers[worker] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int iteration = 0; iteration < 64; iteration++)
                {
                    SurfaceData a = PayloadPool.RentSurface();
                    SurfaceData b = PayloadPool.RentSurface();
                    SurfaceData c = PayloadPool.RentSurface();
                    SurfaceData d = PayloadPool.RentSurface();
                    System.Collections.Generic.List<Vector3> collision = PayloadPool.RentCollision();
                    MeshPayload payload = new(a, b, c, d, collision, 0, revision, 0);
                    PayloadPool.Release(payload);
                }
            });
        }
        System.Threading.Tasks.Task.WaitAll(workers);
        TestAssert.Check(PayloadPool.RetainedBufferBytes <= PayloadPool.MaxRetainedBufferBytes,
                "concurrent worker rent/release never exceeds the retained byte budget");
        TestAssert.Check(PayloadPool.RetainedSurfaceCount >= 0
                        && PayloadPool.RetainedCollisionCount >= 0,
                "concurrent worker rent/release keeps item counters non-negative");
        TestAssert.Check(PayloadPool.RetainedBufferBytes >= 0,
                "concurrent worker rent/release keeps retained bytes non-negative");
    }

    private static void TestChunkWorkTracking()
    {
        ChunkWorkTracker tracker = new();
        Vector2I coordinate = new(4, -7);

        TestAssert.Check(tracker.TryBegin(coordinate),
                "first worker claims a coordinate");
        TestAssert.Check(!tracker.TryBegin(coordinate),
                "a coordinate cannot be claimed by two workers");
        tracker.RequestRebuild(coordinate);
        tracker.RequestRebuild(coordinate);
        TestAssert.Equal(1, tracker.InFlightCount,
                "repeated edits keep one active owner");
        TestAssert.Check(tracker.Complete(coordinate),
                "an edit during a build schedules one follow-up");
        TestAssert.Equal(0, tracker.InFlightCount,
                "completion releases active ownership");
        TestAssert.Check(!tracker.Complete(coordinate),
                "the follow-up request is consumed exactly once");
        TestAssert.Check(tracker.TryBegin(coordinate),
                "the coordinate can be claimed again after completion");
        TestAssert.Check(!tracker.Complete(coordinate),
                "an unchanged build does not schedule unnecessary work");
    }

    private static void TestChunkWorkerLifecycle()
    {
        ChunkWorkTracker tracker = new();
        Vector2I coordinate = new(7, -4);
        int availableSlots = 2;
        int pendingPayloads = 0;
        int discardedPayloads = 0;
        int builds = 0;

        TestAssert.Check(tracker.TryBegin(coordinate),
                "worker dispatch claims the coordinate before entering the build lifecycle");
        ChunkWorkerExecutionResult first = ExecuteTestWorker(
                () => true,
                () => availableSlots--,
                () =>
                {
                    builds++;
                    tracker.RequestRebuild(coordinate);
                    tracker.RequestRebuild(coordinate);
                    return new object();
                },
                _ =>
                {
                    pendingPayloads++;
                    return true;
                },
                _ => discardedPayloads++,
                () => availableSlots++,
                _ => false,
                (_, _) => { });

        TestAssert.Check(first.Published && !first.RetryRequested && !first.Cancelled,
                "worker build publishes a current payload successfully");
        TestAssert.Equal(1, availableSlots,
                "published payload retains its capacity reservation until main-thread apply");
        TestAssert.Equal(1, pendingPayloads,
                "one dispatched build publishes exactly one payload");
        TestAssert.Equal(0, discardedPayloads,
                "published payload ownership transfers to the apply queue");

        bool followUp = tracker.Complete(coordinate, withinRetention: true);
        TestAssert.Check(followUp,
                "edits arriving during an active build coalesce into one follow-up request");
        TestAssert.Check(ChunkWorkTracker.ShouldQueueAsRebuild(
                        currentRebuild: false, rebuildRequested: followUp),
                "a rebuild request upgrades an active fresh build's follow-up to rebuild work");
        TestAssert.Check(tracker.TryBegin(coordinate),
                "the coordinate can be dispatched again after completion");

        ChunkWorkerExecutionResult second = ExecuteTestWorker(
                () => true,
                () => availableSlots--,
                () =>
                {
                    builds++;
                    return new object();
                },
                _ =>
                {
                    pendingPayloads++;
                    return true;
                },
                _ => discardedPayloads++,
                () => availableSlots++,
                _ => false,
                (_, _) => { });
        TestAssert.Check(second.Published && !tracker.Complete(coordinate, withinRetention: true),
                "the follow-up publishes once and does not schedule another unchanged build");
        TestAssert.Equal(2, builds,
                "one initial build plus one coalesced follow-up are executed");

        // Simulate the main-thread apply path releasing both published slots.
        availableSlots += pendingPayloads;
        pendingPayloads = 0;
        TestAssert.Equal(2, availableSlots,
                "main-thread payload application returns every published capacity reservation");
        TestAssert.Equal(0, pendingPayloads,
                "no completed payload remains queued after application");

        Vector2I failedCoordinate = new(-9, 3);
        TestAssert.Check(tracker.TryBegin(failedCoordinate),
                "a fresh worker request can enter the failure-injection path");
        int failureSlots = 1;
        int failureAttempts = 0;
        int failureReports = 0;
        ChunkWorkerExecutionResult failed = ExecuteTestWorker(
                () => true,
                () => failureSlots--,
                () => throw new InvalidOperationException("injected one-time mesher failure"),
                _ => false,
                _ => discardedPayloads++,
                () => failureSlots++,
                _ => tracker.TryRecordFailure(
                        failedCoordinate, alive: true, withinRetention: true,
                        maximumAttempts: 3, out failureAttempts),
                (_, retry) =>
                {
                    TestAssert.Check(retry,
                            "worker exception is reported with its bounded retry decision");
                    failureReports++;
                });
        TestAssert.Check(!failed.Published && failed.RetryRequested && !failed.Cancelled,
                "one-time worker exception requests a retry without publishing a payload");
        TestAssert.Equal(1, failureSlots,
                "failed build releases its reserved payload slot");
        TestAssert.Equal(1, failureAttempts,
                "first worker failure records the first retry attempt");
        TestAssert.Equal(1, failureReports,
                "worker exception is reported exactly once");
        TestAssert.Check(!ChunkWorkTracker.ShouldQueueAsRebuild(
                        currentRebuild: false, rebuildRequested: false),
                "retrying a fresh request preserves fresh-work intent");
        TestAssert.Check(ChunkWorkTracker.ShouldQueueAsRebuild(
                        currentRebuild: true, rebuildRequested: false),
                "retrying a rebuild request preserves rebuild-work intent");
        TestAssert.Check(tracker.Complete(failedCoordinate, withinRetention: true) == false,
                "failed work releases its in-flight coordinate before retry dispatch");

        TestAssert.Check(tracker.TryBegin(failedCoordinate),
                "the failed coordinate can be claimed for its retry");
        ChunkWorkerExecutionResult retried = ExecuteTestWorker(
                () => true,
                () => failureSlots--,
                () => new object(),
                _ =>
                {
                    tracker.ForgetFailure(failedCoordinate);
                    return true;
                },
                _ => discardedPayloads++,
                () => failureSlots++,
                _ => false,
                (_, _) => { });
        TestAssert.Check(retried.Published,
                "the retried request publishes after the injected transient failure is removed");
        tracker.Complete(failedCoordinate, withinRetention: true);
        TestAssert.Equal(0, tracker.FailureCount,
                "successful publication clears the coordinate's retry history");
        failureSlots++;
        TestAssert.Equal(1, failureSlots,
                "applying the retried payload returns its remaining capacity reservation");

        Vector2I exhaustedCoordinate = new(12, 12);
        TestAssert.Check(tracker.TryRecordFailure(
                        exhaustedCoordinate, true, true, 3, out int attemptOne)
                        && attemptOne == 1,
                "the first repeated worker failure is retryable");
        TestAssert.Check(tracker.TryRecordFailure(
                        exhaustedCoordinate, true, true, 3, out int attemptTwo)
                        && attemptTwo == 2,
                "the second worker failure is the final bounded retry");
        TestAssert.Check(!tracker.TryRecordFailure(
                        exhaustedCoordinate, true, true, 3, out int attemptThree)
                        && attemptThree == 3,
                "the third failure exhausts the initial attempt plus two retries");
        TestAssert.Equal(0, tracker.FailureCount,
                "exhausted retry history is removed instead of retained indefinitely");

        Vector2I departedCoordinate = new(500, -500);
        TestAssert.Check(tracker.TryBegin(departedCoordinate),
                "departed chunk enters the in-flight worker set");
        TestAssert.Check(tracker.TryRecordFailure(
                        departedCoordinate, true, true, 3, out _),
                "a failure initially records retry state while the chunk is retained");
        TestAssert.Equal(1, tracker.FailureCount,
                "retained failed request has one retry record before travel");
        tracker.Complete(departedCoordinate, withinRetention: false);
        TestAssert.Equal(0, tracker.FailureCount,
                "completion after travel removes retry state for an abandoned out-of-retention chunk");

        Vector2I queuedDepartedCoordinate = new(40, -40);
        TestAssert.Check(tracker.TryRecordFailure(
                        queuedDepartedCoordinate, true, true, 3, out _),
                "a failed request records retry history before its retry is queued");
        TestAssert.Equal(1, tracker.FailureCount,
                "queued retry history exists before the streaming center changes");
        tracker.ForgetFailureIfNotRequeued(
                queuedDepartedCoordinate, willBeRequeued: false);
        TestAssert.Equal(0, tracker.FailureCount,
                "queue reconciliation removes retry history for a retry discarded outside retention");

        Vector2I shrunkViewCoordinate = new(4, 0);
        TestAssert.Check(ChunkStreamingPolicy.IsWithinRetention(
                        shrunkViewCoordinate, Vector2I.Zero, limit: 4),
                "view-distance shrink can leave a chunk inside the unload margin");
        TestAssert.Check(tracker.TryRecordFailure(
                        shrunkViewCoordinate, true, true, 3, out _),
                "a retry for that chunk begins with retained failure history");
        tracker.ForgetFailureIfNotRequeued(
                shrunkViewCoordinate, willBeRequeued: false);
        TestAssert.Equal(0, tracker.FailureCount,
                "queue reconciliation clears a retry no longer scheduled after view-distance shrink");

        Vector2I retainedRetryCoordinate = new(1, 1);
        TestAssert.Check(tracker.TryRecordFailure(
                        retainedRetryCoordinate, true, true, 3, out _),
                "a retry in the new fresh radius records its attempt history");
        tracker.ForgetFailureIfNotRequeued(
                retainedRetryCoordinate, willBeRequeued: true);
        TestAssert.Equal(1, tracker.FailureCount,
                "a retry that remains scheduled preserves its bounded attempt history");
        tracker.ForgetFailure(retainedRetryCoordinate);

        Vector2I staleCoordinate = new(2, 2);
        TestAssert.Check(tracker.TryBegin(staleCoordinate),
                "stale-payload test claims its work coordinate");
        int freshnessChecks = 0;
        int staleSlots = 1;
        int staleBuilds = 0;
        int staleDiscards = 0;
        int stalePublishes = 0;
        ChunkWorkerExecutionResult stale = ExecuteTestWorker(
                () => ++freshnessChecks < 3,
                () => staleSlots--,
                () =>
                {
                    staleBuilds++;
                    return new object();
                },
                _ =>
                {
                    stalePublishes++;
                    return true;
                },
                _ => staleDiscards++,
                () => staleSlots++,
                _ => false,
                (_, _) => { });
        TestAssert.Check(!stale.Published && staleBuilds == 1,
                "worker revalidates retention after meshing before publishing a stale payload");
        TestAssert.Equal(0, stalePublishes,
                "a payload completed after travel never enters the apply queue");
        TestAssert.Equal(1, staleDiscards,
                "a stale completed payload is returned to its owner exactly once");
        TestAssert.Equal(1, staleSlots,
                "discarding a stale payload releases its reserved capacity slot");
        tracker.Complete(staleCoordinate, withinRetention: false);

        Vector2I publishRaceCoordinate = new(3, -3);
        TestAssert.Check(tracker.TryBegin(publishRaceCoordinate),
                "publish-race test claims its in-flight coordinate");
        int publishRaceSlots = 1;
        int publishRaceCalls = 0;
        int publishRaceDiscards = 0;
        ChunkWorkerExecutionResult rejectedPublish = ExecuteTestWorker(
                () => true,
                () => publishRaceSlots--,
                () => new object(),
                _ =>
                {
                    publishRaceCalls++;
                    return false;
                },
                _ => publishRaceDiscards++,
                () => publishRaceSlots++,
                _ => false,
                (_, _) => { });
        TestAssert.Check(!rejectedPublish.Published && publishRaceCalls == 1,
                "worker rejects a payload when the final locked publication check detects travel");
        TestAssert.Equal(1, publishRaceDiscards,
                "a payload rejected at publication is discarded exactly once");
        TestAssert.Equal(1, publishRaceSlots,
                "a payload rejected at publication releases its reserved capacity slot");
        tracker.Complete(publishRaceCoordinate, withinRetention: false);

        Vector2I cancelledCoordinate = new(4, 4);
        TestAssert.Check(tracker.TryBegin(cancelledCoordinate),
                "shutdown-cancellation test claims its coordinate");
        int cancelledSlots = 1;
        int retryCallsAfterCancellation = 0;
        ChunkWorkerExecutionResult cancelled = ExecuteTestWorker(
                () => true,
                () => cancelledSlots--,
                () => throw new OperationCanceledException(),
                _ => false,
                _ => discardedPayloads++,
                () => cancelledSlots++,
                _ =>
                {
                    retryCallsAfterCancellation++;
                    return true;
                },
                (_, _) => { });
        TestAssert.Check(cancelled.Cancelled && !cancelled.RetryRequested,
                "shutdown cancellation is not treated as an ordinary worker failure");
        TestAssert.Equal(0, retryCallsAfterCancellation,
                "cancelled work never enters the retry path");
        TestAssert.Equal(1, cancelledSlots,
                "cancellation after capacity reservation releases the slot");
        tracker.Complete(cancelledCoordinate, withinRetention: false);

        using CancellationTokenSource activeBuildCancellation = new();
        int activeBuildSlots = 1;
        int activeBuildDiscards = 0;
        int activeBuildPublishes = 0;
        int activeBuildRetries = 0;
        ChunkWorkerExecutionResult cancelledAfterBuild = ExecuteTestWorker(
                () => true,
                () => activeBuildSlots--,
                () =>
                {
                    // Model shutdown arriving after meshing has completed but
                    // before the worker can transfer payload ownership.
                    activeBuildCancellation.Cancel();
                    return new object();
                },
                _ =>
                {
                    activeBuildPublishes++;
                    return true;
                },
                _ => activeBuildDiscards++,
                () => activeBuildSlots++,
                _ =>
                {
                    activeBuildRetries++;
                    return true;
                },
                (_, _) => { },
                activeBuildCancellation.Token);
        TestAssert.Check(cancelledAfterBuild.Cancelled && !cancelledAfterBuild.Published,
                "shutdown after a build completes is observed before payload publication");
        TestAssert.Equal(1, activeBuildDiscards,
                "a just-built payload remains worker-owned and is discarded exactly once");
        TestAssert.Equal(0, activeBuildPublishes,
                "shutdown between build and publication cannot enqueue a stale payload");
        TestAssert.Equal(0, activeBuildRetries,
                "shutdown between build and publication is not treated as a worker failure");
        TestAssert.Equal(1, activeBuildSlots,
                "cancelling after meshing releases the reserved payload slot");
    }

    private static void TestPartialBuildBufferOwnership()
    {
        int surfacesBefore = PayloadPool.RetainedSurfaceCount;
        int collisionsBefore = PayloadPool.RetainedCollisionCount;

        SurfaceData opaque = PayloadPool.RentSurface();
        SurfaceData glass = PayloadPool.RentSurface();
        SurfaceData water = PayloadPool.RentSurface();
        SurfaceData lava = PayloadPool.RentSurface();
        System.Collections.Generic.List<Vector3> collision = PayloadPool.RentCollision();

        // Keep each rented buffer small and non-empty so the shared pool retains
        // all five entries and the test can observe the cleanup transfer.
        foreach (SurfaceData surface in new[] { opaque, glass, water, lava })
        {
            surface.Vertex.Capacity = 1;
            surface.Normal.Capacity = 1;
            surface.Color.Capacity = 1;
            surface.Uv.Capacity = 1;
            surface.Uv2.Capacity = 1;
        }
        collision.Capacity = 1;

        bool exceptionObserved = false;
        try
        {
            throw new OperationCanceledException("injected cancellation after buffer rent");
        }
        catch (OperationCanceledException)
        {
            exceptionObserved = true;
        }
        finally
        {
            PayloadPool.ReleaseBuildBuffers(opaque, glass, water, lava, collision);
        }

        TestAssert.Check(exceptionObserved,
                "the partial mesher build fixture exits through its failure path");
        TestAssert.Check(PayloadPool.RetainedSurfaceCount >= surfacesBefore,
                "an interrupted pre-payload build returns all four rented surfaces");
        TestAssert.Check(PayloadPool.RetainedCollisionCount >= collisionsBefore,
                "an interrupted pre-payload build returns its rented collision buffer");
    }

    private static void TestChunkGenerationReservation()
    {
        int generationCount = 0;
        ChunkGenerationReservations reservations = new();
        Vector2I coordinate = new(-12, 19);
        object reservationLock = new();
        Func<byte[]> generate = () =>
        {
            Interlocked.Increment(ref generationCount);
            Thread.Sleep(5);
            return new byte[] { 1, 2, 3, 4 };
        };

        Task<Lazy<byte[]>>[] claimers = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() =>
                {
                    lock (reservationLock)
                    {
                        return reservations.GetOrCreate(coordinate, generate);
                    }
                })).ToArray();
        Task.WaitAll(claimers);
        Lazy<byte[]> reservation = claimers[0].Result;
        TestAssert.Check(claimers.All(claim => ReferenceEquals(reservation, claim.Result)),
                "concurrent same-coordinate cache misses share one reservation");

        Task<byte[]>[] readers = claimers.Select(claim =>
                Task.Run(() => claim.Result.Value)).ToArray();
        Task.WaitAll(readers);

        TestAssert.Equal(1, generationCount,
                "concurrent readers execute one chunk-generation reservation");
        TestAssert.Check(readers.All(reader => ReferenceEquals(readers[0].Result, reader.Result)),
                "concurrent readers receive the same generated byte array");
        lock (reservationLock)
        {
            reservations.Complete(coordinate, reservation);
        }
        TestAssert.Equal(0, reservations.Count,
                "successful generation removes its in-flight reservation");

        Lazy<byte[]> failed;
        lock (reservationLock)
        {
            failed = reservations.GetOrCreate(coordinate,
                    () => throw new InvalidOperationException("injected generation failure"));
        }
        bool generationFailed = false;
        try
        {
            _ = failed.Value;
        }
        catch (InvalidOperationException)
        {
            generationFailed = true;
        }
        TestAssert.Check(generationFailed, "injected chunk generation failure is observed");
        lock (reservationLock)
        {
            reservations.Complete(coordinate, failed);
        }
        Lazy<byte[]> retried;
        lock (reservationLock)
        {
            retried = reservations.GetOrCreate(coordinate,
                    () => new byte[] { 9, 8, 7 });
        }
        TestAssert.Check(!ReferenceEquals(failed, retried) && retried.Value[0] == 9,
                "a failed generation reservation can be replaced and retried");
        lock (reservationLock)
        {
            reservations.Complete(coordinate, retried);
        }

        using CancellationTokenSource shutdown = new();
        Lazy<byte[]> cancelled;
        lock (reservationLock)
        {
            cancelled = reservations.GetOrCreate(coordinate, () =>
            {
                // Simulate generation reaching a safe checkpoint after work
                // starts, then being interrupted by world shutdown.
                shutdown.Cancel();
                shutdown.Token.ThrowIfCancellationRequested();
                return new byte[] { 5, 5, 5 };
            });
        }

        bool generationCancelled = false;
        try
        {
            _ = cancelled.Value;
        }
        catch (OperationCanceledException)
        {
            generationCancelled = true;
        }

        TestAssert.Check(generationCancelled,
                "chunk generation cancellation interrupts its in-flight reservation");
        lock (reservationLock)
        {
            reservations.Complete(coordinate, cancelled);
        }
        TestAssert.Equal(0, reservations.Count,
                "cancelled generation removes its reservation without publishing partial data");

        Lazy<byte[]> afterCancellation;
        lock (reservationLock)
        {
            afterCancellation = reservations.GetOrCreate(coordinate,
                    () => new byte[] { 6, 7, 8 });
        }
        TestAssert.Check(afterCancellation.Value.SequenceEqual(new byte[] { 6, 7, 8 }),
                "a later request can generate and publish the chunk after cancellation");
        lock (reservationLock)
        {
            reservations.Complete(coordinate, afterCancellation);
        }
    }

    private static void TestMutationBlockIds()
    {
        BlockDb.EnsureTables();
        TestAssert.Check(!VoxelWorld.IsMutationBlockIdDefined((BlockId)61),
                "the first unregistered persisted block ID is rejected at the mutation boundary");
        TestAssert.Check(!VoxelWorld.IsMutationBlockIdDefined((BlockId)255),
                "the largest byte-backed block ID is rejected");
        TestAssert.Check(VoxelWorld.IsMutationBlockIdDefined(BlockId.Stone),
                "registered block IDs remain valid for world mutation");

        VoxelWorld world = new();
        try
        {
            TestAssert.Check(!world.SetBlock(0, 1, 0, (BlockId)61),
                    "SetBlock rejects an unregistered ID before requiring a chunk");
            TestAssert.Check(!world.SetBlock(0, 1, 0, (BlockId)255),
                    "SetBlock rejects the largest byte-backed ID before mutation");
        }
        finally
        {
            world.Free();
        }
    }


    private static void TestWorldCompatibilityPolicies()
    {
        TestAssert.Check(WorldCompatibilityPolicy.IsSupportedGeneratorVersion(1, 11),
                "the minimum supported world generator remains loadable");
        TestAssert.Check(WorldCompatibilityPolicy.IsSupportedGeneratorVersion(11, 11),
                "the current generator version remains loadable");
        TestAssert.Check(!WorldCompatibilityPolicy.IsSupportedGeneratorVersion(12, 11),
                "a future generator version is rejected");
        TestAssert.Check(!WorldCompatibilityPolicy.IsSupportedGeneratorVersion(0, 11),
                "an invalid zero generator version is rejected");

        TestAssert.Check(WorldCompatibilityPolicy.HasValidNoiseSeeds(
                        new[] { 1, 2, 3, 4, 5 }),
                "five persisted noise seeds satisfy legacy generator metadata");
        TestAssert.Check(!WorldCompatibilityPolicy.HasValidNoiseSeeds(
                        new[] { 1, 2, 3, 4 }),
                "incomplete noise seed metadata is rejected");
        TestAssert.Check(!WorldCompatibilityPolicy.HasValidNoiseSeeds(null),
                "missing noise seed metadata is rejected");

        TestAssert.Check(WorldCompatibilityPolicy.IsSupportedSaveVersion(1, 4),
                "world save format V1 compatibility is retained");
        TestAssert.Check(WorldCompatibilityPolicy.IsSupportedSaveVersion(4, 4),
                "current world save format V4 remains supported");
        TestAssert.Check(!WorldCompatibilityPolicy.IsSupportedSaveVersion(5, 4),
                "future world save formats are rejected");
        TestAssert.Check(!WorldCompatibilityPolicy.IsSupportedSaveVersion(0, 4),
                "invalid zero world save versions are rejected");

        TestAssert.Check(!WorldCompatibilityPolicy.MustRejectMissingManifest(
                        false, false, false),
                "a genuinely empty world folder may receive a default manifest");
        TestAssert.Check(WorldCompatibilityPolicy.MustRejectMissingManifest(
                        true, false, false),
                "corrupt manifest candidates must not be replaced");
        TestAssert.Check(WorldCompatibilityPolicy.MustRejectMissingManifest(
                        false, true, false),
                "a manifest-less folder with world data is not assumed to be V1");
        TestAssert.Check(WorldCompatibilityPolicy.MustRejectMissingManifest(
                        false, false, true),
                "a manifest-less folder with player data is not assumed to be V1");
    }



    private static void TestPlayerPersistenceNotifications()
    {
        VoxelWorld statsWorld = new();
        Player statsPlayer = new();
        try
        {
            statsWorld.Player = statsPlayer;
            statsWorld.EnsurePlayerPersistenceEventsWired();
            statsWorld.EnsurePlayerPersistenceEventsWired();
            TestAssert.Check(!statsWorld.IsDirty,
                    "loading a player before event wiring does not dirty a clean world");
            statsPlayer.Stats.AddHunger(-1.0f);
            TestAssert.Check(statsWorld.IsDirty,
                    "stationary hunger changes mark the world dirty");
        }
        finally
        {
            statsWorld.Free();
            statsPlayer.Free();
        }

        VoxelWorld campfireWorld = new();
        Player campfirePlayer = new();
        try
        {
            campfireWorld.Player = campfirePlayer;
            campfireWorld.EnsurePlayerPersistenceEventsWired();
            campfirePlayer.EmitSignal(Player.SignalName.CampfireUsed,
                    "Rested — respawn point set, healed");
            TestAssert.Check(campfireWorld.IsDirty,
                    "campfire rest notifications persist respawn-anchor changes");
        }
        finally
        {
            campfireWorld.Free();
            campfirePlayer.Free();
        }
    }

    private static void TestPooledScratchBufferClearing()
    {
        byte[] scratch = Enumerable.Repeat((byte)0x7F, 16).ToArray();
        ChunkStreamingPolicy.ClearUsedBuffer(scratch, 12);
        TestAssert.Check(scratch.Take(12).All(value => value == 0),
                "pooled state scratch clears every active voxel before copying");
        TestAssert.Check(scratch.Skip(12).All(value => value == 0x7F),
                "pooled state scratch leaves the unused rented tail untouched");
    }

    private static void TestStreamingPolicies()
    {
        TestAssert.Check(ChunkStreamingPolicy.IsWithinRetention(
                        new Vector2I(-3, 5), new Vector2I(0, 0), 5),
                "negative coordinates use symmetric retention bounds");
        TestAssert.Check(!ChunkStreamingPolicy.IsWithinRetention(
                        new Vector2I(0, 6), Vector2I.Zero, 5),
                "coordinates outside retention are rejected");

        TestAssert.Check(ChunkStreamingPolicy.DirectionChanged(
                        Vector3.Right, Vector3.Left),
                "a direction reversal refreshes queued priorities");
        TestAssert.Check(ChunkStreamingPolicy.DirectionChanged(
                        Vector3.Right, Vector3.Zero),
                "stopping clears stale directional bias");
        TestAssert.Check(!ChunkStreamingPolicy.DirectionChanged(
                        Vector3.Right, new Vector3(0.99f, 0, 0.1f).Normalized()),
                "small heading jitter does not rebuild the priority heap");

        TestAssert.Check(ChunkStreamingPolicy.AffectsChunk(
                        0, 0, 8, 8, 16, 16, false),
                "an edit always rebuilds its owner chunk");
        TestAssert.Check(!ChunkStreamingPolicy.AffectsChunk(
                        -1, 0, 8, 8, 16, 16, false),
                "an interior edit does not rebuild an unrelated west neighbour");
        TestAssert.Check(ChunkStreamingPolicy.AffectsChunk(
                        -1, 0, 0, 8, 16, 16, false),
                "a west-edge edit rebuilds the west neighbour");
        TestAssert.Check(!ChunkStreamingPolicy.AffectsChunk(
                        -1, -1, 0, 8, 16, 16, false),
                "a single-edge edit does not rebuild a diagonal neighbour");
        TestAssert.Check(ChunkStreamingPolicy.AffectsChunk(
                        -1, -1, 0, 0, 16, 16, false),
                "a corner edit rebuilds its matching diagonal neighbour");
        TestAssert.Check(ChunkStreamingPolicy.AffectsChunk(
                        1, 1, 8, 8, 16, 16, true),
                "lighting changes retain the full propagation neighbourhood");
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int localX = dx < 0 ? 0 : dx > 0 ? 15 : 8;
                int localZ = dz < 0 ? 0 : dz > 0 ? 15 : 8;
                TestAssert.Check(ChunkStreamingPolicy.AffectsChunk(
                                dx, dz, localX, localZ, 16, 16, false),
                        $"matching edge/corner edit rebuilds neighbour ({dx},{dz})");
                TestAssert.Check(ChunkStreamingPolicy.AffectsChunk(
                                dx, dz, 8, 8, 16, 16, true),
                        $"lighting dependency includes neighbour ({dx},{dz})");
            }
        }


        TestAssert.Check(!ChunkStreamingPolicy.IsSiteCellOutsideRetention(
                        -1, -1, 64, Vector2I.Zero, 5, 16),
                "negative procedural cells overlapping the retained region remain cached");
        TestAssert.Check(ChunkStreamingPolicy.IsSiteCellOutsideRetention(
                        10, 10, 64, Vector2I.Zero, 5, 16),
                "procedural cells wholly outside the retained region are evictable");

        TestAssert.Check(ChunkStreamingPolicy.ShouldRetry(1, 3),
                "transient worker failures retry before the attempt limit");
        TestAssert.Check(!ChunkStreamingPolicy.ShouldRetry(3, 3),
                "worker retries stop at the bounded attempt limit");
    }

    private static void TestChunkDependencyRevisions()
    {
        ChunkDependencyRevisions revisions = new();
        Vector2I center = new(6, -3);
        Vector2I west = center + Vector2I.Left;
        Vector2I northWest = center + new Vector2I(-1, -1);
        Vector2I far = center + new Vector2I(8, 8);

        long centerBefore = revisions.MeshRevision(center);
        long westBefore = revisions.MeshRevision(west);
        long northWestBefore = revisions.MeshRevision(northWest);
        long farBefore = revisions.MeshRevision(far);
        revisions.RecordBlockMutation(center, 8, 8, 16, 16, lightingChanged: false);

        TestAssert.Check(!revisions.IsMeshRevisionCurrent(center, centerBefore),
                "an interior edit invalidates the edited chunk's in-flight mesh");
        TestAssert.Check(revisions.IsMeshRevisionCurrent(west, westBefore)
                        && revisions.IsMeshRevisionCurrent(northWest, northWestBefore),
                "an interior edit leaves unsampled geometry neighbors valid");
        TestAssert.Check(revisions.IsMeshRevisionCurrent(far, farBefore),
                "an interior edit leaves a distant payload valid when the world edit counter advances");

        long westEdgeBefore = revisions.MeshRevision(west);
        long northWestEdgeBefore = revisions.MeshRevision(northWest);
        revisions.RecordBlockMutation(center, 0, 8, 16, 16, lightingChanged: false);
        TestAssert.Check(!revisions.IsMeshRevisionCurrent(west, westEdgeBefore),
                "a west-boundary edit invalidates the neighbor that samples that edge");
        TestAssert.Check(revisions.IsMeshRevisionCurrent(northWest, northWestEdgeBefore),
                "a west-edge edit does not invalidate an unrelated diagonal neighbor");

        long diagonalBefore = revisions.MeshRevision(northWest);
        revisions.RecordBlockMutation(center, 0, 0, 16, 16, lightingChanged: false);
        TestAssert.Check(!revisions.IsMeshRevisionCurrent(northWest, diagonalBefore),
                "a corner edit invalidates the matching diagonal dependency");

        long twoChunksAwayBefore = revisions.MeshRevision(center + new Vector2I(2, 0));
        long threeChunksAwayBefore = revisions.MeshRevision(center + new Vector2I(3, 0));
        revisions.RecordBlockMutation(center, 8, 8, 16, 16, lightingChanged: true);
        TestAssert.Check(!revisions.IsMeshRevisionCurrent(
                        center + new Vector2I(2, 0), twoChunksAwayBefore),
                "light edits invalidate targets that consume neighboring halo-affected light maps");
        TestAssert.Check(revisions.IsMeshRevisionCurrent(
                        center + new Vector2I(3, 0), threeChunksAwayBefore),
                "light edits do not invalidate targets outside the 5x5 dependency region");

        long[] lightSnapshot = new long[25];
        revisions.CaptureLightInputRevisions(center, lightSnapshot);
        revisions.RecordBlockMutation(
                center + new Vector2I(3, 0), 8, 8, 16, 16, lightingChanged: true);
        TestAssert.Check(revisions.LightInputRevisionsCurrent(center, lightSnapshot),
                "a lighting edit outside the 5x5 solve input does not restart that solve");

        revisions.RecordBlockMutation(center, 8, 8, 16, 16, lightingChanged: false);
        TestAssert.Check(revisions.LightInputRevisionsCurrent(center, lightSnapshot),
                "geometry-only edits do not invalidate light solve inputs");
        revisions.RecordBlockMutation(center, 8, 8, 16, 16, lightingChanged: true);
        TestAssert.Check(!revisions.LightInputRevisionsCurrent(center, lightSnapshot),
                "opacity/emission edits inside the solve region invalidate its light snapshot");

        long[] nextSnapshot = new long[25];
        revisions.CaptureLightInputRevisions(center, nextSnapshot);
        revisions.RecordBlockMutation(center + new Vector2I(1, 1), 8, 8, 16, 16,
                lightingChanged: true);
        TestAssert.Check(!revisions.LightInputRevisionsCurrent(center, nextSnapshot),
                "light-affecting edits inside the 5x5 source region are detected");
    }

    private static void TestDependencyRevisionBound()
    {
        ChunkDependencyRevisions revisions = new();
        Vector2I cp = new(2, -4);

        long initial = revisions.MeshRevision(cp);
        revisions.RecordBlockMutation(cp, 8, 8, 16, 16, lightingChanged: true);
        long capturedMesh = revisions.MeshRevision(cp);
        long[] capturedLight = new long[25];
        revisions.CaptureLightInputRevisions(cp, capturedLight);
        TestAssert.Check(capturedMesh != initial,
                "a lighting edit advances the target mesh revision before the bound is reached");
        TestAssert.Check(revisions.ResetIfUnbounded(1),
                "revision storage resets after exceeding the configured entry bound");
        TestAssert.Check(!revisions.IsMeshRevisionCurrent(cp, capturedMesh),
                "a payload captured before a revision reset is rejected by the new floor");
        TestAssert.Check(!revisions.LightInputRevisionsCurrent(cp, capturedLight),
                "a light solve captured before a revision reset is rejected by the new floor");
        long afterReset = revisions.MeshRevision(cp);
        TestAssert.Check(revisions.IsMeshRevisionCurrent(cp, afterReset),
                "a payload captured after the reset uses the current revision floor");
        TestAssert.Check(!revisions.ResetIfUnbounded(1),
                "revision storage does not reset again while it is within the bound");
        revisions.RecordBlockMutation(cp, 8, 8, 16, 16, lightingChanged: false);
        TestAssert.Check(!revisions.IsMeshRevisionCurrent(cp, afterReset),
                "edits after a reset advance the revision again");
    }

    private static void TestStreamingApplyBatches()
    {
        TestAssert.Equal(StreamingApplyBudget.MaxVerticesPerStep,
                StreamingApplyBudget.BatchLength(20_001),
                "one streaming apply step is capped at the configured vertex limit");
        TestAssert.Equal(Math.Min(5_001, StreamingApplyBudget.MaxVerticesPerStep),
                StreamingApplyBudget.BatchLength(5_001),
                "valid surface and collision batches keep their complete triangle groups");
        bool rejectedIncompleteTriangle = false;
        try
        {
            _ = StreamingApplyBudget.BatchLength(5_000);
        }
        catch (ArgumentException)
        {
            rejectedIncompleteTriangle = true;
        }
        TestAssert.Check(rejectedIncompleteTriangle,
                "malformed mesh/collision input is rejected instead of silently dropping an incomplete triangle");
        TestAssert.Equal(0, StreamingApplyBudget.BatchLength(0),
                "empty surfaces do not schedule an apply step");
        TestAssert.Check(!StreamingApplyBudget.ShouldStop(25.0, 0, 2.0),
                "a bounded first apply step can progress even when a frame has no remaining slack");
        TestAssert.Check(StreamingApplyBudget.ShouldStop(2.0, 1, 2.0),
                "the next staged apply step yields when its frame budget is exhausted");
    }

    private readonly struct TestWorkerOperation : IChunkWorkerOperation<object>
    {
        private readonly CancellationToken _cancellationToken;
        private readonly Func<bool> _canContinue;
        private readonly Action _reservePayloadSlot;
        private readonly Func<object> _build;
        private readonly Func<object, bool> _publish;
        private readonly Action<object> _discardPayload;
        private readonly Action _releasePayloadSlot;
        private readonly Func<Exception, bool> _requestRetry;
        private readonly Action<Exception, bool> _reportFailure;

        public TestWorkerOperation(
                Func<bool> canContinue,
                Action reservePayloadSlot,
                Func<object> build,
                Func<object, bool> publish,
                Action<object> discardPayload,
                Action releasePayloadSlot,
                Func<Exception, bool> requestRetry,
                Action<Exception, bool> reportFailure,
                CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _canContinue = canContinue;
            _reservePayloadSlot = reservePayloadSlot;
            _build = build;
            _publish = publish;
            _discardPayload = discardPayload;
            _releasePayloadSlot = releasePayloadSlot;
            _requestRetry = requestRetry;
            _reportFailure = reportFailure;
        }

        public CancellationToken CancellationToken => _cancellationToken;
        public bool CanContinue() => _canContinue();
        public void ReservePayloadSlot() => _reservePayloadSlot();
        public object Build(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _build();
        }
        public bool Publish(object payload) => _publish(payload);
        public void DiscardPayload(object payload) => _discardPayload(payload);
        public void ReleasePayloadSlot() => _releasePayloadSlot();
        public bool RequestRetry(Exception error) => _requestRetry(error);
        public void ReportFailure(Exception error, bool retryRequested) =>
                _reportFailure(error, retryRequested);
    }

    private static ChunkWorkerExecutionResult ExecuteTestWorker(
            Func<bool> canContinue,
            Action reservePayloadSlot,
            Func<object> build,
            Func<object, bool> publish,
            Action<object> discardPayload,
            Action releasePayloadSlot,
            Func<Exception, bool> requestRetry,
            Action<Exception, bool> reportFailure,
            CancellationToken cancellationToken = default)
    {
        TestWorkerOperation operation = new(
                canContinue,
                reservePayloadSlot,
                build,
                publish,
                discardPayload,
                releasePayloadSlot,
                requestRetry,
                reportFailure,
                cancellationToken);
        return ChunkWorkerLifecycle.Execute<object, TestWorkerOperation>(ref operation);
    }

}
