using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.World;
#endif

namespace OrbAutomata;

/// <summary>
/// The one Automata-owned ServiceCycle runtime. It owns the shared host and the
/// ordered feature runtimes, and drives them per Unity frame, per saved-configuration
/// publication, and per lifecycle boundary. It owns no feature policy, native adapter,
/// or typed service generic.
/// </summary>
internal sealed class AutomataServiceCycleRuntime : IAutomataServiceCycleRuntime
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly AutomataServiceCycleHost _host;
    private readonly IAutomataServiceCycleFeatureRuntime[] _features;
    private readonly ServiceConfigurationPublisher _configurationPublication;
    private ConfigGeneration _configurationGeneration;
    private readonly DiscoveryTreeOfferGameAction? _discoveryTreeOffers;
    private readonly SpellWorkbenchGameAction? _spellWorkbench;
    private readonly SpellCompositionGameAction? _spellComposition;
    private readonly SpellLoadoutGameAction? _spellLoadout;
    private readonly TargetingGameAction? _targeting;
    private readonly GenericDiscoveryGameAction? _genericDiscovery;
    private readonly EquipmentLoadoutGameAction? _equipmentLoadout;
    private readonly ChallengeGameAction? _challenges;
    private bool _disposed;
#if SERVICE_CYCLE_PROFILE
    private ulong _nextGameMcpActionIdentity;
#endif

    internal AutomataServiceCycleRuntime(
        Func<long> readLifecycleEpoch,
        ServiceConfigurationPublisher configurationPublication,
        AutomataServiceCycleHost host,
        IAutomataServiceCycleFeatureRuntime[] features,
        ConfigGeneration configurationGeneration,
        DiscoveryTreeOfferGameAction? discoveryTreeOffers = null,
        SpellWorkbenchGameAction? spellWorkbench = null,
        SpellCompositionGameAction? spellComposition = null,
        SpellLoadoutGameAction? spellLoadout = null,
        TargetingGameAction? targeting = null,
        GenericDiscoveryGameAction? genericDiscovery = null,
        EquipmentLoadoutGameAction? equipmentLoadout = null,
        ChallengeGameAction? challenges = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _configurationPublication = configurationPublication ??
            throw new ArgumentNullException(nameof(configurationPublication));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _features = features ?? throw new ArgumentNullException(nameof(features));
        _configurationGeneration = configurationGeneration;
        _discoveryTreeOffers = discoveryTreeOffers;
        _spellWorkbench = spellWorkbench;
        _spellComposition = spellComposition;
        _spellLoadout = spellLoadout;
        _targeting = targeting;
        _genericDiscovery = genericDiscovery;
        _equipmentLoadout = equipmentLoadout;
        _challenges = challenges;
    }

    internal SuiteRuntimeConfiguration CurrentConfiguration => _configurationPublication.ReadLatest().Snapshot;
    internal ConfigGeneration CurrentConfigurationGeneration =>
        _configurationPublication.ReadLatest().Generation;
    internal LifecycleGeneration CurrentLifecycle => _host.CurrentLifecycle;
    internal bool EmergencyStopEngaged => _host.EmergencyStopEngaged;

    public void Tick(float unscaledDeltaTime)
    {
        if (_disposed) return;
        var report = _host.Tick();
        var pump = _host.Pump;
        for (var index = 0; index < _features.Length; index++)
            _features[index].ObserveFrame(pump, in report);
    }

    public void PublishSavedConfiguration(
        SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration)
    {
        if (_disposed) return;
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (configurationGeneration.Value <= _configurationGeneration.Value) return;
        if (configurationGeneration != _configurationGeneration.Next())
            throw new InvalidOperationException(
                "A saved configuration generation was skipped before ServiceCycle publication.");
        var publishedGeneration = _configurationPublication.Publish(configuration);
        if (publishedGeneration != configurationGeneration)
            throw new InvalidOperationException(
                "The configuration store and ServiceCycle publication generations diverged.");
        _configurationGeneration = configurationGeneration;
        for (var index = 0; index < _features.Length; index++)
            _features[index].ObserveConfiguration(configurationGeneration);
    }

    public void CancelPreparedWork()
    {
        if (_disposed) return;
        if (!_host.EmergencyStopEngaged)
            // This cancellation is recoverable: the caller may be releasing ownership, disabling
            // automation, or synchronously engaging the configured emergency stop. Marking it as a
            // shutdown made the configuration pump correctly refuse to clear it, so RESUME could
            // never restore this runtime. The next published false reading may clear a user episode;
            // Dispose still creates the non-clearable shutdown episode below.
            _host.SetEmergencyStop(true, EmergencyStopReason.UserRequested);
    }

    public void InvalidateLifecycle()
    {
        if (_disposed) return;
        var nativeLifecycle = _readLifecycleEpoch();
        if (!_host.TryReplaceLifecycle(nativeLifecycle)) return;
        for (var index = 0; index < _features.Length; index++)
            _features[index].ObserveLifecycle(
                nativeLifecycle,
                _configurationGeneration);
        _discoveryTreeOffers?.InvalidateLifecycle();
        _spellWorkbench?.InvalidateLifecycle();
        _spellComposition?.InvalidateLifecycle();
        _spellLoadout?.InvalidateLifecycle();
        _targeting?.InvalidateLifecycle();
        _genericDiscovery?.InvalidateLifecycle();
        _equipmentLoadout?.InvalidateLifecycle();
        _challenges?.InvalidateLifecycle();
    }

#if SERVICE_CYCLE_PROFILE
    public AutomataRuntimeFrameFacts CaptureFrameFacts(bool includeServices)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataServiceCycleRuntime));
        return AutomataRuntimeFrameFacts.Capture(
            _host,
            _configurationPublication,
            includeServices);
    }

    public GameMcpCommandResult ExecuteGameMcp(GameMcpCommand command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (_disposed)
            return GameMcpCommandResult.Rejected(
                "runtime_disposed",
                "the ServiceCycle runtime has been disposed");

        var registry = _host.Pump.DiagnosticsRegistry;
        var world = registry.World.ReadLatest();
        var configuration = _configurationPublication.ReadLatest();
        var lifecycle = checked((long)_host.CurrentLifecycle.Value);
        if (GameMcpNativeActionAdmission.TryReject(
                command,
                lifecycle,
                configuration.Generation.Value,
                _host.EmergencyStopEngaged,
                out var rejection))
            return rejection;

        try
        {
            if (command.Kind == GameMcpCommandKind.DiscoveryTreeOffer)
                return ExecuteDiscoveryTreeOffer(
                    command,
                    lifecycle,
                    configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.SpellWorkbench)
                return ExecuteSpellWorkbench(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.SpellComposition)
                return ExecuteSpellComposition(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.SpellLoadout)
                return ExecuteSpellLoadout(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.Targeting)
                return ExecuteTargeting(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.Consumable)
                return ExecuteConsumable(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.Crafting)
                return ExecuteCrafting(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.GenericDiscovery)
                return ExecuteGenericDiscovery(
                    command,
                    lifecycle,
                    configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.EquipmentLoadout)
                return ExecuteEquipmentLoadout(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.Challenge)
                return ExecuteChallenge(command, lifecycle, configuration.Generation.Value);
            var service = ServiceForGameMcp(command.Kind);
            var context = CreateGameMcpContext(
                registry,
                service,
                world.Generation,
                out var ordinal);
            var config = configuration.Snapshot;
            var result = ExecuteFeature(
                command,
                config,
                world.Snapshot,
                in context,
                ordinal);
            var exactReason = ExactGameMcpReason(command, world.Snapshot, in result);
            return GameMcpCommandResult.FromAction(
                in result,
                command.Kind,
                lifecycle,
                configuration.Generation.Value,
                exactReason);
        }
        catch (GameMcpActionUnavailableException exception)
        {
            return GameMcpCommandResult.Rejected(
                exception.Code,
                exception.Message,
                lifecycle,
                configuration.Generation.Value);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                OverflowException or ObjectDisposedException)
        {
            return GameMcpCommandResult.Faulted(
                "main_thread_execution_fault",
                exception.GetBaseException().Message,
                lifecycle,
                configuration.Generation.Value);
        }
    }

    private GameMcpCommandResult ExecuteSpellComposition(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(
            command,
            command.Mode == "set_output_level" ? "IntVariable" : "Spell");
        if (_spellComposition is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared spell composition GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var kind = command.Mode == "set_output_level"
            ? SpellCompositionActionKind.SetOutputLevel
            : SpellCompositionActionKind.SetAugments;
        var stacks = new SpellCompositionGlyphStack[command.UuidCounts.Length];
        for (var index = 0; index < stacks.Length; index++)
            stacks[index] = new SpellCompositionGlyphStack(
                command.UuidCounts[index].Uuid,
                command.UuidCounts[index].Count);
        var action = new SpellCompositionAction(
            kind,
            command.TargetId,
            command.Amount,
            stacks,
            command.ExpectedLifecycleGeneration);
        var submission = _spellComposition.Submit(in action);
        var result = SpellCompositionActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpSpellCompositionProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteSpellLoadout(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "Spell");
        if (_spellLoadout is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared spell loadout GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var kind = command.Mode switch
        {
            "remove" => SpellLoadoutActionKind.Remove,
            "move" => SpellLoadoutActionKind.Move,
            _ => throw new ArgumentException("unsupported spell loadout mode " + command.Mode),
        };
        var action = new SpellLoadoutAction(
            kind,
            command.TargetId,
            command.Amount - 1,
            command.ExpectedLifecycleGeneration);
        var submission = _spellLoadout.Submit(in action);
        var result = SpellLoadoutActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpSpellLoadoutProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteTargeting(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(
            command,
            command.Mode == "submit" ? "StructureSO" : "TargetingManager+TargetLink");
        if (_targeting is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable", "the shared targeting GameAction was not composed",
                lifecycle, configurationGeneration);
        var kind = command.Mode switch
        {
            "submit" => TargetingActionKind.Submit,
            "randomize" => TargetingActionKind.Randomize,
            "cancel" => TargetingActionKind.Cancel,
            _ => throw new ArgumentException("unsupported targeting mode " + command.Mode),
        };
        var action = new TargetingAction(kind, command.TargetId, command.ExpectedLifecycleGeneration);
        var submission = _targeting.Submit(in action);
        var result = TargetingActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result, command.Kind, lifecycle, configurationGeneration,
            submission.Reason, GameMcpTargetingProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteConsumable(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "ConsumableSO");
        var feature = FindFeature(command.Kind);
        var kind = command.Mode switch
        {
            "use" => ConsumablePlayerActionKind.Use,
            "cancel" => ConsumablePlayerActionKind.Cancel,
            "discard" => ConsumablePlayerActionKind.Discard,
            "set_randomization" => ConsumablePlayerActionKind.SetRandomization,
            "move" => ConsumablePlayerActionKind.Move,
            _ => throw new ArgumentException("unsupported consumable mode " + command.Mode),
        };
        var list = command.PayloadKey switch
        {
            "inventory" => ConsumablePlayerListKind.Inventory,
            "hotbar" => ConsumablePlayerListKind.Hotbar,
            _ => ConsumablePlayerListKind.None,
        };
        var randomized = string.Equals(
            command.PayloadValue,
            "true",
            StringComparison.Ordinal);
        var action = new ConsumablePlayerAction(
            kind,
            command.TargetId,
            command.ExpectedLifecycleGeneration,
            command.Amount,
            randomized,
            list,
            kind == ConsumablePlayerActionKind.Move ? command.Amount - 1 : -1);
        var submission = ((AutoItemsFeatureRuntime)feature).TryExecuteGameMcp(in action);
        var result = ConsumablePlayerActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpConsumableProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteCrafting(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "CraftingRecipeSO");
        var feature = FindFeature(command.Kind);
        var action = new CraftingPlayerAction(
            command.TargetId,
            command.ExpectedLifecycleGeneration);
        var submission = ((AutoScribeServiceCycleFeature.Runtime)feature)
            .TryExecuteGameMcp(in action);
        var result = CraftingPlayerActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpCraftingProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteGenericDiscovery(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_genericDiscovery is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared generic discovery GameAction was not composed",
                lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, command.DerivedNativeType);
        var action = new GenericDiscoveryAction(
            command.TargetId,
            command.DerivedNativeType,
            command.ExpectedLifecycleGeneration);
        var submission = _genericDiscovery.Submit(in action);
        var result = GenericDiscoveryActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpGenericDiscoveryProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteEquipmentLoadout(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_equipmentLoadout is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared equipment loadout GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "EquipmentSO");
        var kind = command.Mode == "equip"
            ? EquipmentLoadoutActionKind.Equip
            : EquipmentLoadoutActionKind.Unequip;
        var action = new EquipmentLoadoutAction(kind, command.TargetId,
            command.ExpectedLifecycleGeneration);
        var submission = _equipmentLoadout.Submit(in action);
        var result = EquipmentLoadoutActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason,
            GameMcpEquipmentLoadoutProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteChallenge(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_challenges is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared challenge GameAction was not composed", lifecycle,
                configurationGeneration);
        if (command.TargetId != Guid.Empty)
            GameMcpNativeActionAdmission.AssertNativeType(command, "ChallengeSO");
        var kind = command.Mode switch
        {
            "select" => ChallengeActionKind.Select,
            "queue" => ChallengeActionKind.Queue,
            "abandon" => ChallengeActionKind.Abandon,
            "fetch_time" => ChallengeActionKind.FetchTime,
            "fetch_prestige" => ChallengeActionKind.FetchPrestige,
            _ => throw new ArgumentException("unsupported challenge mode " + command.Mode),
        };
        var action = new ChallengeAction(kind, command.TargetId,
            command.ExpectedLifecycleGeneration);
        var submission = _challenges.Submit(in action);
        var result = ChallengeActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason,
            GameMcpChallengeProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteSpellWorkbench(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "SpellRecipeSO");
        if (_spellWorkbench is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared spell workbench GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var kind = command.Mode switch
        {
            "select" => SpellWorkbenchActionKind.Select,
            "discover" => SpellWorkbenchActionKind.Discover,
            "create" => SpellWorkbenchActionKind.Create,
            _ => throw new ArgumentException("unsupported spell workbench mode " + command.Mode),
        };
        var action = new SpellWorkbenchAction(
            kind,
            command.TargetId,
            command.ExpectedLifecycleGeneration);
        var submission = _spellWorkbench.Submit(in action);
        var result = SpellWorkbenchActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpSpellWorkbenchProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteDiscoveryTreeOffer(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "DiscoveryTreeSO");
        if (_discoveryTreeOffers is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared Discovery Tree offer GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var kind = command.Mode switch
        {
            "initiate" => DiscoveryTreeOfferActionKind.Initiate,
            "select" => DiscoveryTreeOfferActionKind.Select,
            "confirm" => DiscoveryTreeOfferActionKind.Confirm,
            "reroll" => DiscoveryTreeOfferActionKind.Reroll,
            _ => throw new ArgumentException("unsupported Discovery Tree offer mode " + command.Mode),
        };
        var action = new DiscoveryTreeOfferAction(
            kind,
            command.TargetId,
            command.SecondaryId,
            command.ExpectedLifecycleGeneration);
        var submission = _discoveryTreeOffers.Submit(in action);
        var result = DiscoveryTreeOfferActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpDiscoveryTreeOfferProjection.Project(kind, in submission));
    }

    private static string? ExactGameMcpReason(
        GameMcpCommand command,
        GameWorldState world,
        in ServiceActionResult result)
    {
        if (command.Kind == GameMcpCommandKind.Harvest &&
            result.Disposition == ServiceActionDisposition.Rejected &&
            result.Code == CommonActionResultCodes.PolicyRejected)
        {
            var pair = command.Mode == "fruit_tree"
                ? AutoHarvestPair.FruitTree
                : AutoHarvestPair.TreasureTree;
            var expected = AutoHarvestPairAuthoring.For(pair);
            var facts = AutoHarvestWorldFacts.For(
                world,
                expected.PlotId,
                expected.ActionId);
            var harvestEvidenceReason =
                HarvestPrerequisiteEvidenceReason(command.Mode, facts.Prerequisites);
            if (harvestEvidenceReason is not null) return harvestEvidenceReason;
        }

        if (command.Kind != GameMcpCommandKind.Purchase ||
            !string.Equals(command.Mode, "upgrade", StringComparison.Ordinal) ||
            result.Disposition != ServiceActionDisposition.Skipped ||
            !WorldLookup.TryFind(world.Upgrades, command.TargetId, out var upgrade) ||
            upgrade.Reading.CachedCostLevel == upgrade.Reading.Level)
        {
            return null;
        }

        return "the native upgrade purchase was skipped while published UpgradeSO.cachedCostLevel " +
            upgrade.Reading.CachedCostLevel + " disagreed with purchase level " +
            upgrade.Reading.Level + "; the game's upstream value cache stays stale until the " +
            "corresponding screen is viewed";
    }

    internal static string? HarvestPrerequisiteEvidenceReason(
        string mode,
        PlotActionPrerequisiteEvidence prerequisites) =>
        prerequisites == PlotActionPrerequisiteEvidence.Unknown
            ? "the native " + mode +
              " harvest was rejected because no plot-action prerequisite latch evidence was " +
              "published for an exact current action instance"
            : null;

    private sealed class GameMcpActionUnavailableException : Exception
    {
        internal GameMcpActionUnavailableException(string code, string message)
            : base(message)
        {
            Code = string.IsNullOrWhiteSpace(code)
                ? throw new ArgumentException("An MCP rejection code is required.", nameof(code))
                : code;
        }

        internal string Code { get; }
    }

    private ServiceActionResult ExecuteFeature(
        GameMcpCommand command,
        SuiteRuntimeConfiguration config,
        GameWorldState world,
        in ServiceActionContext context,
        int serviceOrdinal)
    {
        var feature = FindFeature(command.Kind);
        switch (command.Kind)
        {
            case GameMcpCommandKind.Purchase:
            {
                var kind = command.Mode == "structure"
                    ? AutoBuyCandidateKind.Structure
                    : AutoBuyCandidateKind.Upgrade;
                var expected = kind == AutoBuyCandidateKind.Structure
                    ? "StructureSO"
                    : "UpgradeSO";
                GameMcpNativeActionAdmission.AssertNativeType(command, expected);
                var action = new AutoBuyCycleAction(
                    kind,
                    command.TargetId,
                    world.CollectedAtEpoch,
                    command.Amount);
                return ((AutoBuyFeatureRuntime)feature).TryExecuteGameMcp(
                    in action, in config, in context);
            }
            case GameMcpCommandKind.Cast:
            {
                GameMcpNativeActionAdmission.AssertNativeType(command, "SpellRecipeSO");
                var kind = command.Mode == "release"
                    ? AutoCastActionKind.ReleaseCharge
                    : AutoCastActionKind.Fire;
                var action = new AutoCastCycleAction(
                    kind,
                    checked(command.Amount - 1),
                    command.TargetId,
                    world.CollectedAtEpoch);
                return ((AutoCastFeatureRuntime)feature).TryExecuteGameMcp(
                    in action, in config, in context);
            }
            case GameMcpCommandKind.Concept:
            {
                GameMcpNativeActionAdmission.AssertNativeType(command, "AlchemyRecipeSO");
                var kind = command.Mode switch
                {
                    "add" => AutoConceptActionKind.Add,
                    "remove_owned" => AutoConceptActionKind.RemoveOwned,
                    "rotate_out" => AutoConceptActionKind.RotateOut,
                    _ => throw new ArgumentException("unsupported concept mode " + command.Mode),
                };
                if (!AutoConceptPlanBeliefProjection.TryCreate(
                        world,
                        command.TargetId,
                        out var belief,
                        out var reason))
                {
                    throw new GameMcpActionUnavailableException(
                        "concept_world_facts_unavailable",
                        reason);
                }
                if (!AutoConceptPlanBeliefProjection.TryResolveGameMcpTarget(
                        kind,
                        command.Amount,
                        in belief,
                        out var targetOrDelta,
                        out var code,
                        out reason))
                {
                    throw new GameMcpActionUnavailableException(
                        code,
                        reason);
                }
                var action = new AutoConceptCycleAction(
                    kind,
                    command.TargetId,
                    targetOrDelta,
                    command.SecondaryId,
                    world.CollectedAtEpoch,
                    in belief);
                return ((AutoConceptFeatureRuntime)feature).TryExecuteGameMcp(
                    in action, in config, in context);
            }
            case GameMcpCommandKind.Harvest:
            {
                GameMcpNativeActionAdmission.AssertNativeType(command, "PlotNodeSO");
                var pair = command.Mode == "fruit_tree"
                    ? AutoHarvestPair.FruitTree
                    : AutoHarvestPair.TreasureTree;
                var expected = AutoHarvestPairAuthoring.For(pair);
                if (command.TargetId != expected.PlotId)
                {
                    throw new GameMcpActionUnavailableException(
                        "harvest_target_mismatch",
                        "the server-derived " + command.Mode + " harvest pair requires plot " +
                        expected.PlotId.ToString("D") + ", not " +
                        command.TargetId.ToString("D"));
                }
                var action = new AutoHarvestCycleAction(
                    pair,
                    AutoHarvestWorldFacts.For(world, expected.PlotId, expected.ActionId),
                    AutoHarvestActionSafety.For(world, in expected));
                return ((AutoHarvestFeatureRuntime)feature).TryExecuteGameMcp(
                    in action, in config, in context);
            }
            case GameMcpCommandKind.SpellLevel:
            {
                GameMcpNativeActionAdmission.AssertNativeType(command, "SpellRecipeSO");
                var kind = command.Mode == "all"
                    ? SpellLevelActionKind.All
                    : SpellLevelActionKind.Single;
                var action = new SpellLevelCycleAction(
                    kind,
                    command.TargetId,
                    world.CollectedAtEpoch);
                return ((SpellLevelFeatureRuntime)feature).TryExecuteGameMcp(
                    in action, in config, in context);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(command.Kind));
        }
    }

    private IAutomataServiceCycleFeatureRuntime FindFeature(GameMcpCommandKind kind)
    {
        for (var index = 0; index < _features.Length; index++)
        {
            var feature = _features[index];
            if ((kind == GameMcpCommandKind.Purchase && feature is AutoBuyFeatureRuntime ||
                kind == GameMcpCommandKind.Cast && feature is AutoCastFeatureRuntime ||
                kind == GameMcpCommandKind.Concept && feature is AutoConceptFeatureRuntime ||
                kind == GameMcpCommandKind.Harvest && feature is AutoHarvestFeatureRuntime ||
                kind == GameMcpCommandKind.SpellLevel && feature is SpellLevelFeatureRuntime) ||
                kind == GameMcpCommandKind.Consumable && feature is AutoItemsFeatureRuntime ||
                kind == GameMcpCommandKind.Crafting &&
                    feature is AutoScribeServiceCycleFeature.Runtime)
                return feature;
        }
        throw new InvalidOperationException(
            "the requested feature runtime is not registered in this ServiceCycle");
    }

    private ServiceActionContext CreateGameMcpContext(
        OrbModding.Common.Runtime.ServiceCycle.Registration.ServiceCycleRegistry registry,
        ServiceId service,
        WorldGeneration world,
        out int serviceOrdinal)
    {
        serviceOrdinal = -1;
        for (var ordinal = 0; ordinal < registry.OrdinalCount; ordinal++)
        {
            if (registry.GetServiceId(ordinal) != service) continue;
            serviceOrdinal = ordinal;
            break;
        }
        if (serviceOrdinal < 0)
            throw new InvalidOperationException(
                "service " + service.Value + " is not registered");

        var sequence = checked(++_nextGameMcpActionIdentity);
        var identity = new ServiceCycleIdentity(
            service,
            _host.CurrentLifecycle,
            _configurationPublication.ReadLatest().Generation,
            registry.Strategy.ReadLatest().Generation,
            world,
            new CycleId(sequence));
        var coordinates = new ServiceCycleProfileCoordinates(
            serviceOrdinal,
            Math.Max(0, _host.Pump.LastAcceptedFrameIdentity));
        return new ServiceActionContext(
            identity,
            new BatchId(sequence),
            new ActionId(sequence),
            0,
            registry.Clock.Now,
            in coordinates);
    }

    internal static ServiceId ServiceForGameMcp(GameMcpCommandKind kind) => kind switch
    {
        GameMcpCommandKind.Purchase => AutoBuyServicePolicies.ServiceId,
        GameMcpCommandKind.Cast => AutoCastServicePolicies.ServiceId,
        GameMcpCommandKind.Concept => AutoConceptServicePolicies.ServiceId,
        GameMcpCommandKind.Harvest => AutoHarvestServicePolicies.ServiceId,
        GameMcpCommandKind.SpellLevel => SpellLevelServicePolicies.ServiceId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

#endif

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _discoveryTreeOffers?.Dispose();
            _spellWorkbench?.Dispose();
            _spellComposition?.Dispose();
            _spellLoadout?.Dispose();
            _targeting?.Dispose();
            _genericDiscovery?.Dispose();
            _equipmentLoadout?.Dispose();
            _challenges?.Dispose();
            if (!_host.EmergencyStopEngaged)
                _host.SetEmergencyStop(true, EmergencyStopReason.SuiteShutdown);
        }
        finally
        {
            try
            {
                for (var index = 0; index < _features.Length; index++)
                    _features[index].DisposeDiagnostics();
            }
            finally
            {
                try
                {
                    _host.Shutdown();
                }
                finally
                {
                    for (var index = 0; index < _features.Length; index++)
                        _features[index].DisposeRegistration();
                }
            }
        }
    }
}
