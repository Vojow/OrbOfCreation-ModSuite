using System;
using System.Collections.Generic;

namespace OrbAchievementResonance;

internal static class ResonanceModifierIds
{
    public const string GlobalSpeed = "ff328165-79dd-4c9e-afe4-2a40cf3d92ab";
    public const string AgromancyPower = "23cde6ff-36d0-4dae-8d6a-08d2a324ae9f";
    public const string AlchemyPower = "45a382fc-ff3d-4288-ac25-57391777b655";
    public const string ManufacturingPower = "1de785fd-7e93-498d-b83a-07cafb69bb95";
    public const string MentalPower = "678c8a1d-3344-4649-9c6b-20844420c396";
    public const string Duration = "ac8f550b-d625-435c-b46d-b5145c60813c";
    public const string Special = "faa4a4cb-b4f7-4188-9d42-f9bbccbda449";
    public const string ResourceRate = "d8c8dd1f-536a-478c-a00c-e1d6ec2adc89";
    public const string ResourceCapacity = "6f5bfc8c-3b08-468a-8577-9511833dccc6";
    public const string SpellCastSpeed = "640f4012-92eb-4d24-baa7-1d5266164da6";
    public const string SpellCooldownSpeed = "4cdcedf7-d18c-4d2e-b1e8-9c0fc37aa714";
    public const string SpellPower = "64c93f99-8bec-447a-9899-1c94bc713696";
    public const string SpellSpecial = "72fc39a8-cc92-4af6-ad65-14b9ff41a0b4";
    public const string SpellDuration = "76d7a04d-6333-45e1-9d57-9f3226bbb11d";
    public const string SpellMasteryRate = "9de9b6b0-b1ca-4852-b426-ee123736c8c2";
    public const string SpellExperienceRate = "c79245db-80d5-491d-9676-6f91da1c719e";

    private static readonly HashSet<string> Owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        GlobalSpeed,
        AgromancyPower,
        AlchemyPower,
        ManufacturingPower,
        MentalPower,
        Duration,
        Special,
        ResourceRate,
        ResourceCapacity,
        SpellCastSpeed,
        SpellCooldownSpeed,
        SpellPower,
        SpellSpecial,
        SpellDuration,
        SpellMasteryRate,
        SpellExperienceRate
    };

    public static bool IsOwned(string? uuid)
    {
        return uuid is not null && Owned.Contains(uuid);
    }
}
