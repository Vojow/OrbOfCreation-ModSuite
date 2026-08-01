#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>Read-only queries over one pinned world publication.</summary>
internal static class GameMcpWorldQuery
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 200;
    internal const int MaximumBatchSize = 200;
    private static readonly GameMcpWorldCategory[] Categories = CreateCategories();
    private static readonly Dictionary<string, GameMcpWorldCategory> ByName = IndexCategories();

    internal static JObject Overview(GameMcpFrameContext state)
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
            ["equippedSpellSlots"] = world.SpellSlots.Count,
            ["activeConceptAssignments"] = world.AlchemyInstances.Count,
        };
        return result;
    }

    internal static JObject ListCategories(GameMcpFrameContext state)
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
        GameMcpFrameContext state,
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
        var localizedRequirementCategory = !availability.Available &&
            string.Equals(category.Name, "entity-requirements", StringComparison.Ordinal) &&
            TryLocalizedRequirementFailures(publication.Snapshot, out _, out _);
        if (!availability.Available && !localizedRequirementCategory)
        {
            return NotAvailable(
                publication,
                "category_not_collected",
                availability.Reason.Length == 0
                    ? "the category was not collected"
                    : availability.Reason);
        }

        var world = publication.Snapshot;
        var count = category.Count(world);
        var rows = new JArray();
        var touched = new HashSet<Guid>();
        var end = Math.Min(count, checked(offset + limit));
        for (var index = offset; index < end; index++)
        {
            var row = category.Row(world, index);
            rows.Add(ProjectScanRow(world, category, row));
            if (category.TryIdentity(row, out var identity)) touched.Add(identity);
            else if (row is WorldEntityRequirement requirement)
                touched.Add(requirement.OwnerId);
        }

        var implicated = LocalizedRequirementImplications(world, touched);
        var implicatedOffers = LocalizedDiscoveryOfferImplications(world, touched);
        var incomplete = implicated.Count > 0 || implicatedOffers.Count > 0;

        var result = Envelope(publication);
        result["status"] = incomplete ? "not_available" : "available";
        result["total"] = count;
        if (!incomplete)
        {
            if (rows.Count > 0) result["rows"] = rows;
        }
        else
        {
            result["code"] = "world_list_incomplete";
            result["reason"] =
                implicated.Count > 0 && implicatedOffers.Count > 0
                    ? "this page touches both an unmodeled requirement leaf and an unresolved " +
                      "discovery offer; exact implications are reported"
                    : implicated.Count > 0
                        ? "this page touches an entity with an unmodeled requirement leaf; " +
                          "the exact owner and leaf are reported in implicatedSkippedRows"
                        : "this page contains a discovery offer UUID that is absent from the " +
                          "published explainable entity categories";
            if (rows.Count > 0) result["partialRows"] = rows;
            if (implicated.Count > 0) result["implicatedSkippedRows"] = implicated;
            if (implicatedOffers.Count > 0) result["implicatedOffers"] = implicatedOffers;
        }
        if (end < count) result["nextOffset"] = end;
        return result;
    }

    internal static JObject GetRow(
        GameMcpFrameContext state,
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
        if (!availability.Available)
        {
            return NotAvailable(
                publication,
                "category_not_collected",
                availability.Reason.Length == 0
                    ? "the category was not collected"
                    : availability.Reason);
        }

        var count = category.Count(publication.Snapshot);
        for (var index = 0; index < count; index++)
        {
            var row = category.Row(publication.Snapshot, index);
            if (!category.TryIdentity(row, out var rowIdentity) || rowIdentity != uuid) continue;
            var result = Envelope(publication);
            result["status"] = "available";
            var implicated = LocalizedRequirementImplications(
                publication.Snapshot,
                new HashSet<Guid> { uuid });
            var implicatedOffers = LocalizedDiscoveryOfferImplications(
                publication.Snapshot,
                new HashSet<Guid> { uuid });
            if (implicated.Count == 0 && implicatedOffers.Count == 0)
            {
                result["row"] = ProjectRow(publication.Snapshot, category, row);
            }
            else
            {
                result["status"] = "not_available";
                result["code"] = implicated.Count > 0
                    ? "entity_data_incomplete"
                    : "discovery_offer_read_incomplete";
                result["reason"] =
                    implicated.Count > 0
                        ? "this entity has incomplete published requirement evidence"
                        : "this discovery tree has an offer UUID absent from all published " +
                          "explainable entity categories";
                result["partialRow"] = ProjectRow(publication.Snapshot, category, row);
                if (implicated.Count > 0) result["implicatedSkippedRows"] = implicated;
                if (implicatedOffers.Count > 0) result["implicatedOffers"] = implicatedOffers;
            }
            return result;
        }

        return NotAvailable(
            publication,
            "unknown_uuid",
            "category " + category.Name + " has no row with stable identity " +
            uuid.ToString("D"));
    }

    /// <summary>
    /// Resolve many stable identities from the one immutable publication already pinned by the
    /// router. Results preserve input order; only a failing row repeats its UUID because that
    /// identity is needed to act on the failure. No publication token or state survives this call.
    /// </summary>
    internal static JObject GetRows(
        GameMcpFrameContext state,
        string categoryName,
        IReadOnlyList<string> uuidTexts,
        string expectedNativeType)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        if (!TryCategory(categoryName, out var category, out var reason))
            return NotAvailable(publication, "unknown_category", reason);
        if (uuidTexts is null || uuidTexts.Count == 0 || uuidTexts.Count > MaximumBatchSize)
        {
            return NotAvailable(
                publication,
                "invalid_batch_size",
                "uuids must contain between 1 and " +
                MaximumBatchSize.ToString(CultureInfo.InvariantCulture) + " entries");
        }
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
                "be addressed by UUID; use world_list to read its exact rows");
        }

        var availability = Availability(publication.Snapshot, category);
        if (!availability.Available)
        {
            return NotAvailable(
                publication,
                "category_not_collected",
                availability.Reason.Length == 0
                    ? "the category was not collected"
                    : availability.Reason);
        }

        var results = new JArray();
        for (var inputIndex = 0; inputIndex < uuidTexts.Count; inputIndex++)
        {
            var uuidText = uuidTexts[inputIndex] ?? string.Empty;
            var item = new JObject();
            if (!Guid.TryParseExact(uuidText, "D", out var uuid) || uuid == Guid.Empty)
            {
                item["status"] = "not_available";
                item["code"] = "invalid_uuid";
                item["reason"] = "uuid must be a non-empty canonical D-format GUID";
                item["uuid"] = uuidText;
                results.Add(item);
                continue;
            }

            object? matched = null;
            var count = category.Count(publication.Snapshot);
            for (var rowIndex = 0; rowIndex < count; rowIndex++)
            {
                var row = category.Row(publication.Snapshot, rowIndex);
                if (!category.TryIdentity(row, out var rowIdentity) || rowIdentity != uuid)
                    continue;
                matched = row;
                break;
            }
            if (matched is null)
            {
                item["status"] = "not_available";
                item["code"] = "unknown_uuid";
                item["reason"] = "category " + category.Name +
                    " has no row with stable identity " + uuid.ToString("D");
                item["uuid"] = uuid.ToString("D");
            }
            else
            {
                var implicated = LocalizedRequirementImplications(
                    publication.Snapshot,
                    new HashSet<Guid> { uuid });
                var implicatedOffers = LocalizedDiscoveryOfferImplications(
                    publication.Snapshot,
                    new HashSet<Guid> { uuid });
                if (implicated.Count == 0 && implicatedOffers.Count == 0)
                {
                    item["status"] = "available";
                    item["row"] = ProjectRow(publication.Snapshot, category, matched);
                }
                else
                {
                    item["status"] = "not_available";
                    item["code"] = implicated.Count > 0
                        ? "entity_data_incomplete"
                        : "discovery_offer_read_incomplete";
                    item["reason"] =
                        implicated.Count > 0
                            ? "this entity has incomplete published requirement evidence"
                            : "this discovery tree has an offer UUID absent from all published " +
                              "explainable entity categories";
                    item["partialRow"] = ProjectRow(publication.Snapshot, category, matched);
                    if (implicated.Count > 0) item["implicatedSkippedRows"] = implicated;
                    if (implicatedOffers.Count > 0) item["implicatedOffers"] = implicatedOffers;
                }
            }
            results.Add(item);
        }

        var result = Envelope(publication);
        result["status"] = "available";
        result["results"] = results;
        return result;
    }

    /// <summary>
    /// Projects the state a successful mutation's next read would expose, without wrapping it in a
    /// read envelope. The caller already owns one main-thread frame context and waits for a newer
    /// immutable world before invoking this method.
    /// </summary>
    internal static GameMcpValue ProjectPostState(
        GameMcpFrameContext state,
        string categoryName,
        Guid uuid)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        if (!TryCategory(categoryName, out var category, out var reason))
            return PostStateUnavailable("unknown_category", reason);
        var world = state.World.Snapshot;
        var count = category.Count(world);
        for (var index = 0; index < count; index++)
        {
            var row = category.Row(world, index);
            if (category.TryIdentity(row, out var identity) && identity == uuid)
                return ProjectRow(world, category, row);
        }
        return PostStateUnavailable(
            "post_state_not_published",
            "the newer world has no " + category.Name + " row for the committed target");
    }

    internal static GameMcpValue ProjectEntityState(
        GameWorldState world,
        string categoryName,
        object row)
    {
        if (!TryCategory(categoryName, out var category, out _))
            return new GameMcpDomainValue(row);
        return ProjectRow(world, category, row);
    }

    internal static bool HasDiscoveryPostState(
        GameMcpFrameContext state,
        Guid treeId,
        string mode,
        Guid offerId)
    {
        var world = state.World?.Snapshot;
        if (world is null) return false;
        for (var index = 0; index < world.DiscoveryTrees.Count; index++)
        {
            var tree = world.DiscoveryTrees[index];
            if (tree.EntityId != treeId) continue;
            return mode switch
            {
                "initiate" or "reroll" => tree.ActionMode == 2 && tree.CurrentOfferIds.Count > 0,
                "select" => tree.ActionMode == 2 && tree.SelectedChoiceId == offerId,
                "confirm" => tree.ActionMode == 0 && tree.SelectedChoiceId == Guid.Empty,
                _ => true,
            };
        }
        return false;
    }

    private static GameMcpValue PostStateUnavailable(string reasonCode, string reason) =>
        new JObject
        {
            ["postStateUnavailable"] = new JObject
            {
                ["reasonCode"] = reasonCode,
                ["reason"] = reason,
            },
        }.Freeze();

    internal static JObject Search(
        GameMcpFrameContext state,
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

        // Search is deliberately an entity-catalog surface. Composite diagnostic categories are
        // readable through world_list, where their full identity and localized partiality survive.
        var matches = new JArray();
        var unavailableCategories = new JArray();
        var matchedIdentities = new HashSet<Guid>();
        for (var categoryIndex = 0; categoryIndex < Categories.Length; categoryIndex++)
        {
            var category = Categories[categoryIndex];
            if (!string.Equals(
                    category.IdentityMode,
                    "stable_entity_uuid",
                    StringComparison.Ordinal))
            {
                continue;
            }
            var availability = Availability(publication.Snapshot, category);
            if (!availability.Available)
            {
                unavailableCategories.Add(new JObject
                {
                    ["category"] = category.Name,
                    ["reason"] = availability.Reason,
                });
                continue;
            }
            var count = category.Count(publication.Snapshot);
            for (var rowIndex = 0; rowIndex < count && matches.Count < limit; rowIndex++)
            {
                var row = category.Row(publication.Snapshot, rowIndex);
                if (!Matches(category, row, normalized)) continue;
                if (category.TryIdentity(row, out var identity))
                    matchedIdentities.Add(identity);
                matches.Add(new JObject
                {
                    ["category"] = category.Name,
                    ["expectedNativeType"] = category.ExpectedNativeType,
                    ["row"] = ProjectScanRow(publication.Snapshot, category, row),
                });
            }
        }

        var implicated = LocalizedRequirementImplications(
            publication.Snapshot,
            matchedIdentities);
        var implicatedOffers = LocalizedDiscoveryOfferImplications(
            publication.Snapshot,
            matchedIdentities);
        var incomplete = unavailableCategories.Count > 0 || implicated.Count > 0 ||
            implicatedOffers.Count > 0;
        var result = Envelope(publication);
        result["status"] = incomplete ? "not_available" : "available";
        if (incomplete)
        {
            result["code"] = "world_search_incomplete";
            result["reason"] = unavailableCategories.Count > 0 &&
                (implicated.Count > 0 || implicatedOffers.Count > 0)
                ? "one or more searchable entity categories are incomplete, and returned entities " +
                  "also carry localized incomplete evidence"
                : unavailableCategories.Count > 0
                    ? "one or more searchable entity categories are incomplete"
                    : implicated.Count > 0 && implicatedOffers.Count > 0
                        ? "returned entities touch both implicatedSkippedRows and implicatedOffers"
                        : implicated.Count > 0
                            ? "a returned entity has an unmodeled requirement leaf named in " +
                              "implicatedSkippedRows"
                            : "a returned discovery tree contains an unresolved UUID named in " +
                              "implicatedOffers";
            if (unavailableCategories.Count > 0)
                result["unavailableCategories"] = unavailableCategories;
            if (implicated.Count > 0)
                result["implicatedSkippedRows"] = implicated;
            if (implicatedOffers.Count > 0)
                result["implicatedOffers"] = implicatedOffers;
        }
        if (matches.Count > 0)
            result[incomplete ? "partialMatches" : "matches"] = matches;
        return result;
    }

    internal static JObject NotAvailableWithoutWorld(GameMcpFrameContext state, string code, string reason)
    {
        var result = new JObject
        {
            ["status"] = "not_available",
            ["code"] = code,
            ["reason"] = reason,
        };
        return result;
    }

    internal static JObject WithEnvelope(GameMcpFrameContext state, JObject payload)
    {
        if (state.World is not null &&
            state.World.Generation.Value > 1 &&
            state.World.Snapshot.CollectedAtUtcTicks > 0)
        {
            var envelope = Envelope(state.World);
            envelope.CopyFrom(payload);
            return envelope;
        }

        var result = new JObject
        {
            ["worldGeneration"] = state.World?.Generation.Value ?? 0,
        };
        result.CopyFrom(payload);
        return result;
    }

    internal static GameMcpValue WithEnvelope(GameMcpFrameContext state, GameMcpValue payload)
    {
        if (payload is not GameMcpObject objectPayload)
            throw new ArgumentException("An MCP response payload must be an object.", nameof(payload));
        var result = state.World is not null &&
            state.World.Generation.Value > 1 &&
            state.World.Snapshot.CollectedAtUtcTicks > 0
                ? Envelope(state.World)
                : new JObject
                {
                    ["worldGeneration"] = state.World?.Generation.Value ?? 0,
                };
        result.CopyFrom(objectPayload);
        return result.Freeze();
    }

    private static bool TryWorld(
        GameMcpFrameContext state,
        out WorldPublication<GameWorldState> publication,
        out JObject unavailable)
    {
        if (state.World is not null &&
            state.World.Generation.Value > 1 &&
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

    private static JObject Envelope(WorldPublication<GameWorldState> publication)
    {
        return new JObject
        {
            ["worldGeneration"] = publication.Generation.Value,
        };
    }

    private static JObject NotAvailable(
        WorldPublication<GameWorldState> publication,
        string code,
        string reason)
    {
        var result = Envelope(publication);
        result["status"] = "not_available";
        result["code"] = code;
        result["reason"] = reason;
        return result;
    }

    private static JObject CompactCollectionStatus(GameWorldState world)
    {
        var categories = new JArray();
        for (var index = 0; index < Categories.Length; index++)
            categories.Add(Categories[index].Name);
        var unavailable = new JArray();
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            var category = world.CollectionCategories[index];
            if (category.IsClean) continue;
            unavailable.Add(new JObject
            {
                ["category"] = Normalize(category.Category),
                ["skipped"] = category.Skipped,
                ["reason"] = category.FirstFailure,
            });
        }
        var result = new JObject
        {
            ["complete"] = IsCollectionComplete(world),
            ["categories"] = categories,
        };
        if (unavailable.Count > 0) result["unavailableCategories"] = unavailable;
        if (TryLocalizedRequirementFailures(world, out var implicated, out _))
        {
            var skipped = new JArray();
            for (var index = 0; index < implicated.Length; index++)
            {
                var leaf = implicated[index];
                skipped.Add(new JObject
                {
                    ["ownerId"] = leaf.OwnerId.ToString("D"),
                    ["ordinal"] = leaf.Ordinal,
                    ["nativeType"] = leaf.ConditionTypeName,
                });
            }
            if (skipped.Count > 0) result["skippedEntities"] = skipped;
        }
        return result;
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
        var result = new JObject
        {
            ["name"] = category.Name,
            ["expectedNativeType"] = category.ExpectedNativeType,
            ["count"] = category.Count(world),
            ["available"] = availability.Available,
            ["identityMode"] = category.IdentityMode,
        };
        if (availability.Reason.Length > 0) result["reason"] = availability.Reason;
        return result;
    }

    private static GameMcpCategoryAvailability Availability(
        GameWorldState world,
        GameMcpWorldCategory category)
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
                    return new GameMcpCategoryAvailability(
                        false,
                        report.FirstFailure.Length == 0
                            ? "the collector reported " + reportName + " unavailable"
                            : report.FirstFailure);
                }
                if (report.Skipped > 0)
                {
                    return new GameMcpCategoryAvailability(
                        false,
                        "collection is partial: " +
                        report.Skipped.ToString(CultureInfo.InvariantCulture) +
                        " native rows were skipped from " + reportName +
                        "; first failure: " +
                        (report.FirstFailure.Length == 0
                            ? "the collector did not publish a failure reason"
                            : report.FirstFailure));
                }
                break;
            }
            if (!found)
            {
                return new GameMcpCategoryAvailability(
                    false,
                    "the publication carries no collection report for " + reportName);
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
                    return new GameMcpCategoryAvailability(
                        false,
                        report.FirstFailure.Length == 0
                            ? "the collector reported " + reportName +
                              " degraded without a failure reason"
                            : report.FirstFailure);
                }
                break;
            }
        }

        return new GameMcpCategoryAvailability(true, string.Empty);
    }

    private static GameMcpValue ProjectRow(
        GameWorldState world,
        GameMcpWorldCategory category,
        object row) =>
        row is WorldResource resource
            ? ProjectResource(in resource)
            : row is WorldPurchaseCost purchaseCost
            ? ProjectPurchaseCost(in purchaseCost)
            : row is WorldCraftingRecipe craftingRecipe
            ? ProjectCraftingRecipe(in craftingRecipe)
            : row is WorldDiscoveryTree tree
            ? ProjectDiscoveryTree(world, in tree)
            : new GameMcpProjectedDomainValue(
                row,
                category.ScanFields,
                category.Name,
                category.ExpectedNativeType);

    private static GameMcpValue ProjectDiscoveryTree(
        GameWorldState world,
        in WorldDiscoveryTree tree)
    {
        var result = new JObject
        {
            ["entityId"] = tree.EntityId.ToString("D"),
            ["category"] = "discovery-trees",
            ["nativeType"] = "DiscoveryTreeSO",
            ["mode"] = DiscoveryMode(tree.ActionMode),
            ["rerollsLeft"] = tree.RerollsLeft,
            ["discoveredCount"] = tree.TotalDiscoveredCount,
            ["hasRemainingDiscoveries"] = tree.HasRemainingDiscovery ||
                tree.HasImmediateRequiredDiscovery,
        };

        if (tree.ActionMode == 1)
            result["actionTime"] = new GameMcpDomainValue(tree.ActionTime);
        if (tree.SelectedChoiceId != Guid.Empty)
            result["selectedOfferUuid"] = tree.SelectedChoiceId.ToString("D");

        if (tree.ActionMode == 0)
        {
            var hasNext = tree.HasRemainingDiscovery || tree.HasImmediateRequiredDiscovery;
            var available = tree.Visible && hasNext && tree.NextItemAffordable;
            var initiate = new JObject
            {
                ["available"] = available,
            };
            if (!available)
            {
                initiate["reasonCode"] = !tree.Visible
                    ? "tree_unavailable"
                    : !hasNext
                        ? "no_discoveries"
                        : "unaffordable";
            }
            if (hasNext)
            {
                if (tree.NextItemCosts.Count > 0)
                {
                    var costs = new JArray();
                    for (var index = 0; index < tree.NextItemCosts.Count; index++)
                    {
                        var cost = tree.NextItemCosts[index];
                        costs.Add(new JObject
                        {
                            ["resourceId"] = cost.ResourceId.ToString("D"),
                            ["cost"] = new GameMcpDomainValue(cost.Amount),
                            ["amount"] = new GameMcpDomainValue(cost.AvailableAmount),
                        });
                    }
                    initiate["costs"] = costs;
                }
            }
            result["initiate"] = initiate;
        }

        if (tree.ActionMode == 2)
        {
            if (tree.CurrentOfferIds.Count > 0)
            {
                var offers = new JArray();
                for (var index = 0; index < tree.CurrentOfferIds.Count; index++)
                {
                    var id = tree.CurrentOfferIds[index];
                    var offer = new JObject
                    {
                        ["uuid"] = id.ToString("D"),
                    };
                    if (GameMcpEntityExplainer.TryDescribePublishedEntity(
                            world, id, out var category, out var nativeType, out _))
                    {
                        offer["category"] = category;
                        offer["expectedNativeType"] = nativeType;
                    }
                    offers.Add(offer);
                }
                result["offers"] = offers;
            }

        }
        if (tree.ActionMode == 2)
            result["rerollAvailable"] = tree.Visible &&
                !tree.HasImmediateRequiredDiscovery &&
                tree.RerollsLeft > 0 && tree.CurrentOfferIds.Count > 0 &&
                !tree.UsedRerollsLastDiscover;
        return result.Freeze();
    }

    private static GameMcpValue ProjectResource(in WorldResource resource) =>
        new JObject
        {
            ["entityId"] = resource.EntityId.ToString("D"),
            ["category"] = "resources",
            ["nativeType"] = "ResourceSO",
            ["amount"] = new GameMcpDomainValue(resource.TrueQuantity),
            ["capacity"] = new GameMcpDomainValue(resource.Reading.Capacity),
            ["netRate"] = new GameMcpDomainValue(resource.TrueRate),
            ["atCapacity"] = resource.IsAtCapacity,
        }.Freeze();

    internal static GameMcpValue ProjectPurchaseCost(in WorldPurchaseCost cost)
    {
        var result = new JObject
        {
            ["targetId"] = cost.EntityId.ToString("D"),
            ["resourceId"] = cost.ResourceId.ToString("D"),
            ["baseCost"] = new GameMcpDomainValue(cost.BaseExactAmount),
            ["effectiveCost"] = new GameMcpDomainValue(cost.EffectiveExactAmount),
        };
        if (cost.ExactGroupedLevels > 1)
        {
            result["groupLevels"] = cost.ExactGroupedLevels;
            result["groupCost"] = new GameMcpDomainValue(cost.ExactGroupedAmount);
        }
        if (cost.ModifierSources.Count > 0)
        {
            var modifiers = new JArray();
            for (var index = 0; index < cost.ModifierSources.Count; index++)
            {
                var source = cost.ModifierSources[index];
                modifiers.Add(new JObject
                {
                    ["sourceId"] = source.SourceId.ToString("D"),
                    ["effect"] = source.ValueMeaning,
                    ["value"] = new GameMcpDomainValue(source.Value),
                });
            }
            result["costModifiers"] = modifiers;
        }
        if (cost.AffordabilityEvaluated)
        {
            result["amount"] = new GameMcpDomainValue(cost.AvailableAmount);
            result["totalCost"] = new GameMcpDomainValue(cost.CombinedEffectiveAmount);
            result["resourceAffordable"] = cost.ResourceAffordable;
            result["purchaseAffordable"] = cost.Affordable;
            if (!cost.ResourceAffordable)
                result["resourceReasonCode"] = cost.ResourceAffordabilityReasonCode;
            if (!cost.Affordable)
                result["purchaseReasonCode"] = cost.AffordabilityReasonCode;
        }
        return result.Freeze();
    }

    private static GameMcpValue ProjectCraftingRecipe(in WorldCraftingRecipe recipe)
    {
        var reading = recipe.Reading;
        var result = new JObject
        {
            ["entityId"] = recipe.EntityId.ToString("D"),
            ["category"] = "crafting-recipes",
            ["nativeType"] = "CraftingRecipeSO",
            ["visible"] = reading.Visible,
            ["startingAmount"] = new GameMcpDomainValue(reading.StartingQuantity),
            ["craftTimeSeconds"] = reading.TimeToComplete,
            ["canStart"] = reading.Visible &&
                reading.CanBuyAtStartingQuantity && reading.OutputWithinCapacity,
        };
        if (reading.UseQuantityAsLevel) result["amountActsAsLevel"] = true;
        if (!reading.Visible || !reading.CanBuyAtStartingQuantity || !reading.OutputWithinCapacity)
        {
            var blockers = new JArray();
            if (!reading.Visible) blockers.Add("hidden_or_undiscovered");
            if (!reading.CanBuyAtStartingQuantity) blockers.Add("native_purchase_refused");
            if (!reading.OutputWithinCapacity) blockers.Add("output_capacity_blocked");
            result["blockers"] = blockers;
        }
        if (recipe.Types.Count > 0)
        {
            var types = new JArray();
            for (var index = 0; index < recipe.Types.Count; index++)
                types.Add(recipe.Types[index].TypeId.ToString("D"));
            result["types"] = types;
        }
        if (recipe.Resources.Count > 0)
        {
            var inputs = new JArray();
            var outputs = new JArray();
            for (var index = 0; index < recipe.Resources.Count; index++)
            {
                var resource = recipe.Resources[index];
                var projected = new JObject
                {
                    ["resourceId"] = resource.ResourceId.ToString("D"),
                };
                if (resource.Kind == WorldCraftingRecipeResourceKind.AuthoredInput)
                    projected["cost"] = new GameMcpDomainValue(resource.Amount);
                else
                    projected["yield"] = new GameMcpDomainValue(resource.Amount);
                if (resource.ResourceStateAvailable)
                {
                    projected["amount"] = new GameMcpDomainValue(
                        resource.BandwidthResource ? resource.Headroom : resource.TrueQuantity);
                    if (resource.IsCapped)
                        projected["capacity"] = new GameMcpDomainValue(resource.Capacity);
                    if (resource.Kind == WorldCraftingRecipeResourceKind.AuthoredInput)
                    {
                        projected["affordable"] =
                            (resource.BandwidthResource ? resource.Headroom : resource.TrueQuantity)
                            .CompareTo(resource.Amount) >= 0;
                    }
                }
                if (resource.BandwidthResource) projected["bandwidth"] = true;
                if (!resource.Visible) projected["hidden"] = true;
                if (resource.Kind == WorldCraftingRecipeResourceKind.AuthoredInput)
                    inputs.Add(projected);
                else
                    outputs.Add(projected);
            }
            if (inputs.Count > 0) result["inputs"] = inputs;
            if (outputs.Count > 0) result["outputs"] = outputs;
        }
        if (recipe.ConsumableOutputs.Count > 0)
        {
            var consumables = new JArray();
            for (var index = 0; index < recipe.ConsumableOutputs.Count; index++)
                consumables.Add(recipe.ConsumableOutputs[index].ConsumableId.ToString("D"));
            result["consumableOutputs"] = consumables;
        }
        var drainBlockers = new JArray();
        for (var index = 0; index < recipe.DrainBlocks.Count; index++)
        {
            var drain = recipe.DrainBlocks[index];
            if (!drain.Blocked) continue;
            drainBlockers.Add(new JObject
            {
                ["reasonCode"] = "engagement_drain_limited",
                ["availableRatio"] = new GameMcpDomainValue(drain.NecessaryRatio),
            });
        }
        if (drainBlockers.Count > 0) result["drainBlockers"] = drainBlockers;
        return result.Freeze();
    }

    private static string DiscoveryMode(int mode) => mode switch
    {
        0 => "idle",
        1 => "crafting",
        2 => "choice",
        _ => "unknown_" + mode.ToString(CultureInfo.InvariantCulture),
    };

    private static JArray LocalizedRequirementImplications(
        GameWorldState world,
        HashSet<Guid> touchedIdentities)
    {
        var result = new JArray();
        if (touchedIdentities.Count == 0 ||
            !TryLocalizedRequirementFailures(world, out var failures, out var collectorReason))
        {
            return result;
        }

        for (var index = 0; index < failures.Length; index++)
        {
            var failure = failures[index];
            if (!touchedIdentities.Contains(failure.OwnerId)) continue;
            result.Add(new JObject
            {
                ["category"] = "entity-requirements",
                ["reasonCode"] = "unmodeled_requirement_leaf",
                ["ownerUuid"] = failure.OwnerId.ToString("D"),
                ["ownerKind"] = failure.OwnerKind.ToString(),
                ["containerIndex"] = failure.ContainerIndex,
                ["ordinal"] = failure.Ordinal,
                ["parentOrdinal"] = failure.ParentOrdinal,
                ["conditionTypeName"] = failure.ConditionTypeName,
                ["collectorReason"] = collectorReason,
            });
        }
        return result;
    }

    private static JArray LocalizedDiscoveryOfferImplications(
        GameWorldState world,
        HashSet<Guid> touchedIdentities)
    {
        var result = new JArray();
        if (touchedIdentities.Count == 0) return result;
        for (var treeIndex = 0; treeIndex < world.DiscoveryTrees.Count; treeIndex++)
        {
            var tree = world.DiscoveryTrees[treeIndex];
            if (!touchedIdentities.Contains(tree.EntityId)) continue;
            for (var offerIndex = 0; offerIndex < tree.CurrentOfferIds.Count; offerIndex++)
            {
                var offerId = tree.CurrentOfferIds[offerIndex];
                if (GameMcpEntityExplainer.TryDescribePublishedEntity(
                        world, offerId, out _, out _, out _))
                {
                    continue;
                }
                result.Add(new JObject
                {
                    ["treeUuid"] = tree.EntityId.ToString("D"),
                    ["ordinal"] = offerIndex,
                    ["offerUuid"] = offerId.ToString("D"),
                    ["reasonCode"] = "offer_not_in_explainable_world",
                });
            }
        }
        return result;
    }

    /// <summary>
    /// The entity-requirements reader deliberately publishes every unmodeled leaf with its owner.
    /// It is safe to localize the category's skipped count only when those published rows account
    /// for the entire count. A thrown read has no owner row and therefore remains category-global.
    /// </summary>
    private static bool TryLocalizedRequirementFailures(
        GameWorldState world,
        out WorldEntityRequirement[] failures,
        out string collectorReason)
    {
        collectorReason = string.Empty;
        var skipped = 0;
        var reportFound = false;
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            var report = world.CollectionCategories[index];
            if (!string.Equals(
                    Normalize(report.Category),
                    "entity-requirements",
                    StringComparison.Ordinal))
            {
                continue;
            }
            reportFound = true;
            if (report.Outcome != WorldCategoryOutcome.Collected || report.Skipped <= 0)
            {
                failures = Array.Empty<WorldEntityRequirement>();
                return false;
            }
            skipped = report.Skipped;
            collectorReason = report.FirstFailure.Length == 0
                ? "the collector did not publish a failure reason"
                : report.FirstFailure;
            break;
        }
        if (!reportFound)
        {
            failures = Array.Empty<WorldEntityRequirement>();
            return false;
        }

        var localized = new List<WorldEntityRequirement>();
        for (var index = 0; index < world.EntityRequirements.Count; index++)
        {
            var row = world.EntityRequirements[index];
            if (row.NodeKind == WorldRequirementNodeKind.Leaf &&
                row.Kind == WorldRequirementConditionKind.Unknown)
            {
                localized.Add(row);
            }
        }
        if (localized.Count != skipped)
        {
            failures = Array.Empty<WorldEntityRequirement>();
            return false;
        }
        failures = localized.ToArray();
        return true;
    }

    private static GameMcpValue ProjectScanRow(
        GameWorldState world,
        GameMcpWorldCategory category,
        object row) =>
        row is WorldResource resource
            ? ProjectResource(in resource)
            : row is WorldPurchaseCost purchaseCost
            ? ProjectPurchaseCost(in purchaseCost)
            : row is WorldCraftingRecipe craftingRecipe
            ? ProjectCraftingRecipe(in craftingRecipe)
            : row is WorldDiscoveryTree tree
            ? ProjectDiscoveryTree(world, in tree)
            : new GameMcpProjectedDomainValue(
                row,
                category.ScanFields,
                category.Name,
                category.ExpectedNativeType);

    private static bool Matches(GameMcpWorldCategory category, object row, string query)
    {
        if (category.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            category.RowTypeName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            category.ExpectedNativeType.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
        return category.TryIdentity(row, out var identity) &&
            GameMcpEntityCatalog.Matches(identity, query);
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

    /// <summary>Shares the exact world-query completeness rule with composite diagnostic tools.</summary>
    internal static bool TryCategoryAvailability(
        GameWorldState world,
        string name,
        out string reason)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (!TryCategory(name, out var category, out reason)) return false;
        var availability = Availability(world, category);
        if (availability.Available)
        {
            reason = string.Empty;
            return true;
        }
        reason = availability.Reason.Length == 0
            ? "category collection was incomplete"
            : availability.Reason;
        return false;
    }

    private readonly struct GameMcpCategoryAvailability
    {
        internal GameMcpCategoryAvailability(bool available, string reason)
        {
            Available = available;
            Reason = reason ?? string.Empty;
        }

        internal bool Available { get; }
        internal string Reason { get; }
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
            Entity(nameof(GameWorldState.CraftingRecipes), world => world.CraftingRecipes),
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
            Composite(
                nameof(GameWorldState.RequirementNativeVerdicts),
                world => world.RequirementNativeVerdicts),
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

    private static string ExpectedNativeType(string category) =>
        GameMcpEntityCapabilityMap.ExpectedNativeType(category);

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
        "action-queue-slots" => new[] { "action-queues" },
        "spell-costs" => new[] { "spell-slots" },
        "concept-recipes" or "alchemy-instances" or "alchemy-costs" =>
            new[] { "concept-instances" },
        "plot-phase-descriptors" => new[] { "plot-authoring" },
        "mastery-experience" =>
            new[] { "spell-recipes", "alchemy-recipes", "equipment" },
        "crafting-recipes" => new[]
        {
            "crafting-recipes",
            "crafting-recipe-state",
            "resources",
        },
        "requirement-native-verdicts" => new[] { "requirement-native-verdicts" },
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
            "available", "visible", "complete", "canDevelop", "withinDevelopRange",
            "meetsLevelRequirements", "stillHasLeeway", "belowArtificialMaxLevel",
            "belowMaxInvestmentLevel", "isActive", "isDeveloping", "flagged",
            "purchasedLevels", "baseLevel", "bonusLevel", "totalLevel", "artificialMaxLevel",
            "baseRequirementLevel", "effectiveRequirementLevel",
            "requirementLevelAdjustment", "requirementAdjustments",
        },
        "double-variables" or "int-variables" =>
            new[] { "entityId", "value", "isPercent" },
        "bool-variables" =>
            new[] { "entityId", "value", "initialValue", "isSaved" },
        "modifier-variables" =>
            new[] { "entityId", "modifierType", "amount", "order" },
        "purchase-costs" => new[]
        {
            "entityId", "resourceId", "baseExactAmount", "effectiveExactAmount",
            "amount", "exactGroupedLevels", "exactGroupedAmount", "modifierSources",
            "affordabilityEvaluated", "availableAmount", "combinedEffectiveAmount",
            "resourceAffordable", "resourceAffordabilityReasonCode", "affordable",
            "affordabilityReasonCode",
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
        "crafting-recipes" => new[]
        {
            "entityId", "reading.visible", "reading.visibilityReasonCode",
            "reading.canBuyAtStartingQuantity", "reading.nativePurchaseReasonCode",
            "reading.startingQuantity", "reading.outputWithinCapacity",
            "reading.outputCapacityReasonCode", "reading.authoredInputCount",
            "reading.generatedOutputCount", "reading.consumableOutputCount",
            "reading.engagementEffectCount",
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
            "hasEmptySlot", "consistent",
        },
        "action-queue-slots" => new[]
        {
            "queueId", "index", "empty", "plotNodeId", "plotNodeActionId",
            "quantity", "engaged",
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
        "requirement-native-verdicts" => new[]
        {
            "entityId", "ownerKind", "checkLevel", "met",
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

    internal static string[] RegisteredCategoryNames()
    {
        var result = new string[Categories.Length];
        for (var index = 0; index < Categories.Length; index++)
            result[index] = Categories[index].Name;
        return result;
    }

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
