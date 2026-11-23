using System;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Worker ownership state for chunk coordinates. Callers serialize access
/// with VoxelWorld's queue lock so queue membership and ownership change
/// atomically.
/// </summary>
internal sealed class ChunkWorkTracker
{
    private readonly HashSet<Vector2I> _inFlight = [];
    private readonly HashSet<Vector2I> _rebuildAfterFlight = [];
    private readonly Dictionary<Vector2I, int> _failureAttempts = [];

    public bool TryBegin(Vector2I cp) => _inFlight.Add(cp);

    public bool IsInFlight(Vector2I cp) => _inFlight.Contains(cp);

    /// <summary>Records an edit that arrives after a coordinate has been dequeued.</summary>
    public void RequestRebuild(Vector2I cp)
    {
        if (_inFlight.Contains(cp))
        {
            _rebuildAfterFlight.Add(cp);
        }
    }

    /// <summary>
    /// Records a bounded retry only while the world and coordinate are still
    /// active. A departed coordinate never leaves an unreachable retry entry.
    /// </summary>
    public bool TryRecordFailure(
            Vector2I cp, bool alive, bool withinRetention, int maximumAttempts, out int attempts)
    {
        attempts = 0;
        if (!alive || !withinRetention)
        {
            _failureAttempts.Remove(cp);
            return false;
        }

        attempts = _failureAttempts.TryGetValue(cp, out int previous) ? previous + 1 : 1;
        if (!ChunkStreamingPolicy.ShouldRetry(attempts, maximumAttempts))
        {
            _failureAttempts.Remove(cp);
            return false;
        }

        _failureAttempts[cp] = attempts;
        return true;
    }

    public void ForgetFailure(Vector2I cp) => _failureAttempts.Remove(cp);

    /// <returns>True when one follow-up rebuild was requested during the work.</returns>
    public bool Complete(Vector2I cp, bool withinRetention = true)
    {
        _inFlight.Remove(cp);
        if (!withinRetention)
        {
            _failureAttempts.Remove(cp);
        }

        return _rebuildAfterFlight.Remove(cp);
    }

    public void ForgetFollowUp(Vector2I cp) => _rebuildAfterFlight.Remove(cp);

    /// <summary>A rebuild request always upgrades a fresh retry to rebuild work.</summary>
    public static bool ShouldQueueAsRebuild(bool currentRebuild, bool rebuildRequested) =>
            currentRebuild || rebuildRequested;

    public int InFlightCount => _inFlight.Count;
    public int FailureCount => _failureAttempts.Count;
}


/// <summary>
/// Per-coordinate generation reservations. Callers protect the dictionary
/// with the same world mutex used for the chunk-data cache.
/// </summary>
internal sealed class ChunkGenerationReservations
{
    private readonly Dictionary<Vector2I, Lazy<byte[]>> _inFlight = new();

    public Lazy<byte[]> GetOrCreate(Vector2I cp, Func<byte[]> generate)
    {
        if (!_inFlight.TryGetValue(cp, out Lazy<byte[]>? reservation))
        {
            reservation = new Lazy<byte[]>(generate,
                    System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
            _inFlight.Add(cp, reservation);
        }

        return reservation;
    }

    /// <summary>Remove only the reservation being completed; a newer one is untouched.</summary>
    public void Complete(Vector2I cp, Lazy<byte[]> reservation)
    {
        if (_inFlight.TryGetValue(cp, out Lazy<byte[]>? current)
                && ReferenceEquals(current, reservation))
        {
            _inFlight.Remove(cp);
        }
    }

    public int Count => _inFlight.Count;
}

/// <summary>Small, deterministic rules shared by world streaming and regression tests.</summary>
internal static class WorldCompatibilityPolicy
{
    public static bool IsSupportedGeneratorVersion(int version, int currentVersion) =>
            version >= 1 && version <= currentVersion;

    public static bool HasValidNoiseSeeds(int[]? seeds) => seeds is { Length: 5 };

    public static bool IsSupportedSaveVersion(int version, int currentVersion) =>
            version >= 1 && version <= currentVersion;

    public static bool MustRejectMissingManifest(bool manifestCandidateExists,
            bool worldSaveExists, bool playerSaveExists) =>
            manifestCandidateExists || worldSaveExists || playerSaveExists;
}

internal static class ChunkStreamingPolicy
{
    public static bool IsWithinRetention(Vector2I cp, Vector2I center, int limit) =>
            System.Math.Abs(cp.X - center.X) <= limit
            && System.Math.Abs(cp.Y - center.Y) <= limit;

    /// <summary>
    /// Refresh queued priorities on stops and meaningful turns, but ignore
    /// minor heading jitter so the heap is not rebuilt every frame.
    /// Directions are expected to be normalized or zero.
    /// </summary>
    public static bool DirectionChanged(Vector3 previous, Vector3 current)
    {
        bool wasMoving = previous.LengthSquared() > 0.01f;
        bool isMoving = current.LengthSquared() > 0.01f;
        if (wasMoving != isMoving)
        {
            return true;
        }

        return wasMoving && previous.Dot(current) < 0.8f;
    }

    /// <summary>
    /// The owner always changes. Geometry neighbours only sample the edited
    /// voxel when it lies on their shared edge/corner; light changes use the
    /// wider existing 3x3 propagation neighbourhood.
    /// </summary>
    public static bool AffectsChunk(int dx, int dz, int localX, int localZ,
            int chunkX, int chunkZ, bool lightingChanged)
    {
        if (dx < -1 || dx > 1 || dz < -1 || dz > 1)
        {
            return false;
        }

        if (lightingChanged || dx == 0 && dz == 0)
        {
            return true;
        }

        bool xAffected = dx == 0
                || dx < 0 && localX == 0
                || dx > 0 && localX == chunkX - 1;
        bool zAffected = dz == 0
                || dz < 0 && localZ == 0
                || dz > 0 && localZ == chunkZ - 1;
        return xAffected && zAffected;
    }

    /// <summary>
    /// Tests whether any chunk touched by a deterministic generator cell lies
    /// inside the retained area. The full cell bounds are used so null-cache
    /// entries are evicted only when their entire procedural region is distant.
    /// </summary>
    public static bool IsSiteCellOutsideRetention(int cellX, int cellZ, int cellSize,
            Vector2I center, int limit, int chunkSize)
    {
        int minChunkX = FloorDivide((long)cellX * cellSize, chunkSize);
        int maxChunkX = FloorDivide(((long)cellX + 1) * cellSize - 1, chunkSize);
        int minChunkZ = FloorDivide((long)cellZ * cellSize, chunkSize);
        int maxChunkZ = FloorDivide(((long)cellZ + 1) * cellSize - 1, chunkSize);
        return maxChunkX < center.X - limit || minChunkX > center.X + limit
                || maxChunkZ < center.Y - limit || minChunkZ > center.Y + limit;
    }

    private static int FloorDivide(long value, int divisor) =>
            (int)System.Math.Floor(value / (double)divisor);

    /// <summary>Clear only the active region of a rented scratch buffer.</summary>
    public static void ClearUsedBuffer(byte[] buffer, int usedLength) =>
            Array.Clear(buffer, 0, usedLength);

    /// <summary>Maximum total attempts, including the initial build.</summary>
    public static bool ShouldRetry(int failedAttempts, int maximumAttempts) =>
            maximumAttempts > 0 && failedAttempts > 0 && failedAttempts < maximumAttempts;
}

/// <summary>One implementation of the real worker build/publish contract.</summary>
internal interface IChunkWorkerOperation<TPayload>
        where TPayload : class
{
    bool CanContinue();
    void ReservePayloadSlot();
    TPayload Build();
    bool Publish(TPayload payload);
    void DiscardPayload(TPayload payload);
    void ReleasePayloadSlot();
    bool RequestRetry(Exception error);
    void ReportFailure(Exception error, bool retryRequested);
}

/// <summary>Result of one actual chunk worker build/publish attempt.</summary>
internal readonly record struct ChunkWorkerExecutionResult(
        bool Published, bool RetryRequested, bool Cancelled);

/// <summary>
/// Shared worker execution path used by VoxelWorld.WorkerLoop and deterministic
/// regression tests. The operation is a value type so dispatch adds no captured
/// delegates or per-chunk lifecycle allocations.
/// </summary>
internal static class ChunkWorkerLifecycle
{
    public static ChunkWorkerExecutionResult Execute<TPayload, TOperation>(
            ref TOperation operation)
            where TPayload : class
            where TOperation : struct, IChunkWorkerOperation<TPayload>
    {
        bool reserved = false;
        bool published = false;
        bool cancelled = false;
        bool retryRequested = false;
        TPayload? payload = null;

        try
        {
            if (!operation.CanContinue())
            {
                return new ChunkWorkerExecutionResult(false, false, false);
            }

            operation.ReservePayloadSlot();
            reserved = true;
            if (!operation.CanContinue())
            {
                return new ChunkWorkerExecutionResult(false, false, false);
            }

            payload = operation.Build();
            if (!operation.CanContinue())
            {
                return new ChunkWorkerExecutionResult(false, false, false);
            }

            if (operation.Publish(payload))
            {
                published = true;
                payload = null;
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception error)
        {
            retryRequested = operation.RequestRetry(error);
            operation.ReportFailure(error, retryRequested);
        }
        finally
        {
            try
            {
                if (payload != null)
                {
                    operation.DiscardPayload(payload);
                }
            }
            finally
            {
                if (reserved && !published)
                {
                    operation.ReleasePayloadSlot();
                }
            }
        }

        return new ChunkWorkerExecutionResult(published, retryRequested, cancelled);
    }
}
