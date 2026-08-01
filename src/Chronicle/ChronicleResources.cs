using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OrbChronicle;

internal readonly struct ChronicleResourceDefinition
{
    internal ChronicleResourceDefinition(string id, string label, Guid targetId)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A resource KPI ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("A resource KPI label is required.", nameof(label));
        if (targetId == Guid.Empty)
            throw new ArgumentException("A resource KPI target UUID is required.", nameof(targetId));
        Id = id;
        Label = label;
        TargetId = targetId;
    }

    internal string Id { get; }
    internal string Label { get; }
    internal Guid TargetId { get; }
    internal string ExpectedNativeType => "ResourceSO";
}

internal sealed class ChronicleResourceSectionDefinition
{
    internal ChronicleResourceSectionDefinition(
        string id,
        string label,
        string relationship,
        ChronicleResourceDefinition[] resources)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A resource section ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("A resource section label is required.", nameof(label));
        if (string.IsNullOrWhiteSpace(relationship))
            throw new ArgumentException(
                "A resource section relationship is required.",
                nameof(relationship));
        if (resources is null) throw new ArgumentNullException(nameof(resources));
        if (resources.Length == 0)
            throw new ArgumentException("A resource section cannot be empty.", nameof(resources));

        Id = id;
        Label = label;
        Relationship = relationship;
        Resources = Array.AsReadOnly((ChronicleResourceDefinition[])resources.Clone());
    }

    internal string Id { get; }
    internal string Label { get; }
    internal string Relationship { get; }
    internal IReadOnlyList<ChronicleResourceDefinition> Resources { get; }
}

internal static class ChronicleResources
{
    internal const string SchemaId = "orb-feature-resource-discoveries-v2";

    private static readonly ReadOnlyCollection<ChronicleResourceSectionDefinition> Definitions =
        Array.AsReadOnly(new[]
        {
            Section("magic", "Magic", "spell-output",
                Resource("mana", "Mana", "b11072bf-7980-4e23-bc6c-8034ba09b925"),
                Resource("knowledge", "Knowledge", "eda26ca0-afcc-4fc3-9d8a-eb279123353d"),
                Resource("thaumaturgy", "Thaumaturgy", "889d4c5e-ffa4-4130-b14d-bf2cd5061eac"),
                Resource("spark", "Spark", "cc4f8a0f-0081-4702-bd30-134070e09a4f"),
                Resource("space", "Space", "9550808a-433c-4320-a4a4-e66e2858a362"),
                Resource("verdant-energy", "Verdant Energy", "fca918e7-ba2a-4659-a2fb-d5f9371ee37f"),
                Resource("skill", "Skill", "0b311a4c-2246-41c9-a950-a3282a6da3af"),
                Resource("control", "Control", "ed76773c-8485-4d3a-bc53-9b902ebc756e"),
                Resource("water", "Water", "eab888ff-d8bd-4e46-81eb-639d5d562242"),
                Resource("arcanum", "Arcanum", "37163239-1364-4003-9f62-aee3aab86bc1"),
                Resource("blaze", "Blaze", "caab357b-a7df-43a7-88d4-e65724dd2a2b")),
            Section("scholar", "Scholar", "scholar-resource",
                Resource("psi", "Psi", "471e1ce5-18ab-446d-a3bc-fcaa17bda96e")),
            Section("world", "World", "agromancy-output",
                Resource("wood", "Wood", "27250c24-6c28-4858-acca-17e611e6aeb0"),
                Resource("force-bark", "Force Bark", "00943218-d660-4b85-96e3-074221eb5c3e"),
                Resource("magebloom", "Magebloom", "67737b66-cffa-42d3-87e2-6409cdea9c4a"),
                Resource("ironwood", "Ironwood", "c8ca2ba1-263f-48b0-a58f-2fdcff19da86"),
                Resource("dark-thistle", "Dark Thistle", "2b411301-89df-463c-83a1-2b50859c0f58"),
                Resource("dreamberry", "Dreamberry", "5d97a450-cbd6-4745-8e0b-81037542aa58"),
                Resource("ore", "Ore", "d9f0ec7a-357c-4cdf-a3ce-707caa5641dd")),
            Section("workshop", "Workshop", "craft-output",
                Resource("paper", "Paper", "beec818e-91d1-4781-9523-113187377904"),
                Resource("thaumic-scroll", "Thaumic Scroll", "5659824d-c3f8-4ec8-aef3-4263a00a5bd4"),
                Resource("alchemic-scroll", "Alchemic Scroll", "67acd892-8a8a-455a-aa71-3fb06e75bf38"),
                Resource("sigil", "Sigil", "e67fd994-8f98-4488-92e3-fc8a8d1a4584"),
                Resource("dimensional-core", "Dimensional Core", "722fd539-7276-4404-95b1-6f6a13906591"),
                Resource("cognitive-disc", "Cognitive Disc", "604d4952-68e4-4a34-b5d0-e2886bf1d6de"),
                Resource("ingots", "Ingots", "92deba7f-63df-46f8-b379-fb7c9457293c")),
            Section("alchemy", "Alchemy", "alchemy-output",
                Resource("organic-essence", "Organic Essence", "e68be24b-8b23-47fb-8bd4-e42ec76ba4c5"),
                Resource("occult-essence", "Occult Essence", "45cdb9eb-4346-4a62-97ad-084dbeca64fa"),
                Resource("amber", "Amber", "a36f6160-683b-47a8-8182-2507c18e2e87"),
                Resource("tempered-essence", "Tempered Essence", "1c4a291f-8381-40f2-8d0b-574ea2b710da"),
                Resource("elementia", "Elementia", "45c0d72d-8b06-4de7-b4a7-f44a71fd146c"),
                Resource("soul-shard", "Soul Shard", "55758ae7-f938-48a9-abfc-70d00049fb8c"),
                Resource("hexsteel", "Hexsteel", "4970182d-5138-4770-8d95-dfab6597b351")),
            Section("rituals", "Rituals", "ritual-output",
                Resource("zeal", "Zeal", "db61aedb-0143-402f-bdf9-de50b2d59203"),
                Resource("ceremony", "Ceremony", "e2afd744-f7fe-4846-8698-cc9892d0c8f1"),
                Resource("spectral-dust", "Spectral Dust", "2cca401f-0629-4618-ba7e-48cd03963545"),
                Resource("soul", "Soul", "2f0b80ff-b786-4fa8-beda-a80650c4055b"),
                Resource("divine-fragments", "Divine Fragments", "981c23bf-bb1f-403c-9cb0-bb045ba40d60"),
                Resource("beacon", "Beacon", "51122a88-d37e-4590-9bf9-04d9fbf7699e")),
            Section("restoration", "Restoration", "restoration-input",
                Resource("divine-fragments", "Divine Fragments", "981c23bf-bb1f-403c-9cb0-bb045ba40d60"),
                Resource("ore", "Ore", "d9f0ec7a-357c-4cdf-a3ce-707caa5641dd"),
                Resource("ingots", "Ingots", "92deba7f-63df-46f8-b379-fb7c9457293c"),
                Resource("hexsteel", "Hexsteel", "4970182d-5138-4770-8d95-dfab6597b351"),
                Resource("beacon", "Beacon", "51122a88-d37e-4590-9bf9-04d9fbf7699e")),
        });

    static ChronicleResources()
    {
        var sectionIds = new HashSet<string>(StringComparer.Ordinal);
        for (var sectionIndex = 0; sectionIndex < Definitions.Count; sectionIndex++)
        {
            var section = Definitions[sectionIndex];
            if (!sectionIds.Add(section.Id))
                throw new InvalidOperationException("Resource section IDs must be unique.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var targets = new HashSet<Guid>();
            for (var resourceIndex = 0; resourceIndex < section.Resources.Count; resourceIndex++)
            {
                var resource = section.Resources[resourceIndex];
                if (!ids.Add(resource.Id) || !targets.Add(resource.TargetId))
                {
                    throw new InvalidOperationException(
                        "Resource KPI identities must be unique within a feature section.");
                }
            }
        }
    }

    internal static IReadOnlyList<ChronicleResourceSectionDefinition> All => Definitions;
    internal static int Count => Definitions.Count;
    internal static ChronicleResourceSectionDefinition At(int index) => Definitions[index];

    private static ChronicleResourceSectionDefinition Section(
        string id,
        string label,
        string relationship,
        params ChronicleResourceDefinition[] resources) =>
        new(id, label, relationship, resources);

    private static ChronicleResourceDefinition Resource(string id, string label, string uuid) =>
        new(id, label, Guid.Parse(uuid));
}
