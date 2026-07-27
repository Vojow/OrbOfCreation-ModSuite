using System;
using OrbModding.Common;

namespace OrbMentor;

internal sealed class MentorActionFamilyOwnership : IDisposable
{
    private static readonly AutomationActionFamily[][] Families =
    {
        new[] { AutomationActionFamily.SpellMasteryExperienceGrant },
        new[] { AutomationActionFamily.ArtifactMasteryExperienceGrant },
        new[] { AutomationActionFamily.AlchemyMasteryExperienceGrant },
    };

    private readonly ActionFamilyOwnershipRegistry _registry;
    private readonly ActionFamilyLeaseSet?[] _leases = new ActionFamilyLeaseSet?[3];
    private readonly long[] _nextRetryFrame = new long[3];

    public MentorActionFamilyOwnership(ActionFamilyOwnershipRegistry? registry = null) =>
        _registry = registry ?? ActionFamilyOwnershipRegistry.Shared;

    public bool IsHeld(MentorDomain domain) => _leases[(int)domain]?.IsHeld == true;

    public bool TryCaptureMutationPermit(MentorDomain domain) =>
        _leases[(int)domain]?.TryCaptureMutationPermit() == true;

    public void Refresh(MentorConfig config, bool lifecycleReady, long frame)
    {
        RefreshDomain(MentorDomain.Spells, lifecycleReady && config.Active, frame);
        RefreshDomain(MentorDomain.Artifacts,
            lifecycleReady && config.Active && config.ArtifactsEnabled.Value, frame);
        RefreshDomain(MentorDomain.Alchemy,
            lifecycleReady && config.Active && config.AlchemyEnabled.Value, frame);
    }

    public void ReleaseLifecycleClaims()
    {
        for (var index = _leases.Length - 1; index >= 0; index--)
        {
            _leases[index]?.Dispose();
            _leases[index] = null;
            _nextRetryFrame[index] = 0;
        }
    }

    private void RefreshDomain(MentorDomain domain, bool shouldOwn, long frame)
    {
        var index = (int)domain;
        var lease = _leases[index];
        if (lease is not null && !lease.IsHeld)
        {
            lease.Dispose();
            lease = null;
            _leases[index] = null;
        }
        if (!shouldOwn)
        {
            lease?.Dispose();
            _leases[index] = null;
            _nextRetryFrame[index] = 0;
            return;
        }
        if (lease is not null || frame < _nextRetryFrame[index]) return;
        if (_registry.TryClaimSet(
                new ActionFamilyOwner(MentorFeatureStatus.Key(domain), MentorFeatureStatus.DisplayName(domain)),
                Families[index],
                out lease,
                out _))
        {
            _leases[index] = lease;
            _nextRetryFrame[index] = 0;
        }
        else
        {
            _nextRetryFrame[index] = frame + 60;
        }
    }

    public void Dispose() => ReleaseLifecycleClaims();
}
