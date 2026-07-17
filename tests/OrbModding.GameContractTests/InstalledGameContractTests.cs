using System;
using System.Linq;
using OrbModding.Common;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class InstalledGameContractTests
{
    [GameAssemblyFact]
    public void InstalledAssemblies_MatchAuditedHashes()
    {
        var paths = GameAssemblyPaths.Require();

        var result = GameAssemblyAudit.Check(paths.GameRoot);

        Assert.True(result.AssemblyCSharp.MatchesExpected, FormatMismatch(result.AssemblyCSharp));
        Assert.True(result.AssemblyCSharpFirstPass.MatchesExpected, FormatMismatch(result.AssemblyCSharpFirstPass));
    }

    [GameAssemblyFact]
    public void PlayerAndSaveHooks_MatchRuntimeContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        AssertMethod(assembly, "Player", "ManagerStart", false, "System.Void");
        AssertMethod(assembly, "Player", "GetAchievementLevel", true, "IntVariable");
        AssertMethod(assembly, "SaveStateManager", "CollectJsonData", false, "System.String");
        AssertMethod(assembly, "SaveStateManager", "ImplementLoadedJson", false, "System.Void");
        AssertMethod(
            assembly,
            "SaveStateManager",
            "WriteFileAndBackupAsync",
            false,
            "System.Threading.Tasks.Task",
            "System.String",
            "System.String",
            "System.String");
    }

    [GameAssemblyFact]
    public void ResearchAutomation_MatchesReadAndActionContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        AssertMethod(assembly, "ResearchSO", "CanDevelop", false, "System.Boolean");
        AssertMethod(assembly, "ResearchSO", "GetDevelopError", false, "System.String");
        AssertMethod(assembly, "ResearchSO", "GetDevelopmentCost", false, "ResourceCostList");
        AssertMethod(assembly, "ResearchSO", "Develop", false, "System.Void");
        AssertMethod(assembly, "ResearchSO", "IsVisible", false, "System.Boolean");
        AssertMethod(assembly, "ResearchSO", "IsComplete", false, "System.Boolean");
        AssertMethod(assembly, "ResearchSO", "IsDeveloping", false, "System.Boolean");
        AssertMethod(assembly, "ResearchSO", "GetQueuedLevels", false, "System.Int32");
        AssertMethod(assembly, "ResourceCostList", "GetEntries", false, "System.Collections.Generic.List`1<ResourceTuple>");
        Assert.Equal("ResourceSO", assembly.GetFieldType("ResourceTuple", "resource"));
        AssertMethod(assembly, "ResourceTuple", "GetValue", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueQuantity", false, "BigDouble");
    }

    [GameAssemblyFact]
    public void AutoBuy_MatchesNativePurchaseContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("System.Collections.Generic.List`1<StructureSO>", assembly.GetFieldType("StructureSO", "All"));
        AssertMethod(assembly, "StructureSO", "IsAvailable", false, "System.Boolean");
        AssertMethod(assembly, "StructureSO", "CanPurchase", false, "System.Boolean");
        AssertMethod(assembly, "StructureSO", "GetPurchaseCost", false, "ResourceCostList");
        AssertMethod(assembly, "StructureSO", "Purchase", false, "System.Void", "System.Boolean");
        AssertMethod(assembly, "StructureSO", "GetPurchaseLevel", false, "System.Int32");
        AssertMethod(assembly, "StructureSO", "GetQueuedQuantity", false, "System.Int32");
        AssertMethod(assembly, "StructureSO", "QueueBuild", false, "System.Void", "System.Int32");
        AssertMethod(assembly, "StructureSO", "CompleteAction", false, "System.Void");

        Assert.Equal("System.Collections.Generic.List`1<UpgradeSO>", assembly.GetFieldType("UpgradeSO", "All"));
        AssertMethod(assembly, "UpgradeSO", "IsAvailable", false, "System.Boolean");
        AssertMethod(assembly, "UpgradeSO", "CanPurchase", false, "System.Boolean");
        AssertMethod(assembly, "UpgradeSO", "GetPurchaseCost", false, "ResourceCostList");
        AssertMethod(assembly, "UpgradeSO", "GetLeveledCostList", false, "ResourceCostList", "System.Int32");
        AssertMethod(assembly, "UpgradeSO", "Purchase", false, "System.Void");
        AssertMethod(assembly, "UpgradeSO", "GetPurchaseLevel", false, "System.Int32");
        AssertMethod(assembly, "UpgradeSO", "GetQueuedPurchaseLevel", false, "System.Int32");
        AssertMethod(assembly, "UpgradeSO", "HasFiniteLevels", false, "System.Boolean");
        AssertMethod(assembly, "UpgradeSO", "IsMaxLevel", false, "System.Boolean");
        AssertMethod(assembly, "UpgradeSO", "IsMaxQueuedLevel", false, "System.Boolean");
        AssertMethod(assembly, "UpgradeSO", "CompleteAction", false, "System.Void");

        AssertMethod(assembly, "ActionManager", "GetRemainingRoom", true, "System.Int32");
        AssertMethod(assembly, "GlobalVariables", "GetMultiBuy", true, "IntVariable");
        AssertMethod(assembly, "Player", "GetBulkDevelopment", true, "IntVariable");
        AssertMethod(assembly, "IntVariable", "AsInt", false, "System.Int32");
        AssertMethod(assembly, "IntVariable", "SetValue", false, "System.Void", "System.Int32");
        Assert.Equal("System.Collections.Generic.List`1<ResourceTuple>", assembly.GetFieldType("ResourceCostList", "costs"));
        AssertMethod(assembly, "ResourceCostList", "GetEntries", false, "System.Collections.Generic.List`1<ResourceTuple>");
        Assert.Equal("ResourceSO", assembly.GetFieldType("ResourceTuple", "resource"));
        AssertMethod(assembly, "ResourceTuple", "GetValue", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetQuantity", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueQuantity", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueSpend", false, "BigDouble", "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueAmount", false, "BigDouble", "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetAttributeCostMod", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "IsAvailable", false, "System.Boolean");
        Assert.Equal(
            "System.Collections.Generic.List`1<PersistentEffectDeprecated+Property>",
            assembly.GetFieldType("StructureSO", "structureProperties"));
        Assert.Equal(
            "System.Collections.Generic.List`1<ResourceSO+PersistentEffect>",
            assembly.GetFieldType("PersistentEffectDeprecated", "resourceEffects"));
        Assert.Equal(
            "System.Collections.Generic.List`1<UpgradeableObject+UpgradeEffectModifier>",
            assembly.GetFieldType("PersistentEffectDeprecated", "upgradeableObjectEffects"));
        Assert.Equal("ResourceSO", assembly.GetFieldType("ResourceSO+PersistentEffect", "resource"));
        Assert.Equal("ResourceSO+ModifiableType", assembly.GetFieldType("ResourceSO+PersistentEffect", "upgradeType"));
        Assert.Equal("ValueModifier", assembly.GetFieldType("ResourceSO+PersistentEffect", "modifier"));
        Assert.Equal("UpgradeableObject", assembly.GetFieldType("UpgradeableObject+UpgradeEffectModifier", "upgradeableObject"));
        Assert.Equal("System.String", assembly.GetFieldType("UpgradeableObject+UpgradeEffectModifier", "propertyType"));
        Assert.Equal("ValueModifier", assembly.GetFieldType("UpgradeableObject+UpgradeEffectModifier", "modifier"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("UpgradeableObject+UpgradeEffectModifier", "useTargetRef"));
        AssertMethod(assembly, "ValueModifier", "Adjust", false, "BigDouble", "BigDouble");
        AssertMethod(assembly, "IdScriptableObject", "GetGuid", false, "System.Guid");
        AssertMethod(assembly, "TooltipableObject", "GetName", false, "System.String");
        Assert.Equal("ValueModifierRecord", assembly.GetFieldType("ResourceSO", "quality"));
        Assert.Equal("ValueModifierRecord", assembly.GetFieldType("ResourceSO", "maxQuantity"));
        AssertMethod(assembly, "ValueModifierRecord", "GetValue", false, "BigDouble");
    }

    [GameAssemblyFact]
    public void AutoCast_MatchesNativeLoadoutCastResourceAndTargetContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("SpellManager", assembly.GetFieldType("SpellManager", "instance"));
        Assert.Equal("SpellListVariable", assembly.GetFieldType("SpellManager", "activeSpells"));
        AssertMethod(assembly, "SpellManager", "FireSpellIndex", false, "System.Void", "System.Int32");
        AssertMethod(assembly, "SpellManager", "CanCastASpell", true, "System.Boolean");
        AssertMethod(assembly, "SpellManager", "AddSpell", false, "System.Void", "Spell");
        AssertMethod(assembly, "SpellManager", "RemoveSpell", false, "System.Void", "Spell");
        AssertMethod(assembly, "SpellManager", "MoveSpell", false, "System.Void", "Spell");

        AssertMethod(assembly, "Spell", "Fire", false, "System.Void");
        AssertMethod(assembly, "Spell", "CanCast", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "CanFire", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsEmpty", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsCasting", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsChanneled", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsToggledSpell", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "CanCharge", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "SetChargeInput", false, "System.Void", "System.String", "System.Boolean");
        AssertMethod(assembly, "Spell", "IsAttuning", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsChargeAvailable", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "GetCurrSpellCharges", false, "System.Int32");
        AssertMethod(assembly, "Spell", "GetMaxSpellCharges", false, "System.Int32");
        AssertMethod(assembly, "Spell", "GetCooldownTimeRemaining", false, "BigDouble");
        AssertMethod(assembly, "Spell", "HasEnoughResources", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "GetCost", false, "ResourceCostList");
        AssertMethod(assembly, "Spell", "GetDrainCost", false, "ResourceCostList");
        AssertMethod(assembly, "Spell", "GetScalingInfo", false, "ScalingInfo");
        Assert.Equal("IdObjectRef`1<SpellRecipeSO>", assembly.GetFieldType("Spell", "referenceObj"));
        AssertMethod(assembly, "Spell", "get_reference", false, "SpellRecipeSO");

        Assert.Equal("SpellRecipeSO+CastType", assembly.GetFieldType("SpellRecipeSO", "castType"));
        Assert.Equal("System.Collections.Generic.List`1<InstantEffectBlock>", assembly.GetFieldType("SpellRecipeSO", "onCastEffects"));
        Assert.Equal("System.Collections.Generic.List`1<PersistentEffectBlock>", assembly.GetFieldType("SpellRecipeSO", "toggledEffects"));
        AssertMethod(assembly, "ResourceSO", "GetTrueQuantity", false, "BigDouble");
        Assert.Equal("ValueModifierRecord", assembly.GetFieldType("ResourceSO", "maxQuantity"));
        AssertMethod(assembly, "ValueModifierRecord", "GetValue", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueAmount", false, "BigDouble", "BigDouble");

        Assert.Equal("Targeting.TargetSelectOptions", assembly.GetFieldType("RequestTargetEffectScript", "targetOptions"));
        AssertMethod(assembly, "Targeting.TargetSelectOptions", "HasValidTargetsLeft", false, "System.Boolean", "ScalingInfo");
        AssertMethod(assembly, "Targeting.TargetSelectOptions", "GetRandom", false, "Targeting.ITargetable", "ScalingInfo");
        AssertMethod(assembly, "TargetingManager", "IsTargeting", true, "System.Boolean");
        AssertMethod(assembly, "TargetingManager", "GetTargetingLink", true, "TargetingManager+TargetLink");
        AssertMethod(assembly, "TargetingManager", "SubmitTarget", true, "System.Void", "Targeting.ITargetable");
        AssertMethod(assembly, "TargetingManager+TargetLink", "GetRandom", false, "Targeting.ITargetable");

        Assert.Equal("BoolVariable", assembly.GetFieldType("AutoBuyManager", "autoBuyEnabled"));
        Assert.Equal("BoolVariable", assembly.GetFieldType("UIToggleButton", "isOnVariable"));
        Assert.Equal("UnityEngine.UI.Image", assembly.GetFieldType("UIToggleButton", "iconImage"));
        Assert.Equal("TMPro.TextMeshProUGUI", assembly.GetFieldType("UIToggleButton", "textElement"));
        Assert.Equal("UnityEngine.Sprite", assembly.GetFieldType("UIToggleButton", "onButtonSprite"));
        AssertMethod(assembly, "UIToggleButton", "SetState", false, "System.Void", "System.Boolean");
        AssertMethod(assembly, "TooltipNode", ".ctor", false, "System.Void", "System.String", "UnityEngine.Color");
        AssertMethod(
            assembly,
            "UIPopupText",
            "CreateOn",
            true,
            "UIPopupText",
            "System.Collections.Generic.List`1<TooltipNode>",
            "UnityEngine.RectTransform",
            "UnityEngine.Vector2");
    }

    [GameAssemblyFact]
    public void AutoSpellLevel_MatchesNativePrerequisiteCostAndCapabilityContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("SpellRecipeListVariable", assembly.GetFieldType("SpellManager", "availableSpellRecipes"));
        AssertMethod(assembly, "SpellManager", "TryLevelAllSpells", false, "System.Void");
        Assert.Equal("Prerequisites+Container", assembly.GetFieldType("SpellRecipeSO", "levelingPrerequisites"));
        AssertMethod(assembly, "Prerequisites+Container", "Check", false, "System.Boolean");
        AssertMethod(assembly, "SpellRecipeSO", "GetLevelCost", false, "ResourceCostList");
        AssertMethod(assembly, "SpellRecipeSO", "IsReadyToLevelMastery", false, "System.Boolean");
        AssertMethod(assembly, "SpellRecipeSO", "PurchaseLevel", false, "System.Void");
        AssertMethod(assembly, "ResourceCostList", "HasEnough", false, "System.Boolean");
        AssertMethod(assembly, "ResourceCostList", "PerformCost", false, "System.Void");
        AssertMethod(assembly, "UpgradeSO", "GetPurchaseLevel", false, "System.Int32");
    }

    [GameAssemblyFact]
    public void AutoConcept_MatchesScopedCatalogSlotQuantityAndDrainContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            "System.Collections.Generic.Dictionary`2<System.Guid,IdScriptableObject>",
            assembly.GetFieldType("IdScriptableObject", "RuntimeLookup"));
        Assert.Equal("System.Collections.Generic.List`1<!0>", assembly.GetFieldType("AbstractListVariable`1", "value"));
        Assert.Equal("System.Collections.Generic.List`1<AlchemyTypeSO>", assembly.GetFieldType("AlchemyRecipeSO", "alchemyTypes"));
        Assert.Equal("ResourceCostList", assembly.GetFieldType("AlchemyRecipeSO", "drainCost"));
        Assert.Equal("System.Int32", assembly.GetFieldType("AlchemyRecipeSO", "masteryLevel"));
        AssertMethod(assembly, "AlchemyRecipeSO", "IsDiscovered", false, "System.Boolean");
        AssertMethod(assembly, "AlchemyRecipeSO", "GetExperience", false, "BigDouble");
        AssertMethod(assembly, "AlchemyRecipeSO", "GetRequiredExperience", false, "BigDouble");
        AssertMethod(assembly, "AlchemyRecipeSO", "GetExperienceLevel", false, "System.Int32");
        AssertMethod(assembly, "AlchemyRecipeSO", "GetMaxUsageSlots", false, "System.Int32");
        AssertMethod(assembly, "AlchemyRecipeSO", "GetCoreType", false, "AlchemyTypeSO");

        AssertMethod(assembly, "AlchemyInstanceListVariable", "CanAddInstance", false, "System.Boolean", "AlchemyRecipeSO");
        AssertMethod(assembly, "AlchemyInstanceListVariable", "AddAlchemyInstances", false, "System.Void", "AlchemyRecipeSO", "System.Int32");
        AssertMethod(assembly, "AlchemyInstanceListVariable", "RemoveAlchemyInstances", false, "System.Void", "AlchemyRecipeSO", "System.Int32");
        AssertMethod(assembly, "AlchemyInstanceListVariable", "SetupMaxSlotsValue", false, "System.Void");
        Assert.Equal("System.Int32", assembly.GetFieldType("AlchemyInstance", "quantity"));
        Assert.Equal("System.Int32", assembly.GetFieldType("AlchemyInstance", "queuedQuantity"));
        Assert.Equal("ResourceDrain", assembly.GetFieldType("AlchemyInstance", "resourceDrain"));
        AssertMethod(assembly, "AlchemyInstance", ".ctor", false, "System.Void", "AlchemyRecipeSO");
        AssertMethod(assembly, "AlchemyInstance", "GetDrainCostMod", false, "BigDouble");
        AssertMethod(assembly, "ResourceDrain", "GetCurrentDrain", false, "ResourceCostList");
        AssertMethod(assembly, "ResourceDrain", "GetRatio", false, "BigDouble");
        AssertMethod(assembly, "ResourceCostList", "Subtract", false, "ResourceCostList", "ResourceCostList");
        AssertMethod(assembly, "ResourceCostList", "Multiply", false, "ResourceCostList", "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueSpend", false, "BigDouble", "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueRate", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetModdedDrain", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "IsAtZero", false, "System.Boolean");
        AssertMethod(assembly, "ResourceSO", "GetTrueSoftCap", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "HasMaxQuantity", false, "System.Boolean");
    }

    [GameAssemblyFact]
    public void BigDouble_MatchesPrecisionBridgeContract()
    {
        using var firstPass = new GameAssemblyMetadata(GameAssemblyPaths.Require().FirstPass);

        Assert.Equal("System.Double", firstPass.GetFieldType("BigDouble", "mantissa"));
        Assert.Equal("System.Int64", firstPass.GetFieldType("BigDouble", "exponent"));
        Assert.Equal("BigDouble", firstPass.GetFieldType("BigDouble", "One"));
        AssertMethod(firstPass, "BigDouble", ".ctor", false, "System.Void", "System.Double");
        AssertMethod(firstPass, "BigDouble", ".ctor", false, "System.Void", "System.Double", "System.Int64");
        AssertMethod(firstPass, "BigDouble", "op_Implicit", true, "BigDouble", "System.Double");
    }

    [GameAssemblyFact]
    public void OrbMentor_MatchesNativeMasteryCatalogSaveAndTypeXpContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal("System.Collections.Generic.List`1<SpellRecipeSO>", assembly.GetFieldType("SpellRecipeSO", "All"));
        Assert.Equal("System.Int32", assembly.GetFieldType("SpellRecipeSO", "masteryLevel"));
        Assert.Equal("BigDouble", assembly.GetFieldType("SpellRecipeSO", "masteryExperience"));
        AssertMethod(assembly, "SpellRecipeSO", "GainMasteryExp", false, "System.Void", "BigDouble");
        AssertMethod(assembly, "SpellRecipeSO", "IsDiscovered", false, "System.Boolean");
        AssertMethod(assembly, "SpellRecipeSO", "IsReadyToLevelMastery", false, "System.Boolean");
        AssertMethod(assembly, "SpellRecipeSO", "PurchaseLevel", false, "System.Void");
        AssertMethod(assembly, "SpellRecipeSO", "CollectSaveData", false, "JsonSaveData");
        AssertMethod(assembly, "IdScriptableObject", "GetGuid", false, "System.Guid");
        AssertMethod(assembly, "IdScriptableObject", "GetId", false, "System.Guid");
        Assert.Equal("System.Boolean", assembly.GetFieldType("SpellRecipeSO+SpellRecipeSaveData", "discovered"));
        Assert.Equal("BigDouble", assembly.GetFieldType("SpellRecipeSO+SpellRecipeSaveData", "masteryExperience"));
        Assert.Equal("System.Int32", assembly.GetFieldType("SpellRecipeSO+SpellRecipeSaveData", "masteryLevel"));
        AssertMethod(assembly, "SpellTypeSO", "GainTypeXp", false, "System.Void", "BigDouble", "System.Collections.Generic.List`1<TooltipNode>");
    }

    [GameAssemblyFact]
    public void OrbMentorAlchemy_MatchesNativeMasteryContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal("System.Collections.Generic.List`1<AlchemyRecipeSO>", assembly.GetFieldType("AlchemyRecipeSO", "All"));
        Assert.Equal("BigDouble", assembly.GetFieldType("AlchemyRecipeSO", "masteryXp"));
        Assert.Equal("System.Int32", assembly.GetFieldType("AlchemyRecipeSO", "masteryLevel"));
        AssertMethod(assembly, "AlchemyRecipeSO", "GainMasteryXp", false, "System.Void", "BigDouble");
        AssertMethod(assembly, "AlchemyRecipeSO", "IsDiscovered", false, "System.Boolean");
        AssertMethod(assembly, "AlchemyRecipeSO", "IsAvailable", false, "System.Boolean");
        AssertMethod(assembly, "AlchemyRecipeSO", "IsDiscoveredRecipe", false, "System.Boolean");
        AssertMethod(assembly, "AlchemyRecipeSO", "ApplyMastery", false, "System.Void");
        AssertMethod(assembly, "AlchemyInstance", "CompleteRecipe", false, "System.Void");
        Assert.Equal("BigDouble", assembly.GetFieldType("AlchemyRecipeSO+AlchemyRecipeSaveData", "masteryXp"));
        Assert.Equal("System.Int32", assembly.GetFieldType("AlchemyRecipeSO+AlchemyRecipeSaveData", "masteryLevel"));
    }

    [GameAssemblyFact]
    public void OrbMentorArtifacts_MatchNativeProgressionSequence()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal("System.Collections.Generic.List`1<EquipmentSO>", assembly.GetFieldType("EquipmentSO", "All"));
        Assert.Equal("BigDouble", assembly.GetFieldType("EquipmentSO", "masteryXp"));
        Assert.Equal("System.Int32", assembly.GetFieldType("EquipmentSO", "masteryLevel"));
        Assert.Equal("ExperienceContainer", assembly.GetFieldType("EquipmentSO", "experienceContainer"));
        AssertMethod(assembly, "EquipmentSO", "IncrementActive", false, "System.Void", "System.Double");
        AssertMethod(assembly, "EquipmentSO", "GainMasteryLevels", false, "System.Void", "System.Int32");
        AssertMethod(assembly, "EquipmentSO", "GetExperienceElement", false, "IExperienceElement");
        AssertMethod(assembly, "EquipmentSO", "IsCreated", false, "System.Boolean");
        AssertMethod(assembly, "ExperienceContainer", "GainExperience", false, "System.Void", "BigDouble");
        AssertMethod(assembly, "ExperienceContainer", "GetGainedLevels", false, "System.Int32");
        AssertMethod(assembly, "ExperienceContainer", "GetExperience", false, "BigDouble");
        AssertMethod(assembly, "ExperienceContainer", "GetLevel", false, "System.Int32");
        Assert.Equal("BigDouble", assembly.GetFieldType("EquipmentSO+EquipmentSaveData", "mXp"));
        Assert.Equal("System.Int32", assembly.GetFieldType("EquipmentSO+EquipmentSaveData", "mLv"));
    }

    [GameAssemblyFact]
    public void ModConfigNavigation_MatchesKnownNativeLayout()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("ViewListVariable", assembly.GetFieldType("CoreViewManager", "coreViews"));
        Assert.Equal("ViewListVariable", assembly.GetFieldType("ViewManager", "coreViews"));
        AssertMethod(assembly, "CoreViewManager", "ChangeCoreView", false, "System.Void", "System.Int32");
        AssertMethod(assembly, "CoreViewManager", "GetSubViews", false, "ViewListVariable", "ViewSO");
        AssertMethod(
            assembly,
            "CoreViewManager",
            "SubmitViewLayer",
            false,
            "System.Void",
            "ViewListVariable",
            "ViewSO");
        AssertMethod(assembly, "ViewManager", "SwapCoreView", false, "System.Void", "ViewSO");
        AssertMethod(assembly, "ViewSO", "SetActive", false, "System.Void", "System.Boolean");
        AssertMethod(assembly, "ViewSO", "IsActive", false, "System.Boolean");
        Assert.Equal("ViewSO", assembly.GetFieldType("ManagedView", "viewReference"));
        Assert.Equal("UIGenericItem`1<ViewSO>", assembly.GetBaseType("UIViewRadioButton"));
    }

    private static void AssertMethod(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName,
        bool isStatic,
        string returnType,
        params string[] parameterTypes)
    {
        var matches = assembly.GetMethods(typeName, methodName);
        Assert.Contains(matches, method =>
            method.IsStatic == isStatic &&
            method.ReturnType == returnType &&
            method.ParameterTypes.SequenceEqual(parameterTypes));
    }

    private static string FormatMismatch(AssemblyHashResult result)
    {
        return $"{result.Path}: expected {result.ExpectedSha256}, actual {result.ActualSha256 ?? "<missing>"}";
    }
}
