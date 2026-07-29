using System;
using System.Collections.Generic;
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
        => TryApplyCore(session, selectedMod: null, out error, out publishedInvalidations);

    public bool TryApply(
        ConfigEditSession session,
        ModConfigDescriptor selectedMod,
        out string error,
        out int publishedInvalidations)
        => TryApplyCore(session, selectedMod, out error, out publishedInvalidations);

    private bool TryApplyCore(
        ConfigEditSession session,
        ModConfigDescriptor? selectedMod,
        out string error,
        out int publishedInvalidations)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        IReadOnlyList<ConfigSettingDescriptor> appliedSettings;
        var applied = selectedMod is null
            ? session.Apply(out error, out appliedSettings)
            : session.Apply(selectedMod, out error, out appliedSettings);
        if (!applied)
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
