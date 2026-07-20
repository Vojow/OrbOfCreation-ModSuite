using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using BepInEx.Logging;
using OrbModding.Common;
using OrbMentor;
using Xunit;

namespace OrbModding.Tests;

public sealed class MentorDomainUnlockTests : IDisposable
{
    public MentorDomainUnlockTests() => IdScriptableObject.RuntimeLookup.Clear();
    public void Dispose() => IdScriptableObject.RuntimeLookup.Clear();

    [Fact]
    public void ExactNativeViewsGateMixedDomainsIndependently()
    {
        Register(MentorDomainUnlockGate.MasteriesEnabledUuid, available: true);
        Register(MentorDomainUnlockGate.SpellbookUuid, available: true);
        Register(MentorDomainUnlockGate.ArtifactWorkshopUuid, available: false);
        Register(MentorDomainUnlockGate.AlchemyScreenUuid, available: true);
        var gate = Gate();

        Assert.Equal(MentorDomainUnlockState.Unlocked, gate.Evaluate(MentorDomain.Spells).State);
        var artifact = gate.Evaluate(MentorDomain.Artifacts);
        Assert.Equal(MentorDomainUnlockState.Waiting, artifact.State);
        Assert.Equal("native artifact progression is locked", artifact.Reason);
        Assert.Equal(MentorDomainUnlockState.Unlocked, gate.Evaluate(MentorDomain.Alchemy).State);
    }

    [Fact]
    public void GlobalMasteryLockKeepsEveryDomainWaitingWithoutCatalogWork()
    {
        RegisterAll(masteriesAvailable: false);
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());
        config.Mode.Value = MentorOperationMode.Active;
        config.ArtifactsEnabled.Value = true;
        config.AlchemyEnabled.Value = true;
        using var runtime = new MentorRuntime(config, new ManualLogSource(), unlockGate: Gate());

        runtime.LateTick();

        Assert.False(runtime.IsBlocked);
        Assert.True(runtime.IsWaiting);
        Assert.All(new[] { MentorDomain.Spells, MentorDomain.Artifacts, MentorDomain.Alchemy }, domain =>
        {
            Assert.Equal(MentorDomainUnlockState.Waiting, runtime.DomainUnlock(domain).State);
            Assert.Equal("native mastery progression is locked", runtime.DomainUnlock(domain).Reason);
        });
        Assert.Contains("Waiting: native mastery progression is locked", runtime.CurrentMentor(MentorDomain.Spells));
        Assert.Contains("Spells waiting: native mastery progression is locked", runtime.StatusText());
        Assert.True(config.ArtifactsEnabled.Value);
        Assert.True(config.AlchemyEnabled.Value);
    }

    [Fact]
    public void LifecycleResetCancelsCachedUnlockStateAndReevaluatesNativeViews()
    {
        var mastery = RegisterAll(masteriesAvailable: true);
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());
        config.Mode.Value = MentorOperationMode.Active;
        var now = 0L;
        using var runtime = new MentorRuntime(
            config,
            new ManualLogSource(),
            unlockGate: Gate(),
            readTimestamp: () => now);

        runtime.LateTick();
        Assert.All(new[] { MentorDomain.Spells, MentorDomain.Artifacts, MentorDomain.Alchemy },
            domain => Assert.True(runtime.DomainUnlock(domain).IsUnlocked));

        mastery.available = false;
        runtime.RequestLifecycleReset();
        runtime.LateTick();
        Assert.All(new[] { MentorDomain.Spells, MentorDomain.Artifacts, MentorDomain.Alchemy }, domain =>
        {
            Assert.False(runtime.DomainUnlock(domain).IsUnlocked, runtime.DomainUnlock(domain).Reason);
            Assert.Equal("native mastery progression is locked", runtime.DomainUnlock(domain).Reason);
        });

        mastery.available = true;
        now += Stopwatch.Frequency;
        runtime.LateTick();
        Assert.All(new[] { MentorDomain.Spells, MentorDomain.Artifacts, MentorDomain.Alchemy },
            domain => Assert.True(runtime.DomainUnlock(domain).IsUnlocked));
    }

    [Fact]
    public void SpellUnlockContractFailureDoesNotBlockOtherDomains()
    {
        RegisterAll(masteriesAvailable: true);
        RegisterWrongType(MentorDomainUnlockGate.SpellbookUuid);
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());
        config.Mode.Value = MentorOperationMode.Active;
        config.ArtifactsEnabled.Value = true;
        config.AlchemyEnabled.Value = true;
        using var runtime = new MentorRuntime(config, new ManualLogSource(), unlockGate: Gate());

        runtime.LateTick();

        Assert.False(runtime.IsBlocked);
        Assert.True(runtime.DomainUnlock(MentorDomain.Spells).IsContractBlocked);
        Assert.Contains("Blocked:", runtime.CurrentMentor(MentorDomain.Spells));
        Assert.True(runtime.DomainUnlock(MentorDomain.Artifacts).IsUnlocked);
        Assert.True(runtime.DomainUnlock(MentorDomain.Alchemy).IsUnlocked);
    }

    [Fact]
    public void MissingRegistrationWaitsButUuidTypeContradictionFailsClosed()
    {
        Register(MentorDomainUnlockGate.MasteriesEnabledUuid, available: true);
        var gate = Gate();

        var missing = gate.Evaluate(MentorDomain.Spells);
        Assert.Equal(MentorDomainUnlockState.Waiting, missing.State);
        Assert.Contains("has not registered", missing.Reason);

        RegisterWrongType(MentorDomainUnlockGate.SpellbookUuid);
        var contradiction = gate.Evaluate(MentorDomain.Spells);
        Assert.Equal(MentorDomainUnlockState.ContractBlocked, contradiction.State);
        Assert.Contains("Status=WrongType", contradiction.Reason);
        Assert.Contains("ExpectedType=ViewSO", contradiction.Reason);
    }

    [Fact]
    public void AuditedUnlockUuidsMatchSerializedViewMappings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "entity-mappings.tsv");
        var byId = File.ReadAllLines(path).Skip(1)
            .Select(line => line.Split('\t'))
            .ToDictionary(parts => parts[0], parts => (Name: parts[1], Type: parts[2]), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(("MasteriesEnabled", "ViewSO"), byId[MentorDomainUnlockGate.MasteriesEnabledUuid]);
        Assert.Equal(("MagicSpellbook", "ViewSO"), byId[MentorDomainUnlockGate.SpellbookUuid]);
        Assert.Equal(("WorkshopArtifact", "ViewSO"), byId[MentorDomainUnlockGate.ArtifactWorkshopUuid]);
        Assert.Equal(("ScreenAlchemy", "ViewSO"), byId[MentorDomainUnlockGate.AlchemyScreenUuid]);
    }

    [Fact]
    public void FeatureStatusSeparatesConfigurationAndPublishesOnlyConditionTransitions()
    {
        RegisterAll(masteriesAvailable: true);
        var registry = new FeatureStatusRegistry();
        var transitions = new List<FeatureStatusTransition>();
        registry.Transitioned += transitions.Add;
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());
        using var runtime = new MentorRuntime(
            config,
            new ManualLogSource(),
            unlockGate: Gate(),
            featureStatusRegistry: registry);

        Assert.Equal(4, transitions.Count);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled,
            Status(registry, MentorFeatureStatus.RootFeatureId).State);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked,
            Status(registry, MentorFeatureStatus.SpellsFeatureId).State);
        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled,
            Status(registry, MentorFeatureStatus.SpellsFeatureId).Reason.Code);

        runtime.RefreshFeatureStatus();
        runtime.RefreshFeatureStatus();
        Assert.Equal(4, transitions.Count);

        config.Mode.Value = MentorOperationMode.Active;
        runtime.RefreshFeatureStatus();
        Assert.Equal(6, transitions.Count);
        var root = Status(registry, MentorFeatureStatus.RootFeatureId);
        Assert.True(root.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.NotReady, root.State);

        runtime.RefreshFeatureStatus();
        Assert.Equal(6, transitions.Count);
    }

    [Fact]
    public void StableFeatureStatusRefreshSkipsProjectionAndTransitions()
    {
        RegisterAll(masteriesAvailable: true);
        var registry = new FeatureStatusRegistry();
        var transitions = new List<FeatureStatusTransition>();
        registry.Transitioned += transitions.Add;
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());
        using var runtime = new MentorRuntime(
            config,
            new ManualLogSource(),
            unlockGate: Gate(),
            featureStatusRegistry: registry);
        var projectionCount = runtime.FeatureStatusProjectionCount;
        var transitionCount = transitions.Count;

        for (var refresh = 0; refresh < 1_000; refresh++)
            runtime.RefreshFeatureStatus();

        Assert.Equal(projectionCount, runtime.FeatureStatusProjectionCount);
        Assert.Equal(transitionCount, transitions.Count);
    }

    [Fact]
    public void LaterDuplicateStatusRegistrationRollsBackEarlierMentorKeys()
    {
        RegisterAll(masteriesAvailable: true);
        var registry = new FeatureStatusRegistry();
        using var existingArtifact = registry.Register(new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.MentorGuid, MentorFeatureStatus.ArtifactsFeatureId),
            "Existing artifact owner",
            false,
            FeatureStatusState.ConfigurationDisabled,
            new FeatureStatusReason(
                FeatureStatusReasonCode.ConfigurationDisabled,
                "existing fixture owner")));
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());

        Assert.Throws<InvalidOperationException>(() => new MentorRuntime(
            config,
            new ManualLogSource(),
            unlockGate: Gate(),
            featureStatusRegistry: registry));

        var remaining = Assert.Single(registry.GetSnapshot());
        Assert.Equal(MentorFeatureStatus.ArtifactsFeatureId, remaining.Key.FeatureId);
        Assert.Equal("Existing artifact owner", remaining.DisplayName);
        Assert.False(registry.TryGet(
            new FeatureStatusKey(PluginIds.MentorGuid, MentorFeatureStatus.SpellsFeatureId),
            out _));
        Assert.False(registry.TryGet(
            new FeatureStatusKey(PluginIds.MentorGuid, MentorFeatureStatus.RootFeatureId),
            out _));
    }

    [Fact]
    public void UnlockResetAndRecoveryFlowThroughProjectedRuntimeStatus()
    {
        var mastery = RegisterAll(masteriesAvailable: true);
        var registry = new FeatureStatusRegistry();
        var transitions = new List<FeatureStatusTransition>();
        registry.Transitioned += transitions.Add;
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());
        config.Mode.Value = MentorOperationMode.Active;
        var now = 0L;
        using var runtime = new MentorRuntime(
            config,
            new ManualLogSource(),
            unlockGate: Gate(),
            readTimestamp: () => now,
            featureStatusRegistry: registry);

        Drive(runtime, 20);
        Assert.Equal(FeatureStatusState.Operational,
            Status(registry, MentorFeatureStatus.RootFeatureId).State);

        mastery.available = false;
        now += Stopwatch.Frequency;
        runtime.LateTick();
        Assert.Equal(FeatureStatusState.Locked,
            Status(registry, MentorFeatureStatus.SpellsFeatureId).State);

        runtime.RequestLifecycleReset();
        runtime.LateTick();
        Assert.Contains(transitions, transition =>
            transition.Current is { } current &&
            current.Key.FeatureId == MentorFeatureStatus.SpellsFeatureId &&
            current.Reason.Code == FeatureStatusReasonCode.LifecycleTransition);

        mastery.available = true;
        now += Stopwatch.Frequency;
        Drive(runtime, 20);
        Assert.Equal(FeatureStatusState.Operational,
            Status(registry, MentorFeatureStatus.SpellsFeatureId).State);
        Assert.Equal(FeatureStatusState.Operational,
            Status(registry, MentorFeatureStatus.RootFeatureId).State);
    }

    [Fact]
    public void OptionalDomainContractFailureDegradesRootWithoutBlockingSibling()
    {
        RegisterAll(masteriesAvailable: true);
        var registry = new FeatureStatusRegistry();
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());
        config.Mode.Value = MentorOperationMode.Active;
        config.ArtifactsEnabled.Value = true;
        using var runtime = new MentorRuntime(
            config,
            new ManualLogSource(),
            unlockGate: Gate(),
            featureStatusRegistry: registry);

        Drive(runtime, 20);
        Assert.Equal(FeatureStatusState.Operational,
            Status(registry, MentorFeatureStatus.SpellsFeatureId).State);
        Assert.Equal(FeatureStatusState.Operational,
            Status(registry, MentorFeatureStatus.ArtifactsFeatureId).State);

        runtime.QuarantineDomain(MentorDomain.Artifacts, "artifact fixture contract unavailable");

        Assert.Equal(FeatureStatusState.ContractUnavailable,
            Status(registry, MentorFeatureStatus.ArtifactsFeatureId).State);
        Assert.Equal(FeatureStatusState.Operational,
            Status(registry, MentorFeatureStatus.SpellsFeatureId).State);
        var root = Status(registry, MentorFeatureStatus.RootFeatureId);
        Assert.Equal(FeatureStatusState.Degraded, root.State);
        Assert.Equal(FeatureStatusReasonCode.PartialCapabilityUnavailable, root.Reason.Code);
    }

    private static FeatureStatusSnapshot Status(FeatureStatusRegistry registry, string featureId)
    {
        Assert.True(registry.TryGet(new FeatureStatusKey(PluginIds.MentorGuid, featureId), out var status));
        return status;
    }

    private static void Drive(MentorRuntime runtime, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++) runtime.LateTick();
    }

    private static void RegisterWrongType(string uuid)
    {
        var id = new Guid(uuid);
        var wrongType = new AlchemyRecipeListVariable();
        wrongType.SetGuid(id);
        IdScriptableObject.RuntimeLookup[id] = wrongType;
    }

    private static MentorDomainUnlockGate Gate() => new(name => name switch
    {
        "ViewSO" => typeof(ViewSO),
        "IdScriptableObject" => typeof(IdScriptableObject),
        _ => null,
    });

    private static ViewSO Register(string uuid, bool available)
    {
        var view = new ViewSO { uuid = new Guid(uuid), available = available };
        IdScriptableObject.RuntimeLookup[view.uuid] = view;
        return view;
    }

    private static ViewSO RegisterAll(bool masteriesAvailable)
    {
        var mastery = Register(MentorDomainUnlockGate.MasteriesEnabledUuid, masteriesAvailable);
        Register(MentorDomainUnlockGate.SpellbookUuid, available: true);
        Register(MentorDomainUnlockGate.ArtifactWorkshopUuid, available: true);
        Register(MentorDomainUnlockGate.AlchemyScreenUuid, available: true);
        return mastery;
    }
}
