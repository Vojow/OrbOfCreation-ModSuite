using System;
using OrbModding.Common;

namespace OrbModConfig;

/// <summary>
/// Owns the successful-apply side effect. The invalidation bus and frame source
/// are supplied by the composition root so settings code never locates globals.
/// </summary>
internal sealed class ModSettingsApplyCoordinator
{
    private readonly GameplayInvalidationBus _invalidationBus;
    private readonly Func<int> _readFrame;

    public ModSettingsApplyCoordinator(
        GameplayInvalidationBus invalidationBus,
        Func<int> readFrame)
    {
        _invalidationBus = invalidationBus ?? throw new ArgumentNullException(nameof(invalidationBus));
        _readFrame = readFrame ?? throw new ArgumentNullException(nameof(readFrame));
    }

    public bool TryApply(
        ConfigEditSession session,
        out string error,
        out int publishedInvalidations)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (!session.Apply(out error, out var appliedSettings))
        {
            publishedInvalidations = 0;
            return false;
        }

        publishedInvalidations = ModConfigInvalidationPublisher.PublishAppliedSettings(
            _invalidationBus,
            _readFrame(),
            appliedSettings);
        return true;
    }
}
