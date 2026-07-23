using System;
using System.Buffers.Binary;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Execution;

internal sealed class Factory : IServiceCycleReplayExecutionFactory<
    Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
{
    private readonly int _actionCount;
    private readonly int _configurationHydrationOffset;
    private readonly int _frameHydrationOffset;
    private readonly int _previousStateHydrationOffset;
    private readonly ulong _strategyRecreationOffset;
    private readonly TimeSpan _productionDelay;
    private readonly WakePolicy _productionWake;
    private readonly ServiceId _serviceId;
    private readonly int _inputMaximumEncodedBytes;
    private readonly bool _projectionFault;

    internal Factory(
        int actionCount = 0,
        int frameHydrationOffset = 0,
        int configurationHydrationOffset = 0,
        int previousStateHydrationOffset = 0,
        ulong strategyRecreationOffset = 0,
        TimeSpan productionDelay = default,
        WakePolicy productionWake = default,
        ServiceId serviceId = default,
        int inputMaximumEncodedBytes = 16,
        bool projectionFault = false)
    {
        _actionCount = actionCount;
        _frameHydrationOffset = frameHydrationOffset;
        _configurationHydrationOffset = configurationHydrationOffset;
        _previousStateHydrationOffset = previousStateHydrationOffset;
        _strategyRecreationOffset = strategyRecreationOffset;
        _productionDelay = productionDelay;
        _productionWake = productionWake;
        _serviceId = serviceId.IsValid ? serviceId : new ServiceId("test.replay-execution");
        _inputMaximumEncodedBytes = inputMaximumEncodedBytes;
        _projectionFault = projectionFault;
    }

    internal int CreationCount { get; private set; }
    public ServiceId ServiceId => _serviceId;
    public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(new MonotonicDuration(1), new MonotonicDuration(8));
    public Frame CreateFrame() => new();
    public IServiceCycleReplayCodec<InputRecord> CreateCycleInputCodec() =>
        Created(new InputCodec(_inputMaximumEncodedBytes));
    public IServiceCycleReplayCodec<StateRecord> CreateStateCodec() => Created(new StateCodec());
    public IServiceCycleReplayCodec<ActionRecord> CreateActionCodec() => Created(new ActionCodec());
    public IServiceCycleReplayComparer<InputRecord> CreateCycleInputComparer() => Created(new InputComparer());
    public IServiceCycleReplayComparer<StateRecord> CreateStateComparer() => Created(new ValueComparer<StateRecord>());
    public IServiceCycleReplayComparer<ActionRecord> CreateActionComparer() => Created(new ValueComparer<ActionRecord>());
    public IServiceCycleReplayHydrator<Frame, Config, State, InputRecord, StateRecord> CreateHydrator() =>
        Created(new Hydrator(
            _frameHydrationOffset,
            _configurationHydrationOffset,
            _previousStateHydrationOffset,
            _strategyRecreationOffset));
    public IServiceCycleReplayEvaluatorPort<Frame, Config, State, Action, StateRecord, ActionRecord>
        CreateEvaluatorPort() => Created(new Evaluator(_actionCount, projectionFault: _projectionFault));
    public ServiceCycleReplayWorker<Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
        CreateProductionWorkerDefinition()
    {
        var evaluator = new Evaluator(_actionCount, _productionWake, _projectionFault);
        if (_productionDelay > TimeSpan.Zero)
            return Created(new TestReplayWorker(new DelayedEvaluator(evaluator, _productionDelay)));
        return Created(new TestReplayWorker(evaluator));
    }

    private T Created<T>(T value)
    {
        CreationCount++;
        return value;
    }
}

internal sealed class TestReplayWorker : ServiceCycleReplayWorker<
    Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
{
    internal TestReplayWorker(int actionCount = 0)
        : base(new Evaluator(actionCount), new InputCodec(), new StateCodec(), new ActionCodec()) { }

    internal TestReplayWorker(IServiceCycleReplayEvaluatorPort<
        Frame, Config, State, Action, StateRecord, ActionRecord> evaluator)
        : base(evaluator, new InputCodec(), new StateCodec(), new ActionCodec()) { }
}

internal sealed class DelayedEvaluator : IServiceCycleReplayEvaluatorPort<
    Frame, Config, State, Action, StateRecord, ActionRecord>
{
    private readonly Evaluator _inner;
    private readonly TimeSpan _delay;

    internal DelayedEvaluator(
        Evaluator inner,
        TimeSpan delay)
    {
        _inner = inner;
        _delay = delay;
    }

    public State CreateState(LifecycleGeneration lifecycle) => _inner.CreateState(lifecycle);
    public void ReleaseState(ref State state) => _inner.ReleaseState(ref state);
    public void ReleaseFrame(ref Frame frame) => _inner.ReleaseFrame(ref frame);
    public StateRecord CreateStateRecord(in State state) => _inner.CreateStateRecord(in state);

    public WakePolicy Evaluate(
        in Frame frame,
        in Config config,
        in ServiceCycleContext context,
        ref State state,
        ServiceCycleReplayActionWriter<Action, ActionRecord> actions)
    {
        using (var gate = new ManualResetEventSlim(false)) gate.Wait(_delay);
        return _inner.Evaluate(in frame, in config, in context, ref state, actions);
    }

    public void ProjectState(
        in State state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        _inner.ProjectState(in state, in context, output);
}

internal sealed class StateFactoryProbeEvaluator : IServiceCycleReplayEvaluatorPort<
    Frame, Config, State, Action, StateRecord, ActionRecord>
{
    private readonly Evaluator _inner;
    private readonly System.Action _onCreate;

    internal StateFactoryProbeEvaluator(
        Evaluator inner,
        System.Action onCreate)
    {
        _inner = inner;
        _onCreate = onCreate;
    }

    public State CreateState(LifecycleGeneration lifecycle)
    {
        _onCreate();
        return _inner.CreateState(lifecycle);
    }

    public void ReleaseState(ref State state) => _inner.ReleaseState(ref state);
    public void ReleaseFrame(ref Frame frame) => _inner.ReleaseFrame(ref frame);
    public StateRecord CreateStateRecord(in State state) => _inner.CreateStateRecord(in state);

    public WakePolicy Evaluate(
        in Frame frame,
        in Config config,
        in ServiceCycleContext context,
        ref State state,
        ServiceCycleReplayActionWriter<Action, ActionRecord> actions) =>
        _inner.Evaluate(in frame, in config, in context, ref state, actions);

    public void ProjectState(
        in State state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        _inner.ProjectState(in state, in context, output);
}

internal sealed class Hydrator : IServiceCycleReplayHydrator<Frame, Config, State, InputRecord, StateRecord>
{
    private readonly int _configurationOffset;
    private readonly int _frameOffset;
    private readonly int _previousStateOffset;
    private readonly ulong _strategyOffset;

    internal Hydrator(
        int frameOffset = 0,
        int configurationOffset = 0,
        int previousStateOffset = 0,
        ulong strategyOffset = 0)
    {
        _frameOffset = frameOffset;
        _configurationOffset = configurationOffset;
        _previousStateOffset = previousStateOffset;
        _strategyOffset = strategyOffset;
    }

    public void HydrateFrame(in InputRecord input, in ServiceCycleReplayContext context, ref Frame frame)
    {
        frame ??= new Frame();
        frame.Value = checked(input.Frame + _frameOffset);
        frame.Strategy = input.Strategy;
    }

    public Config HydrateConfiguration(in InputRecord input, in ServiceCycleReplayContext context) =>
        new(checked(input.Config + _configurationOffset));

    public State HydratePreviousState(in StateRecord previousState, in ServiceCycleReplayContext context) =>
        new() { Value = checked(previousState.Value + _previousStateOffset) };

    public InputRecord RecreateCycleInputRecord(
        in Frame frame,
        in Config config,
        in ServiceCycleReplayContext context) => new(
            frame.Value,
            config.Value,
            checked(frame.Strategy + _strategyOffset));
}

internal sealed class Evaluator : IServiceCycleReplayEvaluatorPort<
    Frame, Config, State, Action, StateRecord, ActionRecord>
{
    private readonly int _actionCount;
    private readonly WakePolicy _wake;
    private readonly bool _projectionFault;

    internal Evaluator(
        int actionCount,
        WakePolicy wake = default,
        bool projectionFault = false)
    {
        _actionCount = actionCount;
        _wake = wake.Kind == 0 ? WakePolicy.Immediate : wake;
        _projectionFault = projectionFault;
    }
    internal bool ThrowEvaluation;

    public State CreateState(LifecycleGeneration lifecycle) => new();
    public void ReleaseState(ref State state) => state = null!;
    public void ReleaseFrame(ref Frame frame) => frame = null!;
    public StateRecord CreateStateRecord(in State state) => new(state.Value);

    public WakePolicy Evaluate(
        in Frame frame,
        in Config config,
        in ServiceCycleContext context,
        ref State state,
        ServiceCycleReplayActionWriter<Action, ActionRecord> actions)
    {
        if (ThrowEvaluation) throw new InvalidOperationException("evaluation");
        state.Value++;
        for (var index = 0; index < _actionCount; index++)
        {
            var action = new Action(index);
            var record = new ActionRecord(index);
            actions.Add(in action, in record);
        }
        return _wake;
    }

    public void ProjectState(
        in State state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output)
    {
        if (_projectionFault) throw new InvalidOperationException("projection");
        output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(state.Value));
    }
}

internal sealed class InputCodec : IServiceCycleReplayCodec<InputRecord>
{
    private readonly int _maximumEncodedBytes;

    internal InputCodec(int maximumEncodedBytes = 16) => _maximumEncodedBytes = maximumEncodedBytes;
    internal bool ThrowDecode;
    public ServiceCycleReplayCodecDescriptor Descriptor => new(1, _maximumEncodedBytes);

    public int Encode(in InputRecord record, Span<byte> destination)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, record.Frame);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4), record.Config);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(8), record.Strategy);
        return 16;
    }

    public InputRecord Decode(ReadOnlySpan<byte> source)
    {
        if (ThrowDecode) throw new InvalidOperationException("decode");
        return new InputRecord(
            BinaryPrimitives.ReadInt32LittleEndian(source),
            BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4)),
            BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(8)));
    }
}

internal sealed class NonCanonicalInputCodec : IServiceCycleReplayCodec<InputRecord>
{
    public ServiceCycleReplayCodecDescriptor Descriptor => new(1, 16);

    public int Encode(in InputRecord record, Span<byte> destination)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, record.Frame);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4), 9);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(8), record.Strategy);
        return 16;
    }

    public InputRecord Decode(ReadOnlySpan<byte> source) =>
        new(BinaryPrimitives.ReadInt32LittleEndian(source), 9, 0);
}

internal abstract class IntCodec<TRecord> : IServiceCycleReplayCodec<TRecord>
    where TRecord : struct, IServiceCycleReplayRecord
{
    public ServiceCycleReplayCodecDescriptor Descriptor => new(1, 4);

    public int Encode(in TRecord record, Span<byte> destination)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, Value(in record));
        return 4;
    }

    public TRecord Decode(ReadOnlySpan<byte> source) =>
        Create(BinaryPrimitives.ReadInt32LittleEndian(source));

    protected abstract int Value(in TRecord record);
    protected abstract TRecord Create(int value);
}

internal sealed class StateCodec : IntCodec<StateRecord>
{
    protected override int Value(in StateRecord record) => record.Value;
    protected override StateRecord Create(int value) => new(value);
}

internal sealed class ActionCodec : IntCodec<ActionRecord>
{
    protected override int Value(in ActionRecord record) => record.Value;
    protected override ActionRecord Create(int value) => new(value);
}

internal sealed class InputComparer : IServiceCycleReplayComparer<InputRecord>
{
    public ServiceCycleReplayRecordComparison Compare(in InputRecord expected, in InputRecord actual) =>
        expected.Frame != actual.Frame
            ? new ServiceCycleReplayRecordComparison(1)
            : expected.Config != actual.Config
                ? new ServiceCycleReplayRecordComparison(2)
                : expected.Strategy != actual.Strategy
                    ? new ServiceCycleReplayRecordComparison(3)
                    : ServiceCycleReplayRecordComparison.Match;
}

internal sealed class ValueComparer<TRecord> : IServiceCycleReplayComparer<TRecord>
    where TRecord : struct, IValueRecord, IServiceCycleReplayRecord
{
    internal bool Throw;

    public ServiceCycleReplayRecordComparison Compare(in TRecord expected, in TRecord actual)
    {
        if (Throw) throw new InvalidOperationException("compare");
        return expected.Value == actual.Value
            ? ServiceCycleReplayRecordComparison.Match
            : new ServiceCycleReplayRecordComparison(1);
    }
}

internal interface IValueRecord { int Value { get; } }
internal sealed class Frame
{
    internal int Value;
    internal ulong Strategy;
}
internal readonly struct Config
{
    internal Config(int value) => Value = value;
    internal int Value { get; }
}
internal sealed class State { internal int Value; }
internal readonly struct Action
{
    internal Action(int value) => Value = value;
    internal int Value { get; }
}
internal readonly struct InputRecord : IServiceCycleReplayRecord
{
    internal InputRecord(int frame, int config)
        : this(frame, config, 0) { }

    internal InputRecord(int frame, int config, ulong strategy)
    {
        Frame = frame;
        Config = config;
        Strategy = strategy;
    }

    internal int Frame { get; }
    internal int Config { get; }
    internal ulong Strategy { get; }
}
internal readonly struct StateRecord : IServiceCycleReplayRecord, IValueRecord
{
    internal StateRecord(int value) => Value = value;
    public int Value { get; }
}
internal readonly struct ActionRecord : IServiceCycleReplayRecord, IValueRecord
{
    internal ActionRecord(int value) => Value = value;
    public int Value { get; }
}
