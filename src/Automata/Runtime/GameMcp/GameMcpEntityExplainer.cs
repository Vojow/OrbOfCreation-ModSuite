#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>Evaluated, one-publication explanation for one stable entity identity.</summary>
internal static class GameMcpEntityExplainer
{
    private const int MaximumRequirementExpansionDepth = 32;

    internal static JObject Explain(GameMcpFrameContext state, string uuidText)
    {
        if (!Guid.TryParseExact(uuidText ?? string.Empty, "D", out var uuid) || uuid == Guid.Empty)
        {
            return GameMcpWorldQuery.NotAvailableWithoutWorld(
                state,
                "invalid_uuid",
                "uuid must be a non-empty canonical D-format GUID");
        }
        var publication = state.World;
        if (publication is null || publication.Generation.Value <= 1 ||
            publication.Snapshot.CollectedAtUtcTicks <= 0)
        {
            return GameMcpWorldQuery.NotAvailableWithoutWorld(
                state,
                "world_not_published",
                state.RuntimeNotAvailableReason.Length == 0
                    ? "the world collector has not published a captured world yet"
                    : state.RuntimeNotAvailableReason);
        }

        var world = publication.Snapshot;
        if (!TryResolve(world, uuid, out var kind, out var row, out var nativeType))
        {
            var known = world.EntityIdentities.TryGet(uuid, out var identity);
            var code = known ? "not_world_projected" : "uuid_unknown";
            var reason = known
                ? "this known entity has no explainable published world row"
                : "nothing in this process knows this UUID; search entity_catalog by name";
            var remedy = new JObject { ["tool"] = "entity_catalog" };
            if (known && GameMcpEntityCapabilityMap.TryCategoryForNativeType(
                    identity.RuntimeType,
                    out var knownCategory))
            {
                remedy["tool"] = "world_get";
                remedy["category"] = knownCategory;
            }
            return GameMcpWorldQuery.WithEnvelope(state, new JObject
            {
                ["status"] = "not_available",
                ["code"] = code,
                ["reason"] = reason,
                ["uuid"] = uuid.ToString("D"),
                ["readWith"] = remedy,
            });
        }

        var predicates = Predicates(world, uuid, kind);
        var requirements = Requirements(
            world, uuid, kind, out var parityFailure, out var parityFailureCode);
        var result = new JObject
        {
            ["status"] = parityFailure is null ? "available" : "not_available",
        };
        var description = TryReadNativeDescription(uuid, nativeType);
        if (description.Length > 0) result["description"] = description;
        result.CopyFrom(GameMcpEntityCatalog.Lookup(world.EntityIdentities, uuid));
        result["kind"] = kind.ToString();
        result["state"] = GameMcpEntityCapabilityMap.TryCategoryForNativeType(
            nativeType,
            out var category)
                ? GameMcpWorldQuery.ProjectEntityState(world, category, row)
                : new GameMcpDomainValue(row);
        if (predicates.Count > 0) result["predicates"] = predicates;
        if (requirements is not null) result["requirements"] = requirements;
        var researchThresholds = ResearchThresholds(world, uuid, kind);
        if (researchThresholds is not null) result["researchThresholds"] = researchThresholds;
        var purchase = Purchase(world, uuid, kind);
        if (purchase is not null) result["purchase"] = purchase;
        var blockers = Blockers(world, uuid, kind);
        if (blockers.Count > 0) result["blockers"] = blockers;
        if (parityFailure is not null)
        {
            result["code"] = parityFailureCode;
            result["reason"] = parityFailure;
        }
        return GameMcpWorldQuery.WithEnvelope(state, result);
    }

    private static string TryReadNativeDescription(Guid uuid, string nativeType)
    {
        try
        {
            var type = Type.GetType(nativeType + ", Assembly-CSharp", throwOnError: false);
            if (type is null) return string.Empty;
            var resolved = TypedRegistryResolver.Shared.Resolve(uuid, type);
            if (!resolved.IsResolved || resolved.Value is not ITooltipable tooltip)
                return string.Empty;
            return tooltip.GetDescription()?.Trim() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static JObject Predicates(GameWorldState world, Guid id, EntityKind kind)
    {
        var result = new JObject();
        switch (kind)
        {
            case EntityKind.Structure:
            {
                WorldLookup.TryFind(world.Structures, id, out var structure);
                result["available"] = Verdict(
                    structure.Reading.Unlocked,
                    "published_native_is_available",
                    "native_unavailable");
                result["canPurchase"] =
                    NativeVerdictNotPublished("native_can_purchase_not_published");
                break;
            }
            case EntityKind.Upgrade:
            {
                WorldLookup.TryFind(world.Upgrades, id, out var upgrade);
                result["available"] = Verdict(
                    upgrade.Reading.Available,
                    "published_native_is_available",
                    "native_unavailable");
                result["canPurchase"] =
                    NativeVerdictNotPublished("native_can_purchase_not_published");
                break;
            }
            case EntityKind.Research:
            {
                WorldLookup.TryFind(world.Research, id, out var research);
                result["visible"] = Verdict(
                    research.Visible,
                    "published_native_is_visible",
                    "native_hidden");
                result["available"] = Verdict(
                    research.Available,
                    "published_native_is_available",
                    research.Complete ? "research_complete" : "native_unavailable");
                var reason = research.Complete
                    ? "research_complete"
                    : research.IsDeveloping
                        ? "already_developing"
                        : !research.MeetsLevelRequirements
                            ? "level_requirements_unmet"
                            : !research.StillHasLeeway
                                ? "research_leeway_exhausted"
                                : !research.BelowArtificialMaxLevel
                                    ? "artificial_research_cap_reached"
                                    : !research.BelowMaxInvestmentLevel
                                        ? "research_investment_cap_reached"
                                        : !research.WithinDevelopRange
                                            ? "native_development_range_refused"
                                            : research.CanDevelop
                                                ? "can_develop"
                                                : "native_can_develop_refused";
                result["canDevelop"] = Verdict(
                    research.CanDevelop,
                    "published_native_can_develop",
                    reason);
                break;
            }
            case EntityKind.SpellRecipe:
            {
                WorldLookup.TryFind(world.SpellRecipes, id, out var spell);
                var offered = IsCurrentDiscoveryOffer(world, id);
                var visible = Verdict(
                    spell.Discovered || !spell.HiddenDiscovery || offered,
                    offered ? "published_discovery_offer" : "published_discovery_visibility",
                    "hidden_discovery");
                result["visible"] = visible;
                result["available"] = visible;
                result["canDiscover"] = Verdict(
                    !spell.Discovered && (!spell.HiddenDiscovery || offered),
                    offered ? "published_discovery_offer" : "published_discovery_state",
                    spell.Discovered ? "already_discovered" : "hidden_discovery");
                result["canUse"] = SpellCanUse(world, id);
                break;
            }
            case EntityKind.AlchemyRecipe:
            {
                WorldLookup.TryFind(world.AlchemyRecipes, id, out var alchemy);
                AddDiscoveryPredicates(result, world, id, alchemy.Discovered, nativeDiscoverable: true);
                break;
            }
            case EntityKind.CraftingRecipe:
            {
                WorldLookup.TryFind(world.CraftingRecipes, id, out var recipe);
                var visible = Verdict(
                    recipe.Reading.Visible,
                    "published_native_is_visible",
                    recipe.Reading.VisibilityReasonCode);
                result["visible"] = visible;
                result["available"] = visible;
                result["canPurchase"] = Verdict(
                    recipe.Reading.CanBuyAtStartingQuantity,
                    "published_native_can_buy_at_starting_quantity",
                    recipe.Reading.NativePurchaseReasonCode);
                break;
            }
            case EntityKind.Consumable:
            {
                WorldLookup.TryFind(world.Consumables, id, out var consumable);
                var visible = Verdict(
                    consumable.Visible,
                    "published_native_visibility_field",
                    "not_visible");
                result["visible"] = visible;
                result["available"] = visible;
                result["canUse"] = new JObject
                {
                    ["evaluated"] = false,
                    ["reasonCode"] = "native_can_fire_not_published",
                    ["evidenceSource"] = "explicit_collector_gap",
                };
                break;
            }
            case EntityKind.Resource:
            {
                WorldLookup.TryFind(world.Resources, id, out var resource);
                var visible = Verdict(
                    resource.Reading.Visible,
                    "published_native_is_visible",
                    "not_visible");
                result["visible"] = visible;
                result["available"] = visible;
                break;
            }
            case EntityKind.Ritual:
            {
                WorldLookup.TryFind(world.Rituals, id, out var ritual);
                AddDiscoveryPredicates(result, world, id, ritual.Discovered, nativeDiscoverable: true);
                break;
            }
            case EntityKind.Glyph:
            {
                WorldLookup.TryFind(world.Glyphs, id, out var glyph);
                AddDiscoveryPredicates(result, world, id, glyph.Discovered, glyph.Discoverable);
                break;
            }
            case EntityKind.Equipment:
            {
                WorldLookup.TryFind(world.Equipment, id, out var equipment);
                AddDiscoveryPredicates(result, world, id, equipment.IsCreated, nativeDiscoverable: true);
                break;
            }
            case EntityKind.TimeRune:
            {
                WorldLookup.TryFind(world.TimeRunes, id, out var timeRune);
                AddDiscoveryPredicates(result, world, id, timeRune.Discovered, nativeDiscoverable: true);
                break;
            }
        }
        return result;
    }

    private static void AddDiscoveryPredicates(
        JObject result,
        GameWorldState world,
        Guid id,
        bool discovered,
        bool nativeDiscoverable)
    {
        var offered = IsCurrentDiscoveryOffer(world, id);
        var visible = discovered || offered;
        result["visible"] = Verdict(
            visible,
            offered ? "published_discovery_offer" : "published_discovery_state",
            "not_discovered_or_offered");
        result["available"] = result["visible"]!;
        result["canDiscover"] = Verdict(
            !discovered && nativeDiscoverable,
            offered ? "published_discovery_offer" : "published_discovery_state",
            discovered
                ? "already_discovered"
                : !nativeDiscoverable
                    ? "native_not_discoverable"
                    : "discovery_unavailable");
    }

    private static bool IsCurrentDiscoveryOffer(GameWorldState world, Guid id)
    {
        for (var treeIndex = 0; treeIndex < world.DiscoveryTrees.Count; treeIndex++)
        {
            var offers = world.DiscoveryTrees[treeIndex].CurrentOfferIds;
            for (var offerIndex = 0; offerIndex < offers.Count; offerIndex++)
            {
                if (offers[offerIndex] == id) return true;
            }
        }
        return false;
    }

    private static JObject SpellCanUse(GameWorldState world, Guid recipeId)
    {
        var found = false;
        var ready = false;
        var reason = "not_equipped";
        var slots = new JArray();
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            var slot = world.SpellSlots[index];
            if (!slot.Occupied || slot.SpellRecipeId != recipeId) continue;
            found = true;
            ready |= slot.CastReady;
            if (!slot.CastReady)
            {
                reason = !slot.ResourcesCovered
                    ? "resources_uncovered"
                    : !slot.ChargeAvailable
                        ? "charge_unavailable"
                        : slot.Attuning
                            ? "attuning"
                            : slot.Casting || slot.ReadyingCast
                                ? "cast_in_progress"
                                : "native_can_cast_refused";
            }
            slots.Add(new GameMcpDomainValue(slot));
        }
        var result = Verdict(
            found && ready,
            "published_native_spell_can_cast",
            found ? reason : "not_equipped");
        if (slots.Count > 0) result["slots"] = slots;
        return result;
    }

    private static JObject? Requirements(
        GameWorldState world,
        Guid id,
        EntityKind kind,
        out string? parityFailure,
        out string parityFailureCode)
    {
        parityFailure = null;
        parityFailureCode = string.Empty;
        if (kind is not EntityKind.Structure and not EntityKind.Upgrade and not EntityKind.Research)
            return null;

        var ownerKind = kind switch
        {
            EntityKind.Upgrade => WorldRequirementOwnerKind.Upgrade,
            EntityKind.Research => WorldRequirementOwnerKind.Research,
            _ => WorldRequirementOwnerKind.Structure,
        };
        var checkLevel = kind switch
        {
            EntityKind.Upgrade => UpgradeCheckLevel(world, id),
            EntityKind.Research when WorldLookup.TryFind(world.Research, id, out var research) =>
                research.EffectiveRequirementLevel,
            _ => StructureCheckLevel(world, id),
        };
        var suite = WorldRequirementEvaluator.Evaluate(world, id, checkLevel);
        var root = ProjectRequirementContainer(
            world,
            id,
            containerIndex: 0,
            checkLevel,
            new HashSet<RequirementKey>(),
            depth: 0);
        var parity = new JObject
        {
            ["suiteVerdict"] = suite.ToString(),
        };
        if (!GameMcpWorldQuery.TryCategoryAvailability(
                world, "requirement-native-verdicts", out var categoryFailure))
        {
            parity["status"] = "not_available";
            parity["reasonCode"] = "requirement_collection_incomplete";
            parityFailure = categoryFailure;
            parityFailureCode = "requirement_collection_incomplete";
        }
        else if (!WorldRequirementNativeVerdictLookup.TryFind(
                world.RequirementNativeVerdicts, id, out var native))
        {
            parity["status"] = "not_available";
            parity["reasonCode"] = "native_parameterized_verdict_not_published";
            parityFailure = "the same-generation native parameterized prerequisite verdict is absent";
            parityFailureCode = "native_verdict_unavailable";
        }
        else if (native.OwnerKind != ownerKind || native.CheckLevel != checkLevel)
        {
            parity["status"] = "not_available";
            parity["reasonCode"] = "native_verdict_input_mismatch";
            parity["nativeOwnerKind"] = native.OwnerKind.ToString();
            parity["nativeCheckLevel"] = native.CheckLevel;
            parityFailure = "the native prerequisite oracle was captured for a different owner or level";
            parityFailureCode = "native_verdict_input_mismatch";
        }
        else
        {
            parity["nativeVerdict"] = native.Met ? "Met" : "Unmet";
            if (suite == WorldRequirementVerdict.Unevaluable)
            {
                parity["status"] = "not_available";
                parity["reasonCode"] = "suite_verdict_unevaluable";
                parityFailure = "the suite could not evaluate a requirement the native oracle answered";
                parityFailureCode = "suite_verdict_unevaluable";
            }
            else if ((suite == WorldRequirementVerdict.Met) != native.Met)
            {
                parity["status"] = "mismatch";
                parity["reasonCode"] = "native_verdict_disagrees";
                parityFailure = "suite requirement verdict " + suite +
                    " disagrees with native verdict " + (native.Met ? "Met" : "Unmet");
                parityFailureCode = "native_verdict_mismatch";
            }
            else
            {
                parity["status"] = "matched";
                parity["reasonCode"] = "native_verdict_matched";
            }
        }

        return new JObject
        {
            ["checkLevel"] = checkLevel,
            ["suiteVerdict"] = suite.ToString(),
            ["root"] = root,
            ["nativeParity"] = parity,
        };
    }

    private static JObject ProjectRequirementContainer(
        GameWorldState world,
        Guid ownerId,
        int containerIndex,
        long checkLevel,
        HashSet<RequirementKey> trail,
        int depth)
    {
        var key = new RequirementKey(ownerId, containerIndex);
        if (depth > MaximumRequirementExpansionDepth)
            return Refusal("requirement_depth_exceeded", ownerId, containerIndex);
        if (!trail.Add(key)) return Refusal("requirement_cycle", ownerId, containerIndex);
        try
        {
            var children = new JArray();
            if (WorldEntityRequirementLookup.TryFindContainerRange(
                    world.EntityRequirements, ownerId, containerIndex, out var start, out var count))
            {
                var rows = world.EntityRequirements.AsSpan();
                for (var offset = 0; offset < count; offset++)
                {
                    ref readonly var row = ref rows[start + offset];
                    if (row.ParentOrdinal >= 0) continue;
                    children.Add(ProjectRequirementNode(
                        world, rows, start, count, in row, checkLevel, trail, depth + 1));
                }
            }
            var result = new JObject
            {
                ["status"] = "available",
                ["ownerUuid"] = ownerId.ToString("D"),
                ["tierIndex"] = containerIndex,
                ["operator"] = "AND",
            };
            if (children.Count > 0) result["children"] = children;
            return result;
        }
        finally
        {
            trail.Remove(key);
        }
    }

    private static JObject ProjectRequirementNode(
        GameWorldState world,
        ReadOnlySpan<WorldEntityRequirement> rows,
        int start,
        int count,
        in WorldEntityRequirement row,
        long checkLevel,
        HashSet<RequirementKey> trail,
        int depth)
    {
        if (row.NodeKind == WorldRequirementNodeKind.Group)
        {
            var children = new JArray();
            for (var offset = 0; offset < count; offset++)
            {
                ref readonly var child = ref rows[start + offset];
                if (child.ParentOrdinal != row.Ordinal) continue;
                children.Add(ProjectRequirementNode(
                    world, rows, start, count, in child, checkLevel, trail, depth + 1));
            }
            var group = new JObject
            {
                ["nodeKind"] = "group",
                ["ordinal"] = row.Ordinal,
                ["parentOrdinal"] = row.ParentOrdinal,
                ["depth"] = row.Depth,
                ["operator"] = row.Operator.ToString().ToUpperInvariant(),
            };
            if (children.Count > 0) group["children"] = children;
            return group;
        }

        var evaluated = WorldRequirementEvaluator.ExplainLeaf(world, in row, checkLevel);
        var leaf = new JObject
        {
            ["nodeKind"] = "leaf",
            ["ordinal"] = row.Ordinal,
            ["parentOrdinal"] = row.ParentOrdinal,
            ["depth"] = row.Depth,
            ["conditionType"] = row.ConditionTypeName,
            ["conditionKind"] = row.Kind.ToString(),
            ["requirementNativeType"] = RequirementNativeType(row.Kind),
            ["reqType"] = row.ReqType,
            ["selectedValueKind"] = evaluated.SelectedValueKind,
            ["current"] = ProjectNumber(evaluated.Current),
            ["required"] = ProjectNumber(evaluated.Required),
            ["met"] = evaluated.Met,
            ["verdict"] = evaluated.Verdict.ToString(),
            ["reasonCode"] = evaluated.ReasonCode,
            ["baseThreshold"] = ProjectNumber(evaluated.BaseThreshold),
            ["scaledThreshold"] = ProjectNumber(evaluated.ScaledThreshold),
            ["effectiveThreshold"] = ProjectNumber(evaluated.EffectiveThreshold),
        };
        if (row.TargetId != Guid.Empty)
            leaf["requirementUuid"] = row.TargetId.ToString("D");
        if (row.Kind == WorldRequirementConditionKind.PrerequisiteLink)
        {
            var tiers = ProjectLinkTiers(
                world, in row, checkLevel, evaluated, trail, depth + 1);
            if (tiers.Count > 0) leaf["prerequisiteLinkTiers"] = tiers;
        }
        return leaf;
    }

    private static JArray ProjectLinkTiers(
        GameWorldState world,
        in WorldEntityRequirement row,
        long checkLevel,
        in WorldRequirementLeafEvaluation evaluation,
        HashSet<RequirementKey> trail,
        int depth)
    {
        var selectedTier = row.ReqType == 0
            ? 0L
            : BigDouble.Round(evaluation.ScaledThreshold).ToLong();
        var tiers = new JArray();
        for (var index = 0; index < world.PrerequisiteLinkTiers.Count; index++)
        {
            var tier = world.PrerequisiteLinkTiers[index];
            if (tier.LinkId != row.TargetId) continue;
            tiers.Add(new JObject
            {
                ["tierIndex"] = tier.TierIndex,
                ["selected"] = selectedTier == tier.TierIndex,
                ["activeEnabled"] = tier.ActiveEnabled,
                ["passiveEnabled"] = tier.PassiveEnabled,
                ["evaluatedFrame"] = tier.EvaluatedFrame,
                ["collectedFrame"] = tier.CollectedFrame,
                ["evaluatedThisFrame"] = tier.EvaluatedThisFrame,
                ["requirements"] = ProjectRequirementContainer(
                    world,
                    row.TargetId,
                    tier.TierIndex,
                    checkLevel,
                    trail,
                    depth),
            });
        }
        return tiers;
    }

    private static JObject? ResearchThresholds(GameWorldState world, Guid id, EntityKind kind)
    {
        if (kind != EntityKind.Research || !WorldLookup.TryFind(world.Research, id, out var research))
            return null;
        var result = new JObject
        {
            ["selectedValueKind"] = "total_level",
            ["current"] = research.TotalLevel,
            ["baseThreshold"] = research.BaseRequirementLevel,
            ["scaledThreshold"] = research.BaseRequirementLevel,
            ["effectiveThreshold"] = research.EffectiveRequirementLevel,
            ["leeway"] = research.Modifiers.LeewayPoints.ToInt(),
            ["metWithLeeway"] = research.StillHasLeeway,
            ["nativeMeetsLevelRequirements"] = research.MeetsLevelRequirements,
            ["nativeStillHasLeeway"] = research.StillHasLeeway,
            ["adjustment"] = research.RequirementLevelAdjustment,
        };
        if (research.RequirementAdjustments.Count > 0)
            result["activeAdjustments"] = new GameMcpDomainValue(
                research.RequirementAdjustments);
        return result;
    }

    private static JObject? Purchase(GameWorldState world, Guid id, EntityKind kind)
    {
        if (kind is not EntityKind.Structure and not EntityKind.Upgrade)
            return null;
        if (!WorldPurchaseCostLookup.TryFindRange(
                world.PurchaseCosts, id, out var start, out var count))
        {
            return new JObject
            {
                ["evaluated"] = false,
                ["reasonCode"] = "exact_cost_unavailable",
            };
        }
        var rows = new JArray();
        var affordable = true;
        var evaluated = true;
        for (var index = 0; index < count; index++)
        {
            var row = world.PurchaseCosts[start + index];
            rows.Add(GameMcpWorldQuery.ProjectPurchaseCost(world, in row));
            evaluated &= row.AffordabilityEvaluated;
            affordable &= row.Affordable;
        }
        var result = new JObject
        {
            ["rows"] = rows,
        };
        result["affordability"] = !evaluated
            ? "unavailable"
            : affordable ? "affordable" : "unaffordable";
        return result;
    }

    private static JObject Blockers(GameWorldState world, Guid id, EntityKind kind)
    {
        var result = new JObject();

        if (kind is EntityKind.Structure or EntityKind.Upgrade)
        {
            JObject queue;
            if (WorldLookup.TryFind(
                    world.ActionQueues, KnownEntities.ActiveActionables.Uuid, out var actionQueue))
            {
                queue = Blocker(
                    !actionQueue.Consistent || !actionQueue.HasEmptySlot,
                    !actionQueue.Consistent
                        ? "queue_reading_inconsistent"
                        : actionQueue.HasEmptySlot ? "queue_room_available" : "queue_full");
                queue["evidence"] = new GameMcpDomainValue(actionQueue);
            }
            else
            {
                queue = Blocker(true, "queue_not_published");
            }
            result["queue"] = queue;
        }
        if (kind == EntityKind.Upgrade && WorldLookup.TryFind(world.Upgrades, id, out var upgrade))
        {
            result["cap"] = Blocker(upgrade.IsExhausted, upgrade.IsExhausted
                ? "level_cap_reached"
                : "below_level_cap");
        }
        else if (kind == EntityKind.Research &&
                 WorldLookup.TryFind(world.Research, id, out var research))
        {
            var slack = research.Modifiers.LeewayPoints.ToInt();
            var committed = research.BaseLevel + research.QueuedLevels +
                (research.IsDeveloping ? 1 : 0);
            var leeway = Blocker(
                !research.StillHasLeeway,
                research.StillHasLeeway
                    ? "native_leeway_available"
                    : "native_leeway_exhausted");
            leeway["currentTotalLevel"] = research.TotalLevel;
            leeway["leeway"] = slack;
            leeway["effectiveRequirement"] = research.EffectiveRequirementLevel;
            leeway["nativeMeetsLevelRequirements"] = research.MeetsLevelRequirements;
            result["leeway"] = leeway;
            var cap = Blocker(
                research.Complete || !research.BelowArtificialMaxLevel ||
                    !research.BelowMaxInvestmentLevel,
                research.Complete
                    ? "research_complete"
                    : !research.BelowArtificialMaxLevel
                        ? "artificial_research_cap_reached"
                        : !research.BelowMaxInvestmentLevel
                            ? "research_investment_cap_reached"
                            : "below_research_cap");
            cap["committedLevel"] = committed;
            cap["purchasedLevel"] = research.PurchasedLevels;
            cap["baseLevelExcludingBonus"] = research.BaseLevel;
            cap["bonusLevel"] = research.BonusLevel;
            cap["totalLevel"] = research.TotalLevel;
            cap["effectiveCap"] = research.MaxLevel;
            cap["artificialCap"] = research.ArtificialMaxLevel;
            cap["nativeComplete"] = research.Complete;
            result["cap"] = cap;
        }
        if (kind == EntityKind.SpellRecipe &&
            WorldLookup.TryFind(world.SpellRecipes, id, out var spell))
        {
            result["recipeDiscovery"] = Blocker(
                !spell.Discovered,
                spell.Discovered ? "recipe_discovered" : "recipe_not_discovered");
            var (bandwidth, drain) = SpellResourceBlockers(world, id);
            if (bandwidth is not null) result["bandwidth"] = bandwidth;
            if (drain is not null) result["drain"] = drain;
        }
        else if (kind == EntityKind.AlchemyRecipe &&
                 WorldLookup.TryFind(world.AlchemyRecipes, id, out var alchemy))
        {
            result["recipeDiscovery"] = Blocker(
                !alchemy.Discovered,
                alchemy.Discovered ? "recipe_discovered" : "recipe_not_discovered");
            var (bandwidth, drain) = AlchemyResourceBlockers(world, id);
            if (bandwidth is not null) result["bandwidth"] = bandwidth;
            if (drain is not null) result["drain"] = drain;
        }
        else if (kind == EntityKind.CraftingRecipe &&
                 WorldLookup.TryFind(world.CraftingRecipes, id, out var crafting))
        {
            result["recipeDiscovery"] = Blocker(
                !crafting.Reading.Visible,
                crafting.Reading.VisibilityReasonCode);
            var bandwidth = CraftingBandwidthBlocker(in crafting);
            var drain = CraftingDrainBlocker(in crafting);
            if (bandwidth is not null) result["bandwidth"] = bandwidth;
            if (drain is not null) result["drain"] = drain;
        }

        return result;
    }

    private static (JObject? Bandwidth, JObject? Drain) SpellResourceBlockers(
        GameWorldState world,
        Guid recipeId)
    {
        var bandwidthRows = new JArray();
        var drainRows = new JArray();
        var bandwidthBlocked = false;
        var drainBlocked = false;
        for (var slotIndex = 0; slotIndex < world.SpellSlots.Count; slotIndex++)
        {
            var slot = world.SpellSlots[slotIndex];
            if (!slot.Occupied || slot.SpellRecipeId != recipeId) continue;
            CollectSpellCosts(
                world, slot.SlotIndex, WorldSpellCostKind.Immediate,
                bandwidthRows, ref bandwidthBlocked);
            CollectSpellCosts(
                world, slot.SlotIndex, WorldSpellCostKind.Drain,
                drainRows, ref drainBlocked);
        }
        return (
            ResourceBlocker(bandwidthRows, bandwidthBlocked, "bandwidth"),
            ResourceBlocker(drainRows, drainBlocked, "drain"));
    }

    private static void CollectSpellCosts(
        GameWorldState world,
        int slotIndex,
        WorldSpellCostKind kind,
        JArray rows,
        ref bool blocked)
    {
        if (!WorldSpellCostLookup.TryFindRange(
                world.SpellCosts, slotIndex, kind, out var start, out var count)) return;
        for (var index = 0; index < count; index++)
        {
            var cost = world.SpellCosts[start + index];
            var row = ResourceCostEvidence(world, cost.ResourceId, cost.Amount, out var oneBlocked);
            row["slotIndex"] = slotIndex;
            row["costKind"] = kind.ToString();
            rows.Add(row);
            blocked |= oneBlocked;
        }
    }

    private static (JObject? Bandwidth, JObject? Drain) AlchemyResourceBlockers(
        GameWorldState world,
        Guid recipeId)
    {
        var bandwidthRows = ResourceCosts(
            world, recipeId, WorldAlchemyCostKind.RecipeDrain, out var bandwidthBlocked);
        var drainRows = ResourceCosts(
            world, recipeId, WorldAlchemyCostKind.CurrentDrain, out var drainBlocked);
        return (
            ResourceBlocker(bandwidthRows, bandwidthBlocked, "bandwidth"),
            ResourceBlocker(drainRows, drainBlocked, "drain"));
    }

    private static JArray ResourceCosts(
        GameWorldState world,
        Guid recipeId,
        WorldAlchemyCostKind kind,
        out bool blocked)
    {
        blocked = false;
        var rows = new JArray();
        if (!WorldAlchemyCostLookup.TryFindRange(
                world.AlchemyCosts, recipeId, kind, out var start, out var count)) return rows;
        for (var index = 0; index < count; index++)
        {
            var cost = world.AlchemyCosts[start + index];
            var row = ResourceCostEvidence(world, cost.ResourceId, cost.Amount, out var oneBlocked);
            row["costKind"] = kind.ToString();
            rows.Add(row);
            blocked |= oneBlocked;
        }
        return rows;
    }

    private static JObject ResourceCostEvidence(
        GameWorldState world,
        Guid resourceId,
        BigDouble amount,
        out bool blocked)
    {
        var available = BigDouble.Zero;
        var bandwidth = false;
        if (WorldLookup.TryFind(world.Resources, resourceId, out var resource))
        {
            bandwidth = resource.Reading.Traits.BandwidthResource;
            available = bandwidth ? resource.Headroom : resource.Reading.Quantity;
            blocked = available.CompareTo(amount) < 0;
        }
        else
        {
            blocked = true;
        }
        var result = new JObject
        {
            ["resourceUuid"] = resourceId.ToString("D"),
            ["cost"] = ProjectNumber(amount),
            ["amount"] = ProjectNumber(available),
            ["blocked"] = blocked,
        };
        if (bandwidth) result["bandwidth"] = true;
        if (blocked) result["reasonCode"] = "resource_or_headroom_insufficient";
        return result;
    }

    private static JObject? CraftingBandwidthBlocker(in WorldCraftingRecipe recipe)
    {
        var rows = new JArray();
        var blocked = false;
        for (var index = 0; index < recipe.Resources.Count; index++)
        {
            var resource = recipe.Resources[index];
            if (resource.Kind != WorldCraftingRecipeResourceKind.AuthoredInput ||
                !resource.BandwidthResource) continue;
            var oneBlocked = !resource.ResourceStateAvailable ||
                resource.Headroom.CompareTo(resource.Amount) < 0;
            blocked |= oneBlocked;
            rows.Add(new JObject
            {
                ["resourceUuid"] = resource.ResourceId.ToString("D"),
                ["cost"] = ProjectNumber(resource.Amount),
                ["amount"] = ProjectNumber(resource.Headroom),
                ["bandwidth"] = true,
                ["blocked"] = oneBlocked,
            });
        }
        return ResourceBlocker(rows, blocked, "bandwidth");
    }

    private static JObject? CraftingDrainBlocker(in WorldCraftingRecipe recipe)
    {
        var rows = new JArray();
        var blocked = false;
        for (var index = 0; index < recipe.DrainBlocks.Count; index++)
        {
            var row = recipe.DrainBlocks[index];
            rows.Add(new GameMcpDomainValue(row));
            blocked |= row.Blocked;
        }
        return ResourceBlocker(rows, blocked, "drain");
    }

    private static JObject? ResourceBlocker(JArray rows, bool blocked, string axis)
    {
        if (rows.Count == 0) return null;
        var result = new JObject
        {
            ["blocked"] = blocked,
            ["rows"] = rows,
        };
        if (blocked) result["reasonCode"] = axis + "_blocked";
        return result;
    }

    internal static bool TryDescribePublishedEntity(
        GameWorldState world,
        Guid id,
        out string category,
        out string nativeType,
        out object row)
    {
        if (!TryResolve(world, id, out var kind, out row, out nativeType))
        {
            category = string.Empty;
            return false;
        }
        category = kind switch
        {
            EntityKind.Structure => "structures",
            EntityKind.Upgrade => "upgrades",
            EntityKind.Research => "research",
            EntityKind.SpellRecipe => "spell-recipes",
            EntityKind.AlchemyRecipe => "alchemy-recipes",
            EntityKind.CraftingRecipe => "crafting-recipes",
            EntityKind.Consumable => "consumables",
            EntityKind.Resource => "resources",
            EntityKind.Ritual => "rituals",
            EntityKind.Glyph => "glyphs",
            EntityKind.Equipment => "equipment",
            EntityKind.TimeRune => "time-runes",
            EntityKind.DiscoveryTree => "discovery-trees",
            _ => string.Empty,
        };
        return category.Length != 0;
    }

    private static bool TryResolve(
        GameWorldState world,
        Guid id,
        out EntityKind kind,
        out object row,
        out string nativeType)
    {
        var matches = 0;
        var resolvedKind = EntityKind.Unknown;
        object? resolvedRow = null;
        var resolvedNativeType = string.Empty;
        void Found(EntityKind candidateKind, object candidate, string candidateType)
        {
            matches++;
            resolvedKind = candidateKind;
            resolvedRow = candidate;
            resolvedNativeType = candidateType;
        }

        if (WorldLookup.TryFind(world.Structures, id, out var structure))
            Found(EntityKind.Structure, structure, "StructureSO");
        if (WorldLookup.TryFind(world.Upgrades, id, out var upgrade))
            Found(EntityKind.Upgrade, upgrade, "UpgradeSO");
        if (WorldLookup.TryFind(world.Research, id, out var research))
            Found(EntityKind.Research, research, "ResearchSO");
        if (WorldLookup.TryFind(world.SpellRecipes, id, out var spell))
            Found(EntityKind.SpellRecipe, spell, "SpellRecipeSO");
        if (WorldLookup.TryFind(world.AlchemyRecipes, id, out var alchemy))
            Found(EntityKind.AlchemyRecipe, alchemy, "AlchemyRecipeSO");
        if (WorldLookup.TryFind(world.CraftingRecipes, id, out var crafting))
            Found(EntityKind.CraftingRecipe, crafting, "CraftingRecipeSO");
        if (WorldLookup.TryFind(world.Consumables, id, out var consumable))
            Found(EntityKind.Consumable, consumable, "ConsumableSO");
        if (WorldLookup.TryFind(world.Resources, id, out var resource))
            Found(EntityKind.Resource, resource, "ResourceSO");
        if (WorldLookup.TryFind(world.Rituals, id, out var ritual))
            Found(EntityKind.Ritual, ritual, "RitualSO");
        if (WorldLookup.TryFind(world.Glyphs, id, out var glyph))
            Found(EntityKind.Glyph, glyph, "GlyphSO");
        if (WorldLookup.TryFind(world.Equipment, id, out var equipment))
            Found(EntityKind.Equipment, equipment, "EquipmentSO");
        if (WorldLookup.TryFind(world.TimeRunes, id, out var timeRune))
            Found(EntityKind.TimeRune, timeRune, "TimeRuneSO");
        if (WorldLookup.TryFind(world.DiscoveryTrees, id, out var discoveryTree))
            Found(EntityKind.DiscoveryTree, discoveryTree, "DiscoveryTreeSO");
        kind = resolvedKind;
        row = resolvedRow!;
        nativeType = resolvedNativeType;
        return matches == 1;
    }

    private static long UpgradeCheckLevel(GameWorldState world, Guid id)
    {
        WorldLookup.TryFind(world.Upgrades, id, out var row);
        return WorldRequirementEvaluator.UpgradeCheckLevel(in row);
    }

    private static long StructureCheckLevel(GameWorldState world, Guid id)
    {
        WorldLookup.TryFind(world.Structures, id, out var row);
        return WorldRequirementEvaluator.StructureCheckLevel(in row);
    }

    private static JObject Verdict(
        bool value,
        string evidenceSource,
        string falseReason) => new()
    {
        ["value"] = value,
        ["reasonCode"] = value ? "passed" : falseReason,
        ["evidenceSource"] = evidenceSource,
    };

    private static JObject Blocker(bool blocked, string reasonCode) => new()
    {
        ["blocked"] = blocked,
        ["reasonCode"] = reasonCode,
    };

    private static JObject NativeVerdictNotPublished(string reasonCode) => new()
    {
        ["evaluated"] = false,
        ["reasonCode"] = reasonCode,
        ["evidenceSource"] = "explicit_collector_gap",
    };

    private static JObject Refusal(string code, Guid ownerId, int tierIndex) => new()
    {
        ["status"] = "not_available",
        ["code"] = code,
        ["ownerUuid"] = ownerId.ToString("D"),
        ["tierIndex"] = tierIndex,
    };

    private static GameMcpValue ProjectNumber(BigDouble value) =>
        new GameMcpDomainValue(value);

    private static string RequirementNativeType(WorldRequirementConditionKind kind) => kind switch
    {
        WorldRequirementConditionKind.Upgrade => "UpgradeSO",
        WorldRequirementConditionKind.Research => "ResearchSO",
        WorldRequirementConditionKind.Structure => "StructureSO",
        WorldRequirementConditionKind.Spell => "SpellRecipeSO",
        WorldRequirementConditionKind.AlchemyRecipe => "AlchemyRecipeSO",
        WorldRequirementConditionKind.Ritual => "RitualSO",
        WorldRequirementConditionKind.Number => "NumberVariable",
        WorldRequirementConditionKind.Generic => "UpgradeableObject",
        WorldRequirementConditionKind.PrerequisiteLink => "PrerequisiteLinkSO",
        _ => "unknown",
    };

    private readonly struct RequirementKey : IEquatable<RequirementKey>
    {
        internal RequirementKey(Guid ownerId, int tierIndex)
        {
            OwnerId = ownerId;
            TierIndex = tierIndex;
        }

        private Guid OwnerId { get; }
        private int TierIndex { get; }
        public bool Equals(RequirementKey other) =>
            OwnerId == other.OwnerId && TierIndex == other.TierIndex;
        public override bool Equals(object? obj) => obj is RequirementKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(OwnerId, TierIndex);
    }

    private enum EntityKind
    {
        Unknown = 0,
        Structure = 1,
        Upgrade = 2,
        Research = 3,
        SpellRecipe = 4,
        AlchemyRecipe = 5,
        CraftingRecipe = 6,
        Consumable = 7,
        Resource = 8,
        Ritual = 9,
        Glyph = 10,
        Equipment = 11,
        TimeRune = 12,
        DiscoveryTree = 13,
    }
}
#endif
