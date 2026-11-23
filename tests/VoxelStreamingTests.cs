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
                "time budget still permits one payload to make progress");
        TestAssert.Check(!StreamingApplyBudget.ShouldStop(1.99, 1, 2.0),
                "payload application continues while within its frame time budget");
        TestAssert.Check(StreamingApplyBudget.ShouldStop(2.0, 1, 2.0),
                "payload application stops once its frame time budget is exhausted");
        TestAssert.Check(StreamingApplyBudget.ShouldStop(0.0, 1, 0.0),
                "zero time budget stops after the first payload");

        TestChunkWorkTracking();
        TestChunkWorkerLifecycle();
        TestChunkGenerationReservation();
        TestMutationBlockIds();
        TestWorldCompatibilityPolicies();
        TestPlayerPersistenceNotifications();
        TestPooledScratchBufferClearing();
        TestStreamingPolicies();

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

    private readonly struct TestWorkerOperation : IChunkWorkerOperation<object>
    {
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
                Action<Exception, bool> reportFailure)
        {
            _canContinue = canContinue;
            _reservePayloadSlot = reservePayloadSlot;
            _build = build;
            _publish = publish;
            _discardPayload = discardPayload;
            _releasePayloadSlot = releasePayloadSlot;
            _requestRetry = requestRetry;
            _reportFailure = reportFailure;
        }

        public bool CanContinue() => _canContinue();
        public void ReservePayloadSlot() => _reservePayloadSlot();
        public object Build() => _build();
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
            Action<Exception, bool> reportFailure)
    {
        TestWorkerOperation operation = new(
                canContinue,
                reservePayloadSlot,
                build,
                publish,
                discardPayload,
                releasePayloadSlot,
                requestRetry,
                reportFailure);
        return ChunkWorkerLifecycle.Execute<object, TestWorkerOperation>(ref operation);
    }

}
