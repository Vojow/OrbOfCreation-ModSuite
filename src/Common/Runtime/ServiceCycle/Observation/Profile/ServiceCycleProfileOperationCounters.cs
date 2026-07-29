#if SERVICE_CYCLE_PROFILE
namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal struct ServiceCycleProfileOperationCounters
{
    private uint _reflectedFieldReads;
    private uint _reflectedMethodCalls;
    private uint _stableIdReads;
    private uint _listEntries;
    private uint _invocationArgumentArrays;
    private uint _recordCopies;
    private bool _exhausted;

    internal void AddReflectedFieldReads(uint count = 1) =>
        Add(ref _reflectedFieldReads, count);
    internal void AddReflectedMethodCalls(uint count = 1) =>
        Add(ref _reflectedMethodCalls, count);
    internal void AddStableIdReads(uint count = 1) =>
        Add(ref _stableIdReads, count);
    internal void AddListEntries(uint count = 1) =>
        Add(ref _listEntries, count);
    internal void AddInvocationArgumentArrays(uint count = 1) =>
        Add(ref _invocationArgumentArrays, count);
    internal void AddRecordCopies(uint count = 1) =>
        Add(ref _recordCopies, count);

    internal bool TrySnapshot(out ServiceCycleProfileOperations operations)
    {
        if (_exhausted)
        {
            operations = default;
            return false;
        }
        operations = new ServiceCycleProfileOperations(
            _reflectedFieldReads,
            _reflectedMethodCalls,
            _stableIdReads,
            _listEntries,
            _invocationArgumentArrays,
            _recordCopies);
        return true;
    }

    private void Add(ref uint value, uint count)
    {
        if (_exhausted) return;
        if (uint.MaxValue - value < count)
        {
            _exhausted = true;
            return;
        }
        value += count;
    }
}
#endif
