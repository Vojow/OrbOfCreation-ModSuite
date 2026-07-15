using System.Collections.Generic;

namespace OrbAchievementResonance;

internal enum ResonanceBonusCategory
{
    Speed,
    Power,
    Duration,
    Special,
    ResourceRate,
    ResourceCapacity,
    Casting,
    CastingProgression
}

internal enum ResonanceTargetKind
{
    AttributeGroup,
    ResourceTypeProperty,
    NumberVariable
}

internal sealed class ResonanceTarget
{
    public ResonanceTarget(
        string name,
        string targetUuid,
        string modifierUuid,
        ResonanceBonusCategory category,
        ResonanceTargetKind kind,
        string propertyName,
        string notes)
    {
        Name = name;
        TargetUuid = targetUuid;
        ModifierUuid = modifierUuid;
        Category = category;
        Kind = kind;
        PropertyName = propertyName;
        Notes = notes;
    }

    public string Name { get; }

    public string TargetUuid { get; }

    public string ModifierUuid { get; }

    public ResonanceBonusCategory Category { get; }

    public ResonanceTargetKind Kind { get; }

    public string PropertyName { get; }

    public string Notes { get; }
}

internal static class ResonanceTargetCatalog
{
    public const string AchievementStrengthUuid = "534d8a27-7320-4ca1-8d8c-7eaf0ade385c";

    public static readonly IReadOnlyList<ResonanceTarget> All = new[]
    {
        new ResonanceTarget(
            "GlobalSpeedGroup",
            "8a199f0d-48dd-4c3e-840e-d97a1b7dca4b",
            ResonanceModifierIds.GlobalSpeed,
            ResonanceBonusCategory.Speed,
            ResonanceTargetKind.AttributeGroup,
            "MergingRecord",
            "R1 speed vertical slice target."),

        new ResonanceTarget(
            "AgromancyPowerGroup",
            "026977c7-9d5e-4762-b67a-8df4163c9a51",
            ResonanceModifierIds.AgromancyPower,
            ResonanceBonusCategory.Power,
            ResonanceTargetKind.AttributeGroup,
            "MergingRecord",
            "R2 power component."),
        new ResonanceTarget(
            "AlchemyPowerGroup",
            "0861aea1-4f80-45f3-a190-1cac2533e41c",
            ResonanceModifierIds.AlchemyPower,
            ResonanceBonusCategory.Power,
            ResonanceTargetKind.AttributeGroup,
            "MergingRecord",
            "R2 power component."),
        new ResonanceTarget(
            "ManufacturingPowerGroup",
            "317d234b-354e-41bb-a252-6a280fb506ff",
            ResonanceModifierIds.ManufacturingPower,
            ResonanceBonusCategory.Power,
            ResonanceTargetKind.AttributeGroup,
            "MergingRecord",
            "R2 power component."),
        new ResonanceTarget(
            "MentalPowerGroup",
            "633688bb-983e-4200-9d08-8c87779333f0",
            ResonanceModifierIds.MentalPower,
            ResonanceBonusCategory.Power,
            ResonanceTargetKind.AttributeGroup,
            "MergingRecord",
            "R2 power component."),

        new ResonanceTarget(
            "AllDurationGroup",
            "b096ccd2-7ff4-4ac2-8cc8-da215677e299",
            ResonanceModifierIds.Duration,
            ResonanceBonusCategory.Duration,
            ResonanceTargetKind.AttributeGroup,
            "MergingRecord",
            "Optional duration category; off by default until overlap report is captured."),
        new ResonanceTarget(
            "AllSpecialsGroup",
            "bfed13da-c722-416b-a2fa-a0366a49d156",
            ResonanceModifierIds.Special,
            ResonanceBonusCategory.Special,
            ResonanceTargetKind.AttributeGroup,
            "MergingRecord",
            "Optional special-effects category; off by default until overlap report is captured."),

        new ResonanceTarget(
            "GlobalResourceType.Rate",
            "c8f9e0c8-2b5d-48f6-9ead-27b3eb7389d4",
            ResonanceModifierIds.ResourceRate,
            ResonanceBonusCategory.ResourceRate,
            ResonanceTargetKind.ResourceTypeProperty,
            "Rate",
            "Passive generation only. GainRate is intentionally not silently included."),
        new ResonanceTarget(
            "GlobalCappedResourceType.MaxQuantity",
            "b5a19071-8156-494b-8986-b3c42f37b73e",
            ResonanceModifierIds.ResourceCapacity,
            ResonanceBonusCategory.ResourceCapacity,
            ResonanceTargetKind.ResourceTypeProperty,
            "MaxQuantity",
            "Capped resources only."),

        new ResonanceTarget(
            "SpellCastSpeed",
            "5a83b33b-1bcc-426b-ad80-7a29464511e5",
            ResonanceModifierIds.SpellCastSpeed,
            ResonanceBonusCategory.Casting,
            ResonanceTargetKind.NumberVariable,
            "Value",
            "Direct spell cast-speed variable."),
        new ResonanceTarget(
            "SpellCooldownSpeed",
            "66fa868d-95eb-43ee-b13c-82124dd55d84",
            ResonanceModifierIds.SpellCooldownSpeed,
            ResonanceBonusCategory.Casting,
            ResonanceTargetKind.NumberVariable,
            "Value",
            "Direct spell cooldown-speed variable."),
        new ResonanceTarget(
            "SpellPower",
            "7e096704-cc71-4ca3-924a-64c85d9c11e2",
            ResonanceModifierIds.SpellPower,
            ResonanceBonusCategory.Casting,
            ResonanceTargetKind.NumberVariable,
            "Value",
            "Direct spell power variable."),
        new ResonanceTarget(
            "SpellSpecial",
            "be7609c8-35d8-4c2b-91ed-43135529298e",
            ResonanceModifierIds.SpellSpecial,
            ResonanceBonusCategory.Casting,
            ResonanceTargetKind.NumberVariable,
            "Value",
            "Direct spell special variable."),
        new ResonanceTarget(
            "SpellDuration",
            "e22f9061-43f2-4a8b-bd25-da749c016448",
            ResonanceModifierIds.SpellDuration,
            ResonanceBonusCategory.Casting,
            ResonanceTargetKind.NumberVariable,
            "Value",
            "Direct spell duration variable."),
        new ResonanceTarget(
            "SpellMasteryRate",
            "e5ca51b0-2877-4836-8c6b-861aa24046b0",
            ResonanceModifierIds.SpellMasteryRate,
            ResonanceBonusCategory.CastingProgression,
            ResonanceTargetKind.NumberVariable,
            "Value",
            "Advanced casting progression target; off by default."),
        new ResonanceTarget(
            "SpellExperienceRate",
            "50605aba-eb4e-4afc-a62a-963d5b23639c",
            ResonanceModifierIds.SpellExperienceRate,
            ResonanceBonusCategory.CastingProgression,
            ResonanceTargetKind.NumberVariable,
            "Value",
            "Advanced casting progression target; off by default.")
    };
}
