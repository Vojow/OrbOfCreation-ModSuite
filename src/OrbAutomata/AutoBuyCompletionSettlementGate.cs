namespace OrbAutomata;

internal sealed class AutoBuyCompletionSettlementGate
{
    private bool _pending;

    public void Notify() => _pending = true;

    public bool TryBegin(bool settlementInProgress)
    {
        if (!_pending || settlementInProgress)
        {
            return false;
        }

        _pending = false;
        return true;
    }

    public void Clear() => _pending = false;
}
