using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

internal sealed partial class ServiceResourceClaimLedger
{
    internal void EndFactory(ServiceResourceClaim claim)
    {
        if (claim is null) throw new ArgumentNullException(nameof(claim));
        BeginFactoryClose(claim);
        CompleteFactoryClose(claim);
    }

    internal void BeginFactoryClose(ServiceResourceClaim claim)
    {
        if (claim is null) throw new ArgumentNullException(nameof(claim));
        if (!ReferenceEquals(
                Volatile.Read(ref _activeFactoryToken),
                claim))
        {
            throw new InvalidOperationException(
                "Only the exact live factory token can close.");
        }
        if (!claim.MarkFactoryClosing())
            throw new InvalidOperationException(
                "Only an open exact factory token can close.");
    }

    internal void CompleteFactoryClose(ServiceResourceClaim claim)
    {
        if (claim is null) throw new ArgumentNullException(nameof(claim));
        if (!ReferenceEquals(
                Volatile.Read(ref _activeFactoryToken),
                claim) ||
            !claim.IsFactoryClosing)
        {
            throw new InvalidOperationException(
                "Only the exact closing factory token can complete.");
        }
        if (claim.IsReserved) ReleaseReservation(claim);
        SweepRetiredClaims();
        if (!ReferenceEquals(
                Interlocked.CompareExchange(
                    ref _activeFactoryToken,
                    null,
                    claim),
                claim))
        {
            throw new InvalidOperationException(
                "Only the exact live factory token can be cleared.");
        }
    }

    internal bool Release(ServiceResourceClaim? claim)
    {
        if (claim is null ||
            (uint)claim.SlotIndex >= (uint)_claims.Length)
            return false;
        if (!ReferenceEquals(
                Volatile.Read(ref _claims[claim.SlotIndex]),
                claim))
            return false;
        if (claim.IsReserved) return ReleaseReservation(claim);
        if (!claim.MarkRetired() && !claim.IsRetired) return false;
        var activeFactory = Volatile.Read(ref _activeFactoryToken);
        if (activeFactory is not null && activeFactory.IsFactoryOpen) return true;
        ReleaseRetired(claim);
        return true;
    }

    private bool ReleaseReservation(ServiceResourceClaim claim)
    {
        if (!ReferenceEquals(
                Interlocked.CompareExchange(
                    ref _claims[claim.SlotIndex],
                    null,
                    claim),
                claim))
            return false;
        claim.ClearIdentity();
        return true;
    }

    private void SweepRetiredClaims()
    {
        for (var index = 0; index < _claims.Length; index++)
        {
            var claim = Volatile.Read(ref _claims[index]);
            if (claim?.IsRetired == true) ReleaseRetired(claim);
        }
    }

    private void ReleaseRetired(ServiceResourceClaim claim)
    {
        if (!claim.IsRetired) return;
        ReleaseReservation(claim);
    }
}
