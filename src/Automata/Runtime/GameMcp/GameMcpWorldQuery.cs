#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>Read-only queries over one pinned world publication.</summary>
internal static class GameMcpWorldQuery
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 200;
    private static readonly GameMcpWorldCategory[] Categories = CreateCategories();
    private static readonly Dictionary<string, GameMcpWorldCategory> ByName = IndexCategories();

    internal static JObject Overview(GameMcpStateSnapshot state)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;

        var world = publication.Snapshot;
        var result = Envelope(publication);
        result["status"] = "available";
        result["collection"] = CompactCollectionStatus(world);
        result["economy"] = new JObject
        {
            ["resourceRows"] = world.Resources.Count,
            ["unlockedStructures"] = CountUnlockedStructures(world),
            ["purchasableUpgrades"] = CountPurchasableUpgrades(world),
            ["purchaseCostRows"] = world.PurchaseCosts.Count,
        };
        result["progression"] = new JObject
        {
            ["discoveredSpellRecipes"] = CountDiscoveredSpells(world),
            ["spellRecipesReadyToLevel"] = CountReadySpells(world),
            ["discoveredAlchemyRecipes"] = CountDiscoveredAlchemy(world),
            ["availableViews"] = CountAvailableViews(world),
            ["visiblePlots"] = CountVisiblePlots(world),
        };
        result["running"] = new JObject
        {
            ["actionQueues"] = world.ActionQueues.Count,
            ["occupiedActionQueueSlots"] = world.ActionQueueSlots.Count,
            ["actionQueueMembers"] = world.ActionQueueMembers.Count,
            ["equippedSpellSlots"] = world.SpellSlots.Count,
            ["activeConceptAssignments"] = world.AlchemyInstances.Count,
            ["plotActionInstances"] = world.PlotActionInstances.Count,
        };
        result["detailCategories"] = new JArray(
            "resources",
            "structures",
            "upgrades",
            "purchase-costs",
            "spell-recipes",
            "spell-slots",
            "alchemy-recipes",
            "alchemy-instances",
            "plot-nodes",
            "plot-actions");
        return result;
    }

    internal static JObject ListCategories(GameMcpStateSnapshot state)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        var result = Envelope(publication);
        result["status"] = "available";
        var categories = new JArray();
        for (var index = 0; index < Categories.Length; index++)
            categories.Add(DescribeCategory(publication.Snapshot, Categories[index]));
        result["categories"] = categories;
        return result;
    }

    internal static JObject ListRows(
        GameMcpStateSnapshot state,
        string categoryName,
        int offset,
        int limit)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        if (!TryCategory(categoryName, out var category, out var reason))
            return NotAvailable(publication, "unknown_category", reason);
        if (offset < 0)
            return NotAvailable(publication, "invalid_offset", "offset must be zero or greater");
        if (limit <= 0 || limit > MaximumPageSize)
        {
            return NotAvailable(
                publication,
                "invalid_limit",
                "limit must be between 1 and " +
                MaximumPageSize.ToString(CultureInfo.InvariantCulture));
        }

        var availability = Availability(publication.Snapshot, category);
        if (!(bool)availability["available"]!)
        {
            return NotAvailable(
                publication,
                "category_not_collected",
                (string?)availability["reason"] ?? "the category was not collected");
        }

        var count = category.Count(publication.Snapshot);
        var rows = new JArray();
        var end = Math.Min(count, checked(offset + limit));
        for (var index = offset; index < end; index++)
            rows.Add(ProjectScanRow(category, category.Row(publication.Snapshot, index)));

        var result = Envelope(publication);
        result["status"] = "available";
        result["category"] = category.Name;
        result["rowType"] = category.RowTypeName;
        result["expectedNativeType"] = category.ExpectedNativeType;
        result["total"] = count;
        result["offset"] = offset;
        result["limit"] = limit;
        result["rows"] = rows;
        if (end < count) result["nextOffset"] = end;
        return result;
    }

    internal static JObject GetRow(
        GameMcpStateSnapshot state,
        string categoryName,
        string uuidText,
        string expectedNativeType)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        if (!TryCategory(categoryName, out var category, out var reason))
            return NotAvailable(publication, "unknown_category", reason);
        if (!Guid.TryParseExact(uuidText ?? string.Empty, "D", out var uuid))
            return NotAvailable(publication, "invalid_uuid", "uuid must be a canonical D-format GUID");
        if (expectedNativeType.Length > 0 &&
            !string.Equals(
                expectedNativeType,
                category.ExpectedNativeType,
                StringComparison.Ordinal))
        {
            return NotAvailable(
                publication,
                "native_type_mismatch",
                "category " + category.Name + " requires expected native type " +
                category.ExpectedNativeType + ", not " + expectedNativeType);
        }
        if (!string.Equals(
                category.IdentityMode,
                "stable_entity_uuid",
                StringComparison.Ordinal))
        {
            return NotAvailable(
                publication,
                "composite_identity_required",
                "category " + category.Name + " has composite identity fields and cannot " +
                "be uniquely addressed by one UUID; use world_list to read its exact rows");
        }

        var availability = Availability(publication.Snapshot, category);
        if (!(bool)availability["available"]!)
        {
            return NotAvailable(
                publication,
                "category_not_collected",
                (string?)availability["reason"] ?? "the category was not collected");
        }

        var count = category.Count(publication.Snapshot);
        for (var index = 0; index < count; index++)
        {
            var row = category.Row(publication.Snapshot, index);
            if (!category.TryIdentity(row, out var rowIdentity) || rowIdentity != uuid) continue;
            var result = Envelope(publication);
            result["status"] = "available";
            result["category"] = category.Name;
            result["rowType"] = category.RowTypeName;
            result["expectedNativeType"] = category.ExpectedNativeType;
            result["row"] = ProjectRow(category, row);
            return result;
        }

        return NotAvailable(
            publication,
            "unknown_uuid",
            "category " + category.Name + " has no row with stable identity " +
            uuid.ToString("D"));
    }

    internal static JObject Search(
        GameMcpStateSnapshot state,
        string query,
        int limit)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        var normalized = (query ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return NotAvailable(publication, "query_required", "query must not be empty");
        if (limit <= 0 || limit > MaximumPageSize)
        {
            return NotAvailable(
                publication,
                "invalid_limit",
                "limit must be between 1 and " +
                MaximumPageSize.ToString(CultureInfo.InvariantCulture));
        }

        var matches = new JArray();
        var unavailableCategories = new JArray();
        for (var categoryIndex = 0;
             categoryIndex < Categories.Length;
             categoryIndex++)
        {
            var category = Categories[categoryIndex];
            var availability = Availability(publication.Snapshot, category);
            if (!(bool)availability["available"]!)
            {
                unavailableCategories.Add(new JObject
                {
                    ["category"] = category.Name,
                    ["reason"] = availability["reason"],
                });
                continue;
            }
            var count = category.Count(publication.Snapshot);
            for (var rowIndex = 0; rowIndex < count && matches.Count < limit; rowIndex++)
            {
                var row = category.Row(publication.Snapshot, rowIndex);
                var complete = ProjectRow(category, row);
                if (!Matches(complete, normalized)) continue;
                matches.Add(new JObject
                {
                    ["category"] = category.Name,
                    ["expectedNativeType"] = category.ExpectedNativeType,
                    ["row"] = ProjectScanRow(category, row),
                });
            }
        }

        var result = Envelope(publication);
        result["status"] = unavailableCategories.Count == 0
            ? "available"
            : "not_available";
        if (unavailableCategories.Count > 0)
        {
            result["code"] = "world_search_incomplete";
            result["reason"] =
                "the search cannot claim an authoritative result because " +
                unavailableCategories.Count + " world categories were not fully collected";
            result["unavailableCategories"] = unavailableCategories;
        }
        result["query"] = normalized;
        result["limit"] = limit;
        result[unavailableCategories.Count == 0 ? "matches" : "partialMatches"] = matches;
        return result;
    }

    internal static JObject NotAvailableWithoutWorld(GameMcpStateSnapshot state, string code, string reason)
    {
        var result = new JObject
        {
            ["status"] = "not_available",
            ["code"] = code,
            ["reason"] = reason,
            ["worldGeneration"] = 0,
            ["structuralEpoch"] = state.LifecycleGeneration,
            ["collectedEpoch"] = 0,
            ["collectedAtUtc"] = JValue.CreateNull(),
            ["respondedAtUtc"] = DateTime.UtcNow.ToString("O"),
        };
        return result;
    }

    internal static JObject WithEnvelope(GameMcpStateSnapshot state, JObject payload)
    {
        if (state.World is not null &&
            state.World.Generation > 1 &&
            state.World.Snapshot.CollectedAtUtcTicks > 0)
        {
            var envelope = Envelope(state.World);
            foreach (var property in payload.Properties())
                envelope[property.Name] = property.Value;
            return envelope;
        }

        var result = new JObject
        {
            ["worldGeneration"] = state.World?.Generation ?? 0,
            ["structuralEpoch"] = state.LifecycleGeneration,
            ["collectedEpoch"] = state.World?.Snapshot.CollectedAtEpoch ?? 0,
            ["collectedAtUtc"] = state.World is null
                ? JValue.CreateNull()
                : FormatUtcTicks(state.World.Snapshot.CollectedAtUtcTicks),
            ["respondedAtUtc"] = DateTime.UtcNow.ToString("O"),
        };
        foreach (var property in payload.Properties())
            result[property.Name] = property.Value;
        return result;
    }

    private static bool TryWorld(
        GameMcpStateSnapshot state,
        out ServiceWorldPublication publication,
        out JObject unavailable)
    {
        if (state.World is not null &&
            state.World.Generation > 1 &&
            state.World.Snapshot.CollectedAtUtcTicks > 0)
        {
            publication = state.World;
            unavailable = null!;
            return true;
        }

        publication = null!;
        unavailable = NotAvailableWithoutWorld(
            state,
            "world_not_published",
            state.RuntimeNotAvailableReason.Length == 0
                ? "the world collector has not published a captured world yet"
                : state.RuntimeNotAvailableReason);
        return false;
    }

    private static JObject Envelope(ServiceWorldPublication publication)
    {
        var world = publication.Snapshot;
        return new JObject
        {
            ["worldGeneration"] = publication.Generation,
            ["structuralEpoch"] = world.CollectedAtEpoch,
            ["collectedEpoch"] = world.CollectedAtEpoch,
            ["collectedAtUtc"] = FormatUtcTicks(world.CollectedAtUtcTicks),
            ["respondedAtUtc"] = DateTime.UtcNow.ToString("O"),
        };
    }

    private static JObject NotAvailable(
        ServiceWorldPublication publication,
        string code,
        string reason)
    {
        var result = Envelope(publication);
        result["status"] = "not_available";
        result["code"] = code;
        result["reason"] = reason;
        return result;
    }

    private static JToken FormatUtcTicks(long ticks)
    {
        if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks) return JValue.CreateNull();
        return new JValue(new DateTime(ticks, DateTimeKind.Utc).ToString("O"));
    }

    private static JObject CompactCollectionStatus(GameWorldState world)
    {
        var unavailable = new JArray();
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            var category = world.CollectionCategories[index];
            if (category.IsClean) continue;
            unavailable.Add(new JObject
            {
                ["category"] = category.Category,
                ["outcome"] = category.Outcome.ToString(),
                ["skipped"] = category.Skipped,
                ["reason"] = category.FirstFailure,
            });
        }
        return new JObject
        {
            ["complete"] = IsCollectionComplete(world),
            ["reportedCategories"] = world.CollectionCategories.Count,
            ["unavailableCategories"] = unavailable,
        };
    }

    private static int CountUnlockedStructures(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.Structures.Count; index++)
            if (world.Structures[index].Reading.Unlocked) count++;
        return count;
    }

    private static int CountPurchasableUpgrades(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.Upgrades.Count; index++)
        {
            var upgrade = world.Upgrades[index];
            if (upgrade.Reading.Available && !upgrade.IsExhausted) count++;
        }
        return count;
    }

    private static int CountDiscoveredSpells(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.SpellRecipes.Count; index++)
            if (world.SpellRecipes[index].Discovered) count++;
        return count;
    }

    private static int CountReadySpells(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.SpellRecipes.Count; index++)
            if (world.SpellRecipes[index].MasteryLevelReady) count++;
        return count;
    }

    private static int CountDiscoveredAlchemy(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.AlchemyRecipes.Count; index++)
            if (world.AlchemyRecipes[index].Discovered) count++;
        return count;
    }

    private static int CountAvailableViews(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.Views.Count; index++)
            if (world.Views[index].Available) count++;
        return count;
    }

    private static int CountVisiblePlots(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.PlotNodes.Count; index++)
            if (world.PlotNodes[index].Reading.Visible) count++;
        return count;
    }

    private static bool IsCollectionComplete(GameWorldState world)
    {
        if (world.CollectionCategories.Count == 0) return false;
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            if (!world.CollectionCategories[index].IsClean) return false;
        }
        return true;
    }

    private static JObject DescribeCategory(GameWorldState world, GameMcpWorldCategory category)
    {
        var availability = Availability(world, category);
        return new JObject
        {
            ["name"] = category.Name,
            ["worldProperty"] = category.WorldPropertyName,
            ["rowType"] = category.RowTypeName,
            ["expectedNativeType"] = category.ExpectedNativeType,
            ["count"] = category.Count(world),
            ["available"] = availability["available"],
            ["reason"] = availability["reason"],
            ["identityMode"] = category.IdentityMode,
        };
    }

    private static JObject Availability(GameWorldState world, GameMcpWorldCategory category)
    {
        for (var requiredIndex = 0;
             requiredIndex < category.ReportCategories.Length;
             requiredIndex++)
        {
            var reportName = category.ReportCategories[requiredIndex];
            var found = false;
            for (var index = 0; index < world.CollectionCategories.Count; index++)
            {
                var report = world.CollectionCategories[index];
                if (!string.Equals(
                        Normalize(report.Category),
                        reportName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                found = true;
                if (report.Outcome == WorldCategoryOutcome.Unavailable)
                {
                    return new JObject
                    {
                        ["available"] = false,
                        ["reason"] = report.FirstFailure.Length == 0
                            ? "the collector reported " + reportName + " unavailable"
                            : report.FirstFailure,
                    };
                }
                if (report.Skipped > 0)
                {
                    return new JObject
                    {
                        ["available"] = false,
                        ["reason"] =
                            "collection is partial: " +
                            report.Skipped.ToString(CultureInfo.InvariantCulture) +
                            " native rows were skipped from " + reportName +
                            "; first failure: " +
                            (report.FirstFailure.Length == 0
                                ? "the collector did not publish a failure reason"
                                : report.FirstFailure),
                    };
                }
                break;
            }
            if (!found)
            {
                return new JObject
                {
                    ["available"] = false,
                    ["reason"] =
                        "the publication carries no collection report for " + reportName,
                };
            }
        }

        for (var blockerIndex = 0;
             blockerIndex < category.FailureOnlyReportCategories.Length;
             blockerIndex++)
        {
            var reportName = category.FailureOnlyReportCategories[blockerIndex];
            for (var index = 0; index < world.CollectionCategories.Count; index++)
            {
                var report = world.CollectionCategories[index];
                if (!string.Equals(
                        Normalize(report.Category),
                        reportName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (report.Outcome == WorldCategoryOutcome.Unavailable ||
                    report.Skipped > 0)
                {
                    return new JObject
                    {
                        ["available"] = false,
                        ["reason"] = report.FirstFailure.Length == 0
                            ? "the collector reported " + reportName +
                              " degraded without a failure reason"
                            : report.FirstFailure,
                    };
                }
                break;
            }
        }

        return new JObject
        {
            ["available"] = true,
            ["reason"] = string.Empty,
        };
    }

    private static JObject ProjectRow(GameMcpWorldCategory category, object row)
    {
        var projected = GameMcpObjectProjector.Project(row) as JObject ?? new JObject();
        projected["mcpCategory"] = category.Name;
        projected["expectedNativeType"] = category.ExpectedNativeType;
        return projected;
    }

    private static JObject ProjectScanRow(GameMcpWorldCategory category, object row)
    {
        var complete = GameMcpObjectProjector.Project(row) as JObject ?? new JObject();
        var scan = new JObject();
        for (var index = 0; index < category.ScanFields.Length; index++)
            CopyPath(complete, scan, category.ScanFields[index]);
        return scan;
    }

    private static void CopyPath(JObject source, JObject destination, string path)
    {
        var segments = path.Split('.');
        JToken? value = source;
        for (var index = 0; index < segments.Length; index++)
        {
            value = value?[segments[index]];
            if (value is null) return;
        }

        var target = destination;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (target[segments[index]] is not JObject nested)
            {
                nested = new JObject();
                target[segments[index]] = nested;
            }
            target = nested;
        }
        target[segments[segments.Length - 1]] = value.DeepClone();
    }

    private static bool Matches(JObject row, string query)
    {
        var text = row.ToString();
        return text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool TryCategory(
        string name,
        out GameMcpWorldCategory category,
        out string reason)
    {
        var normalized = Normalize(name);
        if (ByName.TryGetValue(normalized, out category!))
        {
            reason = string.Empty;
            return true;
        }
        category = null!;
        reason = "unknown category '" + (name ?? string.Empty) +
            "'; call world_categories for the exact discoverable names";
        return false;
    }

    private static GameMcpWorldCategory[] CreateCategories()
    {
        var result = new GameMcpWorldCategory[]
        {
            Entity(nameof(GameWorldState.Resources), world => world.Resources),
            Entity(nameof(GameWorldState.Structures), world => world.Structures),
            Entity(nameof(GameWorldState.Upgrades), world => world.Upgrades),
            Entity(nameof(GameWorldState.Research), world => world.Research),
            Entity(nameof(GameWorldState.DoubleVariables), world => world.DoubleVariables),
            Entity(nameof(GameWorldState.IntVariables), world => world.IntVariables),
            Entity(nameof(GameWorldState.BoolVariables), world => world.BoolVariables),
            Entity(nameof(GameWorldState.ModifierVariables), world => world.ModifierVariables),
            Composite(nameof(GameWorldState.PurchaseCosts), world => world.PurchaseCosts),
            Entity(nameof(GameWorldState.AlchemyRecipes), world => world.AlchemyRecipes),
            Entity(nameof(GameWorldState.AlchemyTypes), world => world.AlchemyTypes),
            Entity(nameof(GameWorldState.SpellRecipes), world => world.SpellRecipes),
            Entity(nameof(GameWorldState.SpellTypes), world => world.SpellTypes),
            Entity(nameof(GameWorldState.Equipment), world => world.Equipment),
            Entity(nameof(GameWorldState.EquipmentTypes), world => world.EquipmentTypes),
            Entity(nameof(GameWorldState.ResourceTypes), world => world.ResourceTypes),
            Entity(nameof(GameWorldState.CraftingRecipeTypes), world => world.CraftingRecipeTypes),
            Entity(nameof(GameWorldState.HarvestElements), world => world.HarvestElements),
            Entity(nameof(GameWorldState.HarvestResources), world => world.HarvestResources),
            Entity(nameof(GameWorldState.TimeRunes), world => world.TimeRunes),
            Entity(nameof(GameWorldState.Glyphs), world => world.Glyphs),
            Entity(nameof(GameWorldState.Consumables), world => world.Consumables),
            Entity(nameof(GameWorldState.Rituals), world => world.Rituals),
            Entity(nameof(GameWorldState.Achievements), world => world.Achievements),
            Entity(nameof(GameWorldState.Advancements), world => world.Advancements),
            Entity(nameof(GameWorldState.Challenges), world => world.Challenges),
            Entity(nameof(GameWorldState.ThoughtStreams), world => world.ThoughtStreams),
            Entity(nameof(GameWorldState.Tutorials), world => world.Tutorials),
            Entity(nameof(GameWorldState.Views), world => world.Views),
            Entity(nameof(GameWorldState.PlotNodeActions), world => world.PlotNodeActions),
            Entity(nameof(GameWorldState.PassiveAbilities), world => world.PassiveAbilities),
            Entity(nameof(GameWorldState.Characters), world => world.Characters),
            Entity(nameof(GameWorldState.DiscoveryTrees), world => world.DiscoveryTrees),
            Entity(nameof(GameWorldState.RecipeBooks), world => world.RecipeBooks),
            Entity(nameof(GameWorldState.PlotNodes), world => world.PlotNodes),
            Composite(nameof(GameWorldState.PlotActions), world => world.PlotActions),
            Composite(nameof(GameWorldState.PlotActionInstances), world => world.PlotActionInstances),
            Entity(nameof(GameWorldState.ActionQueues), world => world.ActionQueues),
            Composite(nameof(GameWorldState.ActionQueueSlots), world => world.ActionQueueSlots),
            Composite(nameof(GameWorldState.ActionQueueMembers), world => world.ActionQueueMembers),
            Composite(nameof(GameWorldState.SpellSlots), world => world.SpellSlots),
            Composite(nameof(GameWorldState.SpellCosts), world => world.SpellCosts),
            Composite(nameof(GameWorldState.MasteryExperience), world => world.MasteryExperience),
            Composite(nameof(GameWorldState.ConceptRecipes), world => world.ConceptRecipes),
            Composite(nameof(GameWorldState.AlchemyInstances), world => world.AlchemyInstances),
            Composite(nameof(GameWorldState.AlchemyCosts), world => world.AlchemyCosts),
            Composite(nameof(GameWorldState.PlotAuthoring), world => world.PlotAuthoring),
            Composite(nameof(GameWorldState.PlotPhaseDescriptors), world => world.PlotPhaseDescriptors),
            Composite(nameof(GameWorldState.EffectBlocks), world => world.EffectBlocks),
            Composite(nameof(GameWorldState.EntityRequirements), world => world.EntityRequirements),
            Entity(nameof(GameWorldState.TreasurePools), world => world.TreasurePools),
        };
        Array.Sort(result, static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return result;
    }

    private static GameMcpWorldCategory Entity<TRow>(
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table)
        where TRow : struct, IWorldEntity =>
        new GameMcpEntityCategory<TRow>(
            propertyName,
            table,
            ExpectedNativeType(Normalize(propertyName)),
            RequiredReportCategories(Normalize(propertyName)),
            FailureOnlyReportCategories(Normalize(propertyName)),
            ScanFields(Normalize(propertyName)));

    private static GameMcpWorldCategory Composite<TRow>(
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table)
        where TRow : struct =>
        new GameMcpCompositeCategory<TRow>(
            propertyName,
            table,
            ExpectedNativeType(Normalize(propertyName)),
            RequiredReportCategories(Normalize(propertyName)),
            FailureOnlyReportCategories(Normalize(propertyName)),
            ScanFields(Normalize(propertyName)));

    private static Dictionary<string, GameMcpWorldCategory> IndexCategories()
    {
        var result = new Dictionary<string, GameMcpWorldCategory>(StringComparer.Ordinal);
        for (var index = 0; index < Categories.Length; index++)
            result.Add(Categories[index].Name, Categories[index]);
        return result;
    }

    private static string ExpectedNativeType(string category) => category switch
    {
        "resources" => "ResourceSO",
        "structures" => "StructureSO",
        "upgrades" => "UpgradeSO",
        "research" => "ResearchSO",
        "double-variables" => "DoubleVariable",
        "int-variables" => "IntVariable",
        "bool-variables" => "BoolVariable",
        "modifier-variables" => "ValueModifierVariable",
        "purchase-costs" => "StructureSO|UpgradeSO",
        "alchemy-recipes" => "AlchemyRecipeSO",
        "alchemy-types" => "AlchemyTypeSO",
        "spell-recipes" => "SpellRecipeSO",
        "spell-types" => "SpellTypeSO",
        "equipment" => "EquipmentSO",
        "equipment-types" => "EquipmentTypeSO",
        "resource-types" => "ResourceTypeSO",
        "crafting-recipe-types" => "CraftingRecipeTypeSO",
        "harvest-elements" => "HarvestElementSO",
        "harvest-resources" => "HarvestElementSO",
        "time-runes" => "TimeRuneSO",
        "glyphs" => "GlyphSO",
        "consumables" => "ConsumableSO",
        "rituals" => "RitualSO",
        "achievements" => "AchievementSO",
        "advancements" => "AdvancementSO",
        "challenges" => "ChallengeSO",
        "thought-streams" => "ThoughtStreamSO",
        "tutorials" => "TutorialSO",
        "views" => "ViewSO",
        "plot-node-actions" => "PlotNodeActionSO",
        "passive-abilities" => "PassiveAbilitySO",
        "characters" => "CharacterSO",
        "discovery-trees" => "DiscoveryTreeSO",
        "recipe-books" => "RecipeBookSO",
        "plot-nodes" => "PlotNodeSO",
        "plot-actions" => "PlotNodeSO|PlotNodeActionSO",
        "plot-action-instances" => "PlotNodeActionInstance",
        "action-queues" => "ActionQueueVariable",
        "action-queue-slots" => "PlotNodeActionInstance",
        "action-queue-members" => "StructureSO|UpgradeSO",
        "spell-slots" => "Spell",
        "spell-costs" => "Spell",
        "mastery-experience" => "SpellRecipeSO|AlchemyRecipeSO|EquipmentSO",
        "concept-recipes" => "AlchemyRecipeSO",
        "alchemy-instances" => "AlchemyInstance",
        "alchemy-costs" => "AlchemyInstance",
        "plot-authoring" => "PlotNodeSO",
        "plot-phase-descriptors" => "PlotNodeSO",
        "effect-blocks" => "EffectSO",
        "entity-requirements" => "EntitySO",
        "treasure-pools" => "TreasurePoolSO",
        _ => "not_collected_yet",
    };

    private static string[] RequiredReportCategories(string category) => category switch
    {
        "purchase-costs" => new[]
        {
            "structures",
            "upgrades",
            "resources",
            "modifier-variables",
            "int-variables",
            "structure-costs",
            "upgrade-costs",
        },
        "plot-actions" => new[] { "plot-nodes", "plot-node-actions", "plot-actions" },
        "plot-action-instances" => new[] { "plot-actions" },
        "action-queue-slots" or "action-queue-members" => new[] { "action-queues" },
        "spell-costs" => new[] { "spell-slots" },
        "concept-recipes" or "alchemy-instances" or "alchemy-costs" =>
            new[] { "concept-instances" },
        "plot-phase-descriptors" => new[] { "plot-authoring" },
        "mastery-experience" =>
            new[] { "spell-recipes", "alchemy-recipes", "equipment" },
        _ => new[] { category },
    };

    private static string[] FailureOnlyReportCategories(string category) => category switch
    {
        // The collector emits modifier-folding only when a frame-global modifier could not be
        // reconstructed. Its absence is the clean case; its presence invalidates every row derived
        // with those globals.
        "resources" or "harvest-resources" or "purchase-costs" =>
            new[] { "modifier-folding" },
        _ => Array.Empty<string>(),
    };

    private static string[] ScanFields(string category) => category switch
    {
        "resources" => new[]
        {
            "entityId", "reading.visible", "reading.quantity", "reading.capacity",
            "reading.rate", "reading.usage", "reading.reservation", "trueQuantity",
            "trueRate", "fillFraction", "isCapped", "isAtCapacity", "headroom",
        },
        "structures" => new[]
        {
            "entityId", "reading.unlocked", "reading.level", "reading.queuedLevels",
            "committedLevel", "effectiveLevel", "hasWorkInFlight",
            "developmentProgress", "reading.insufficientReqPenaltyActive",
        },
        "upgrades" => new[]
        {
            "entityId", "reading.available", "reading.level", "reading.maxLevel",
            "reading.queuedLevels", "reading.cachedCostLevel", "committedLevel",
            "remainingLevels", "isExhausted", "isDeveloping", "developmentProgress",
        },
        "research" => new[]
        {
            "entityId", "level", "queuedLevels", "maxLevel", "researchStage",
            "available", "isActive", "isDeveloping", "flagged",
        },
        "double-variables" or "int-variables" =>
            new[] { "entityId", "value", "isPercent" },
        "bool-variables" =>
            new[] { "entityId", "value", "initialValue", "isSaved" },
        "modifier-variables" =>
            new[] { "entityId", "modifierType", "amount", "order" },
        "purchase-costs" => new[]
        {
            "entityId", "resourceId", "amount", "exactGroupedLevels",
            "exactGroupedAmount",
        },
        "alchemy-recipes" => new[]
        {
            "entityId", "coreTypeId", "discovered", "masteryLevel", "masteryXp",
            "maxLevel", "advancementLevel",
        },
        "alchemy-types" => new[]
        {
            "entityId", "selectedLevelId", "level", "maxUsageByMastery",
        },
        "spell-recipes" => new[]
        {
            "entityId", "discovered", "masteryLevel", "masteryLevelReady",
            "masteryXp", "baseCharges", "castSpeed",
        },
        "spell-types" => new[]
        {
            "entityId", "typeLevel", "typeXp", "isVisible", "isElemental",
            "isLoadoutUnique",
        },
        "equipment" => new[]
        {
            "entityId", "isCreated", "masteryLevel", "masteryXp", "equippedLevel",
            "attuningLevel", "attunementTimeLeft",
        },
        "equipment-types" => new[]
        {
            "entityId", "level", "freeLevels", "baseUsage", "masteryLevel",
            "maxTypeSlots",
        },
        "resource-types" =>
            new[] { "entityId", "level", "freeLevels", "specialHidden" },
        "crafting-recipe-types" => new[]
        {
            "entityId", "startingLevel", "maxStartingLevel", "craftVerb",
            "initiated",
        },
        "harvest-elements" => new[]
        {
            "entityId", "masteryLevel", "masteryXp", "instances", "harvestTime",
            "growthTime", "harvestRate",
        },
        "harvest-resources" => new[]
        {
            "entityId", "elementId", "resource.entityId", "resource.reading.visible",
            "resource.trueQuantity", "resource.trueRate",
        },
        "time-runes" => new[]
        {
            "entityId", "discovered", "level", "masteryLevel", "masteryXp", "seen",
        },
        "glyphs" => new[]
        {
            "entityId", "level", "freeLevels", "discovered", "discoverable",
            "maxUsages",
        },
        "consumables" => new[]
        {
            "entityId", "visible", "quantity", "queuedQuantity", "currentPrepTime",
            "currentCooldownTime",
        },
        "rituals" => new[]
        {
            "entityId", "discovered", "inBattle", "activeInstances", "reachedLevel",
            "selectedLevel", "wavesCompleted",
        },
        "achievements" =>
            new[] { "entityId", "level", "seen", "maxLevels" },
        "advancements" =>
            new[] { "entityId", "levels", "xp", "isPersistent" },
        "challenges" => new[]
        {
            "entityId", "level", "state", "seen", "rewardQueued", "maxLevel",
            "difficulty",
        },
        "thought-streams" => new[] { "entityId", "state" },
        "tutorials" => new[] { "entityId", "isCompleted" },
        "views" => new[] { "entityId", "active", "alwaysActive", "available" },
        "plot-node-actions" => new[]
        {
            "entityId", "hasBeenUsed", "isGrowingAction", "elementCost", "baseTime",
            "parallelAction", "prerequisiteCount", "resourceCostCount",
        },
        "passive-abilities" => new[]
        {
            "entityId", "muted", "touched", "hidden", "global", "tokenRate",
        },
        "characters" =>
            new[] { "entityId", "discovered", "numberSlain", "floats" },
        "discovery-trees" => new[]
        {
            "entityId", "actionMode", "actionTime", "rerollsLeft",
            "totalDiscoveredCount", "hasRemainingDiscovery",
            "hasCompletedAllDiscoveries", "selectedChoiceId",
        },
        "recipe-books" => new[] { "entityId", "available" },
        "plot-nodes" => new[]
        {
            "entityId", "reading.visible", "reading.masteryLevel",
            "reading.currentTime", "reading.idleQuantity", "reading.totalQuantity",
            "remainingQuantity", "remainingTotalQuantity",
        },
        "plot-actions" => new[]
        {
            "plotNodeId", "plotNodeActionId", "reading.offeredCount",
            "reading.instanceCount", "reading.prerequisitesConfirmed", "elementCost",
            "elementCostKnown", "hasEnoughForOneInstance",
            "maximumRemainingInstances",
        },
        "plot-action-instances" => new[]
        {
            "plotNodeId", "plotNodeActionId", "ordinal", "quantity", "engaged",
            "empty", "referenceResolved",
        },
        "action-queues" => new[]
        {
            "entityId", "queueId", "slotCount", "usedSlots", "emptySlots",
            "hasEmptySlot", "kind", "totalStacks", "remainingStackRoom",
            "hasStackRoom", "consistent",
        },
        "action-queue-slots" => new[]
        {
            "queueId", "index", "empty", "plotNodeId", "plotNodeActionId",
            "quantity", "engaged",
        },
        "action-queue-members" => new[]
        {
            "queueId", "index", "actionableId", "kind", "stackCount",
            "nativeQueuedCount", "consistency", "actionTime", "actionTimeTotal",
            "buildSpeed", "timingReadable",
        },
        "spell-slots" => new[]
        {
            "slotIndex", "spellRecipeId", "occupied", "casting", "readyingCast",
            "attuning", "toggled", "castReady", "chargeAvailable",
            "resourcesCovered", "currentCharges", "maximumCharges",
            "cooldownRemaining",
        },
        "spell-costs" => new[] { "slotIndex", "kind", "resourceId", "amount" },
        "mastery-experience" => new[]
        {
            "sequence", "domain", "sourceId", "sourceMastery", "sourceEligible",
            "amount",
        },
        "concept-recipes" =>
            new[] { "recipeId", "coreTypeId", "canAddNow" },
        "alchemy-instances" => new[]
        {
            "recipeId", "quantity", "queuedQuantity", "drainReadable", "drainRatio",
        },
        "alchemy-costs" => new[] { "recipeId", "kind", "resourceId", "amount" },
        "plot-authoring" => new[] { "plotNodeId", "autoActionId", "phaseCount" },
        "plot-phase-descriptors" => new[]
        {
            "plotNodeId", "ordinal", "phase", "phaseTimeSeconds", "processType",
            "exitPhase",
        },
        "effect-blocks" => new[]
        {
            "ownerId", "ordinal", "blockTypeName", "effectTypeName", "effectValue",
            "treasurePoolId", "prerequisiteCount", "modCount", "scriptCount",
        },
        "entity-requirements" => new[]
        {
            "ownerId", "ownerKind", "ordinal", "kind", "conditionTypeName",
            "targetId", "reqType", "baseValue",
        },
        "treasure-pools" => new[]
        {
            "entityId", "poolId", "treasuresFound", "partialReward",
            "treasureLevel", "calculatedTreasureLevel",
        },
        _ => throw new InvalidOperationException(
            "world category '" + category + "' has no deliberate MCP scan projection"),
    };

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = new System.Text.StringBuilder(value.Length + 8);
        var previousWasSeparator = true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '-' || character == '_' || char.IsWhiteSpace(character))
            {
                if (!previousWasSeparator) result.Append('-');
                previousWasSeparator = true;
                continue;
            }
            if (char.IsUpper(character) && !previousWasSeparator && result.Length > 0)
                result.Append('-');
            result.Append(char.ToLowerInvariant(character));
            previousWasSeparator = false;
        }
        if (result.Length > 0 && result[result.Length - 1] == '-')
            result.Length--;
        return result.ToString();
    }

    internal static int DefaultLimit => DefaultPageSize;

    private abstract class GameMcpWorldCategory
    {
        protected GameMcpWorldCategory(
            string propertyName,
            string rowTypeName,
            string expectedNativeType,
            string[] reportCategories,
            string[] failureOnlyReportCategories,
            string[] scanFields,
            string identityMode)
        {
            WorldPropertyName = propertyName;
            Name = Normalize(propertyName);
            RowTypeName = rowTypeName;
            ExpectedNativeType = expectedNativeType;
            ReportCategories = reportCategories ??
                throw new ArgumentNullException(nameof(reportCategories));
            if (ReportCategories.Length == 0)
                throw new ArgumentException(
                    "A world category requires at least one collection report.",
                    nameof(reportCategories));
            FailureOnlyReportCategories = failureOnlyReportCategories ??
                throw new ArgumentNullException(nameof(failureOnlyReportCategories));
            ScanFields = scanFields ??
                throw new ArgumentNullException(nameof(scanFields));
            if (ScanFields.Length == 0)
                throw new ArgumentException(
                    "A world category requires a deliberate MCP scan projection.",
                    nameof(scanFields));
            IdentityMode = identityMode;
        }

        internal string Name { get; }
        internal string WorldPropertyName { get; }
        internal string RowTypeName { get; }
        internal string ExpectedNativeType { get; }
        internal string[] ReportCategories { get; }
        internal string[] FailureOnlyReportCategories { get; }
        internal string[] ScanFields { get; }
        internal string IdentityMode { get; }
        internal abstract int Count(GameWorldState world);
        internal abstract object Row(GameWorldState world, int index);
        internal abstract bool TryIdentity(object row, out Guid identity);
    }

    private sealed class GameMcpEntityCategory<TRow> : GameMcpWorldCategory
        where TRow : struct, IWorldEntity
    {
        private readonly Func<GameWorldState, PublicationTable<TRow>> _table;

        internal GameMcpEntityCategory(
            string propertyName,
            Func<GameWorldState, PublicationTable<TRow>> table,
            string expectedNativeType,
            string[] reportCategories,
            string[] failureOnlyReportCategories,
            string[] scanFields)
            : base(
                propertyName,
                typeof(TRow).Name,
                expectedNativeType,
                reportCategories,
                failureOnlyReportCategories,
                scanFields,
                "stable_entity_uuid")
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
        }

        internal override int Count(GameWorldState world) => _table(world).Count;
        internal override object Row(GameWorldState world, int index) => _table(world)[index];
        internal override bool TryIdentity(object row, out Guid identity)
        {
            if (row is TRow typed)
            {
                identity = typed.EntityId;
                return identity != Guid.Empty;
            }
            identity = Guid.Empty;
            return false;
        }
    }

    private sealed class GameMcpCompositeCategory<TRow> : GameMcpWorldCategory
        where TRow : struct
    {
        private readonly Func<GameWorldState, PublicationTable<TRow>> _table;

        internal GameMcpCompositeCategory(
            string propertyName,
            Func<GameWorldState, PublicationTable<TRow>> table,
            string expectedNativeType,
            string[] reportCategories,
            string[] failureOnlyReportCategories,
            string[] scanFields)
            : base(
                propertyName,
                typeof(TRow).Name,
                expectedNativeType,
                reportCategories,
                failureOnlyReportCategories,
                scanFields,
                "composite_guid_fields")
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
        }

        internal override int Count(GameWorldState world) => _table(world).Count;
        internal override object Row(GameWorldState world, int index) => _table(world)[index];
        internal override bool TryIdentity(object row, out Guid identity)
        {
            identity = Guid.Empty;
            return false;
        }
    }
}
#endif
