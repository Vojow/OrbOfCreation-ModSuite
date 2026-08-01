using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbChronicle;

internal enum ChronicleRunState
{
    Dormant = 0,
    Running = 1,
    Paused = 2,
    Finished = 3,
    Abandoned = 4,
}

internal readonly struct ChronicleCommandOutcome
{
    internal ChronicleCommandOutcome(bool accepted, string code, string reason)
    {
        Accepted = accepted;
        Code = code ?? string.Empty;
        Reason = reason ?? string.Empty;
    }

    internal bool Accepted { get; }
    internal string Code { get; }
    internal string Reason { get; }
}

internal enum ChronicleResourceKpiState
{
    Pending = 0,
    Captured = 1,
    Preexisting = 2,
    Missing = 3,
}

internal readonly struct ChronicleResourceKpiReading
{
    internal ChronicleResourceKpiReading(in WorldResource resource)
    {
        Visible = resource.Reading.Visible;
        Quantity = resource.Reading.Quantity;
        TrueQuantity = resource.TrueQuantity;
        TrueRate = resource.TrueRate;
        IsCapped = resource.IsCapped;
        Capacity = resource.IsCapped ? resource.Reading.Capacity : default;
        FillFraction = resource.FillFraction;
        IsAtCapacity = resource.IsAtCapacity;
    }

    internal bool Visible { get; }
    internal BigDouble Quantity { get; }
    internal BigDouble TrueQuantity { get; }
    internal BigDouble TrueRate { get; }
    internal bool IsCapped { get; }
    internal BigDouble Capacity { get; }
    internal double FillFraction { get; }
    internal bool IsAtCapacity { get; }
}

internal sealed class ChronicleResourceKpiSnapshot
{
    internal ChronicleResourceKpiSnapshot(
        ChronicleResourceDefinition definition,
        ChronicleResourceKpiState state,
        long? elapsedTicks,
        ChronicleResourceKpiReading? reading)
    {
        Id = definition.Id;
        Label = definition.Label;
        TargetUuid = definition.TargetId.ToString("D");
        ExpectedNativeType = definition.ExpectedNativeType;
        State = state;
        ElapsedTicks = elapsedTicks;
        ElapsedSeconds = elapsedTicks.HasValue
            ? elapsedTicks.Value / (double)TimeSpan.TicksPerSecond
            : null;
        Visible = reading?.Visible;
        Quantity = reading?.Quantity;
        TrueQuantity = reading?.TrueQuantity;
        TrueRate = reading?.TrueRate;
        IsCapped = reading?.IsCapped;
        Capacity = reading.HasValue && reading.Value.IsCapped
            ? reading.Value.Capacity
            : null;
        FillFraction = reading.HasValue && reading.Value.IsCapped
            ? reading.Value.FillFraction
            : null;
        IsAtCapacity = reading.HasValue && reading.Value.IsCapped
            ? reading.Value.IsAtCapacity
            : null;
    }

    internal string Id { get; }
    internal string Label { get; }
    internal string TargetUuid { get; }
    internal string ExpectedNativeType { get; }
    internal ChronicleResourceKpiState State { get; }
    internal long? ElapsedTicks { get; }
    internal double? ElapsedSeconds { get; }
    internal bool? Visible { get; }
    internal BigDouble? Quantity { get; }
    internal BigDouble? TrueQuantity { get; }
    internal BigDouble? TrueRate { get; }
    internal bool? IsCapped { get; }
    internal BigDouble? Capacity { get; }
    internal double? FillFraction { get; }
    internal bool? IsAtCapacity { get; }
}

internal sealed class ChronicleResourceSectionSnapshot
{
    internal ChronicleResourceSectionSnapshot(
        ChronicleResourceSectionDefinition definition,
        ChronicleResourceKpiSnapshot[] resources)
    {
        if (resources is null) throw new ArgumentNullException(nameof(resources));
        Id = definition.Id;
        Label = definition.Label;
        Relationship = definition.Relationship;
        Resources = Array.AsReadOnly((ChronicleResourceKpiSnapshot[])resources.Clone());
        for (var index = 0; index < resources.Length; index++)
        {
            switch (resources[index].State)
            {
                case ChronicleResourceKpiState.Pending: PendingCount++; break;
                case ChronicleResourceKpiState.Captured: CapturedCount++; break;
                case ChronicleResourceKpiState.Preexisting: PreexistingCount++; break;
                case ChronicleResourceKpiState.Missing: MissingCount++; break;
                default: throw new ArgumentOutOfRangeException(nameof(resources));
            }
        }
    }

    internal string Id { get; }
    internal string Label { get; }
    internal string Relationship { get; }
    internal string CaptureMode => "first-visible";
    internal int PendingCount { get; private set; }
    internal int CapturedCount { get; private set; }
    internal int PreexistingCount { get; private set; }
    internal int MissingCount { get; private set; }
    internal IReadOnlyList<ChronicleResourceKpiSnapshot> Resources { get; }
}

internal sealed class ChronicleMilestoneSnapshot
{
    internal ChronicleMilestoneSnapshot(
        ChronicleMilestoneDefinition definition,
        ChronicleMilestoneState state,
        long? elapsedTicks)
    {
        Id = definition.Id;
        Label = definition.Label;
        TargetUuid = definition.TargetId == Guid.Empty
            ? string.Empty
            : definition.TargetId.ToString("D");
        ExpectedNativeType = definition.ExpectedNativeType;
        DisplayOrder = definition.DisplayOrder;
        State = state;
        ElapsedTicks = elapsedTicks;
        ElapsedSeconds = elapsedTicks.HasValue
            ? elapsedTicks.Value / (double)TimeSpan.TicksPerSecond
            : null;
    }

    internal string Id { get; }
    internal string Label { get; }
    internal string TargetUuid { get; }
    internal string ExpectedNativeType { get; }
    internal int DisplayOrder { get; }
    internal ChronicleMilestoneState State { get; }
    internal long? ElapsedTicks { get; }
    internal double? ElapsedSeconds { get; }
}

internal sealed class ChronicleRunSnapshot
{
    internal ChronicleRunSnapshot(
        long revision,
        Guid runId,
        ChronicleRunState state,
        long elapsedTicks,
        long lifecycleGeneration,
        ulong worldGeneration,
        string reason,
        ChronicleMilestoneSnapshot[] milestones,
        ChronicleResourceSectionSnapshot[] resourceSections)
    {
        if (milestones is null) throw new ArgumentNullException(nameof(milestones));
        if (resourceSections is null) throw new ArgumentNullException(nameof(resourceSections));
        if (elapsedTicks < 0) throw new ArgumentOutOfRangeException(nameof(elapsedTicks));

        Revision = revision;
        RunId = runId == Guid.Empty ? string.Empty : runId.ToString("D");
        State = state;
        ElapsedTicks = elapsedTicks;
        ElapsedSeconds = elapsedTicks / (double)TimeSpan.TicksPerSecond;
        LifecycleGeneration = lifecycleGeneration;
        WorldGeneration = worldGeneration;
        Reason = reason ?? string.Empty;
        MilestoneSchemaId = ChronicleMilestones.SchemaId;
        ClockId = ChronicleMilestones.ClockId;
        ResourceSchemaId = ChronicleResources.SchemaId;
        Milestones = Array.AsReadOnly((ChronicleMilestoneSnapshot[])milestones.Clone());
        ResourceSections = Array.AsReadOnly(
            (ChronicleResourceSectionSnapshot[])resourceSections.Clone());
    }

    internal long Revision { get; }
    internal string RunId { get; }
    internal ChronicleRunState State { get; }
    internal long ElapsedTicks { get; }
    internal double ElapsedSeconds { get; }
    internal long LifecycleGeneration { get; }
    internal ulong WorldGeneration { get; }
    internal string Reason { get; }
    internal string MilestoneSchemaId { get; }
    internal string ClockId { get; }
    internal string ResourceSchemaId { get; }
    internal IReadOnlyList<ChronicleMilestoneSnapshot> Milestones { get; }
    internal IReadOnlyList<ChronicleResourceSectionSnapshot> ResourceSections { get; }
}

internal sealed class ChronicleRunTracker
{
    private readonly ChronicleMilestoneState[] _states =
        new ChronicleMilestoneState[ChronicleMilestones.Count];
    private readonly long?[] _elapsedTicks = new long?[ChronicleMilestones.Count];
    private readonly ChronicleResourceKpiState[][] _resourceStates =
        new ChronicleResourceKpiState[ChronicleResources.Count][];
    private readonly long?[][] _resourceElapsedTicks =
        new long?[ChronicleResources.Count][];
    private readonly ChronicleResourceKpiReading?[][] _resourceReadings =
        new ChronicleResourceKpiReading?[ChronicleResources.Count][];
    private ChronicleWorldObservation _latestObservation =
        ChronicleWorldObservation.Unavailable("the shared world has not been observed");
    private ChronicleRunState _state;
    private Guid _runId;
    private long _elapsedTicksTotal;
    private long _lastObservedAtTicks;
    private long _runLifecycle;
    private ulong _worldGeneration;
    private ulong _requiredReachedMask;
    private string _reason = "no run has been started";
    private bool _sawWorldRestoredFalse;
    private long _revision = 1;

    internal ChronicleRunTracker()
    {
        for (var index = 0; index < _resourceReadings.Length; index++)
        {
            _resourceStates[index] =
                new ChronicleResourceKpiState[ChronicleResources.At(index).Resources.Count];
            _resourceElapsedTicks[index] =
                new long?[ChronicleResources.At(index).Resources.Count];
            _resourceReadings[index] =
                new ChronicleResourceKpiReading?[ChronicleResources.At(index).Resources.Count];
        }
        ResetMilestones();
    }

    internal ChronicleWorldObservation LatestObservation => _latestObservation;

    internal ChronicleRunSnapshot Snapshot
    {
        get
        {
            var milestones = new ChronicleMilestoneSnapshot[ChronicleMilestones.Count];
            for (var index = 0; index < milestones.Length; index++)
            {
                milestones[index] = new ChronicleMilestoneSnapshot(
                    ChronicleMilestones.At(index),
                    _states[index],
                    _elapsedTicks[index]);
            }
            var resourceSections = BuildResourceSectionSnapshots();
            return new ChronicleRunSnapshot(
                _revision,
                _runId,
                _state,
                _elapsedTicksTotal,
                _runLifecycle,
                _worldGeneration,
                _reason,
                milestones,
                resourceSections);
        }
    }

    internal void Observe(in ChronicleWorldObservation observation)
    {
        _latestObservation = observation;
        if (_state != ChronicleRunState.Running) return;
        if (!observation.Available)
        {
            PauseAutomatically(
                "run paused because Chronicle lost a complete world observation: " +
                observation.UnavailableReason);
            return;
        }
        if (observation.LifecycleGeneration != _runLifecycle)
        {
            PauseAutomatically(
                "run paused because lifecycle changed from " + _runLifecycle + " to " +
                observation.LifecycleGeneration);
            return;
        }
        if (observation.WorldGeneration < _worldGeneration)
        {
            PauseAutomatically("run paused because the published world generation moved backwards");
            return;
        }
        if (observation.ObservedAtTicks < _lastObservedAtTicks)
        {
            PauseAutomatically("run paused because the monotonic clock moved backwards");
            return;
        }
        if ((observation.ReachedMask & _requiredReachedMask) != _requiredReachedMask)
        {
            PauseAutomatically(
                "run paused because previously observed native progression regressed without a lifecycle transition");
            return;
        }

        var elapsedSinceLast = observation.ObservedAtTicks - _lastObservedAtTicks;
        if (_elapsedTicksTotal > long.MaxValue - elapsedSinceLast)
        {
            PauseAutomatically("run paused because elapsed time exceeded the supported duration");
            return;
        }
        _elapsedTicksTotal += elapsedSinceLast;
        _lastObservedAtTicks = observation.ObservedAtTicks;
        _worldGeneration = observation.WorldGeneration;
        var changed = elapsedSinceLast > 0;
        if (!observation.WorldRestored) _sawWorldRestoredFalse = true;

        changed |= ApplyMilestones(in observation);
        changed |= ApplyResourceDiscoveries(observation.Resources);

        if (changed) _revision++;
    }

    internal ChronicleCommandOutcome Start()
    {
        if (!_latestObservation.Available)
        {
            return Rejected(
                "chronicle_world_not_available",
                _latestObservation.UnavailableReason);
        }
        if (_state is ChronicleRunState.Running or ChronicleRunState.Paused)
            return Rejected("chronicle_run_active", "a Chronicle run is already active");

        ResetMilestones();
        _runId = Guid.NewGuid();
        _state = ChronicleRunState.Running;
        _elapsedTicksTotal = 0;
        _lastObservedAtTicks = _latestObservation.ObservedAtTicks;
        _runLifecycle = _latestObservation.LifecycleGeneration;
        _worldGeneration = _latestObservation.WorldGeneration;
        _requiredReachedMask = 0;
        _reason = "run is active";
        _states[ChronicleMilestones.MagicIndex] = ChronicleMilestoneState.Reached;
        _elapsedTicks[ChronicleMilestones.MagicIndex] = 0;
        for (var index = 1; index < ChronicleMilestones.Count; index++)
        {
            var mask = ChronicleMilestones.At(index).Mask;
            if ((_latestObservation.BlockedMask & mask) != 0)
                _states[index] = ChronicleMilestoneState.Blocked;
            else if ((_latestObservation.ReachedMask & mask) != 0)
            {
                _states[index] = ChronicleMilestoneState.Preexisting;
                _requiredReachedMask |= mask;
            }
        }
        InitializeResources(_latestObservation.Resources);
        _sawWorldRestoredFalse = !_latestObservation.WorldRestored;
        _revision++;
        return Accepted("chronicle_started", "Chronicle run started");
    }

    internal ChronicleCommandOutcome Pause()
    {
        if (_state != ChronicleRunState.Running)
            return Rejected("chronicle_not_running", "only a running Chronicle run can be paused");
        _state = ChronicleRunState.Paused;
        _reason = "run paused by command";
        _revision++;
        return Accepted("chronicle_paused", _reason);
    }

    internal ChronicleCommandOutcome Resume()
    {
        if (_state != ChronicleRunState.Paused)
            return Rejected("chronicle_not_paused", "only a paused Chronicle run can be resumed");
        if (!_latestObservation.Available)
            return Rejected("chronicle_world_not_available", _latestObservation.UnavailableReason);
        if (_latestObservation.LifecycleGeneration != _runLifecycle)
        {
            return Rejected(
                "chronicle_lifecycle_changed",
                "the paused run belongs to lifecycle " + _runLifecycle +
                "; the current lifecycle is " + _latestObservation.LifecycleGeneration);
        }
        if (_latestObservation.WorldGeneration < _worldGeneration)
        {
            return Rejected(
                "chronicle_world_regressed",
                "the current world generation predates the paused run's last observation");
        }
        if (_latestObservation.ObservedAtTicks < _lastObservedAtTicks)
        {
            return Rejected(
                "chronicle_clock_regressed",
                "the current monotonic timestamp predates the paused run's last observation");
        }
        if ((_latestObservation.ReachedMask & _requiredReachedMask) != _requiredReachedMask)
        {
            return Rejected(
                "chronicle_progress_regressed",
                "previously observed native progression is absent from the current world");
        }

        _state = ChronicleRunState.Running;
        _lastObservedAtTicks = _latestObservation.ObservedAtTicks;
        _worldGeneration = _latestObservation.WorldGeneration;
        _reason = "run is active";
        ApplyMilestones(in _latestObservation);
        ApplyResourceDiscoveries(_latestObservation.Resources);
        _revision++;
        return _state == ChronicleRunState.Finished
            ? Accepted(
                "chronicle_finished_on_resume",
                "Chronicle resumed and the world-restored split completed")
            : Accepted("chronicle_resumed", "Chronicle run resumed");
    }

    internal ChronicleCommandOutcome Abandon()
    {
        if (_state is not (ChronicleRunState.Running or ChronicleRunState.Paused))
            return Rejected("chronicle_no_active_run", "there is no active Chronicle run to abandon");
        _state = ChronicleRunState.Abandoned;
        _reason = "run abandoned by command";
        _revision++;
        return Accepted("chronicle_abandoned", _reason);
    }

    private void PauseAutomatically(string reason)
    {
        _state = ChronicleRunState.Paused;
        _reason = reason;
        _revision++;
    }

    private bool ApplyMilestones(in ChronicleWorldObservation observation)
    {
        var changed = false;
        for (var index = 1; index < ChronicleMilestones.Count; index++)
        {
            if (_states[index] != ChronicleMilestoneState.Pending) continue;
            var mask = ChronicleMilestones.At(index).Mask;
            if ((observation.BlockedMask & mask) != 0)
            {
                _states[index] = ChronicleMilestoneState.Blocked;
                changed = true;
                continue;
            }
            if ((observation.ReachedMask & mask) == 0) continue;
            if (index == ChronicleMilestones.WorldRestoredIndex && !_sawWorldRestoredFalse)
                continue;
            _states[index] = ChronicleMilestoneState.Reached;
            _elapsedTicks[index] = _elapsedTicksTotal;
            _requiredReachedMask |= mask;
            changed = true;
            if (index == ChronicleMilestones.WorldRestoredIndex)
            {
                _state = ChronicleRunState.Finished;
                _reason = "world restored";
            }
        }
        return changed;
    }

    private void ResetMilestones()
    {
        for (var index = 0; index < _states.Length; index++)
        {
            _states[index] = ChronicleMilestoneState.Pending;
            _elapsedTicks[index] = null;
        }
        for (var sectionIndex = 0; sectionIndex < _resourceReadings.Length; sectionIndex++)
        {
            Array.Clear(_resourceStates[sectionIndex], 0, _resourceStates[sectionIndex].Length);
            Array.Clear(
                _resourceElapsedTicks[sectionIndex],
                0,
                _resourceElapsedTicks[sectionIndex].Length);
            Array.Clear(_resourceReadings[sectionIndex], 0, _resourceReadings[sectionIndex].Length);
        }
    }

    private void InitializeResources(PublicationTable<WorldResource> resources)
    {
        for (var sectionIndex = 0; sectionIndex < ChronicleResources.Count; sectionIndex++)
        {
            var section = ChronicleResources.At(sectionIndex);
            for (var resourceIndex = 0; resourceIndex < section.Resources.Count; resourceIndex++)
            {
                var definition = section.Resources[resourceIndex];
                _resourceStates[sectionIndex][resourceIndex] =
                    !WorldLookup.TryFind(resources, definition.TargetId, out var resource)
                        ? ChronicleResourceKpiState.Missing
                        : resource.Reading.Visible
                            ? ChronicleResourceKpiState.Preexisting
                            : ChronicleResourceKpiState.Pending;
            }
        }
    }

    private bool ApplyResourceDiscoveries(PublicationTable<WorldResource> resources)
    {
        var changed = false;
        for (var sectionIndex = 0; sectionIndex < ChronicleResources.Count; sectionIndex++)
        {
            var section = ChronicleResources.At(sectionIndex);
            for (var resourceIndex = 0; resourceIndex < section.Resources.Count; resourceIndex++)
            {
                if (_resourceStates[sectionIndex][resourceIndex] !=
                    ChronicleResourceKpiState.Pending)
                {
                    continue;
                }
                var definition = section.Resources[resourceIndex];
                if (!WorldLookup.TryFind(resources, definition.TargetId, out var resource))
                {
                    _resourceStates[sectionIndex][resourceIndex] =
                        ChronicleResourceKpiState.Missing;
                    changed = true;
                    continue;
                }
                if (!resource.Reading.Visible) continue;

                _resourceStates[sectionIndex][resourceIndex] =
                    ChronicleResourceKpiState.Captured;
                _resourceElapsedTicks[sectionIndex][resourceIndex] = _elapsedTicksTotal;
                _resourceReadings[sectionIndex][resourceIndex] =
                    new ChronicleResourceKpiReading(in resource);
                changed = true;
            }
        }
        return changed;
    }

    private ChronicleResourceSectionSnapshot[] BuildResourceSectionSnapshots()
    {
        var sections = new ChronicleResourceSectionSnapshot[ChronicleResources.Count];
        for (var sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
        {
            var definition = ChronicleResources.At(sectionIndex);
            var states = _resourceStates[sectionIndex];
            var elapsedTicks = _resourceElapsedTicks[sectionIndex];
            var readings = _resourceReadings[sectionIndex];
            var resources = new ChronicleResourceKpiSnapshot[definition.Resources.Count];
            for (var resourceIndex = 0; resourceIndex < resources.Length; resourceIndex++)
            {
                resources[resourceIndex] = new ChronicleResourceKpiSnapshot(
                    definition.Resources[resourceIndex],
                    states[resourceIndex],
                    elapsedTicks[resourceIndex],
                    readings[resourceIndex]);
            }
            sections[sectionIndex] = new ChronicleResourceSectionSnapshot(
                definition,
                resources);
        }
        return sections;
    }

    private static ChronicleCommandOutcome Accepted(string code, string reason) =>
        new(true, code, reason);

    private static ChronicleCommandOutcome Rejected(string code, string reason) =>
        new(false, code, reason);
}
