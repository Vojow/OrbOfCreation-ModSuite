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
        AssertMethod(assembly, "StructureSO", "GetQueuedQuantity", false, "System.Int32");

        Assert.Equal("System.Collections.Generic.List`1<UpgradeSO>", assembly.GetFieldType("UpgradeSO", "All"));
        AssertMethod(assembly, "UpgradeSO", "IsAvailable", false, "System.Boolean");
        AssertMethod(assembly, "UpgradeSO", "CanPurchase", false, "System.Boolean");
        AssertMethod(assembly, "UpgradeSO", "GetPurchaseCost", false, "ResourceCostList");
        AssertMethod(assembly, "UpgradeSO", "Purchase", false, "System.Void");
        AssertMethod(assembly, "UpgradeSO", "GetQueuedPurchaseLevel", false, "System.Int32");

        AssertMethod(assembly, "ActionManager", "GetRemainingRoom", true, "System.Int32");
        AssertMethod(assembly, "GlobalVariables", "GetMultiBuy", true, "IntVariable");
        AssertMethod(assembly, "Player", "GetBulkDevelopment", true, "IntVariable");
        AssertMethod(assembly, "IntVariable", "AsInt", false, "System.Int32");
        AssertMethod(assembly, "IntVariable", "SetValue", false, "System.Void", "System.Int32");
        Assert.Equal("System.Collections.Generic.List`1<ResourceTuple>", assembly.GetFieldType("ResourceCostList", "costs"));
        Assert.Equal("ResourceSO", assembly.GetFieldType("ResourceTuple", "resource"));
        AssertMethod(assembly, "ResourceTuple", "GetValue", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueQuantity", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueSpend", false, "BigDouble", "BigDouble");
    }

    [GameAssemblyFact]
    public void AutoCast_MatchesNativeLoadoutCastResourceAndTargetContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("SpellManager", assembly.GetFieldType("SpellManager", "instance"));
        Assert.Equal("SpellListVariable", assembly.GetFieldType("SpellManager", "activeSpells"));
        AssertMethod(assembly, "SpellManager", "FireSpellIndex", false, "System.Void", "System.Int32");
        AssertMethod(assembly, "SpellManager", "CanCastASpell", true, "System.Boolean");

        AssertMethod(assembly, "Spell", "Fire", false, "System.Void");
        AssertMethod(assembly, "Spell", "CanCast", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "CanFire", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsEmpty", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsCasting", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsChanneled", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsToggledSpell", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "CanCharge", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsAttuning", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsChargeAvailable", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "GetCurrSpellCharges", false, "System.Int32");
        AssertMethod(assembly, "Spell", "GetMaxSpellCharges", false, "System.Int32");
        AssertMethod(assembly, "Spell", "GetCooldownTimeRemaining", false, "BigDouble");
        AssertMethod(assembly, "Spell", "HasEnoughResources", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "GetCost", false, "ResourceCostList");
        AssertMethod(assembly, "Spell", "GetDrainCost", false, "ResourceCostList");
        AssertMethod(assembly, "Spell", "GetScalingInfo", false, "ScalingInfo");

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
    public void AchievementResonance_MatchesKnownNativeLayout()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("NumberVariable", assembly.GetBaseType("IntVariable"));
        AssertMethod(assembly, "NumberVariable", "ApplyEffects", false, "System.Void", "System.Int32");
        AssertMethod(assembly, "NumberVariable", "GetLevel", false, "System.Int32");
        AssertMethod(assembly, "NumberVariable", "GetExecInfo", false, "EffectExecutionInfo", "System.Int32");
        Assert.Equal(
            "System.Collections.Generic.List`1<PersistentEffectBlock>",
            assembly.GetFieldType("NumberVariable", "persistentEffectBlocks"));
        Assert.Equal(
            "System.Collections.Generic.List`1<IPersistentEffectScript>",
            assembly.GetFieldType("PersistentEffectBlock", "effectScripts"));
        Assert.Equal("NumberVariable", assembly.GetFieldType("NumberVariable+PersistentEffect", "numberVariable"));
        Assert.Equal("ValueModifier", assembly.GetFieldType("NumberVariable+PersistentEffect", "modifier"));
        Assert.Equal("UpgradeableObject", assembly.GetFieldType("UpgradeableObject+UpgradeEffectModifier", "upgradeableObject"));
        Assert.Equal("System.String", assembly.GetFieldType("UpgradeableObject+UpgradeEffectModifier", "propertyType"));
        Assert.Equal("System.Int32", assembly.GetFieldType("UpgradeableObject+UpgradeEffectModifier", "propertyIndex"));
        Assert.Equal("ValueModifier", assembly.GetFieldType("UpgradeableObject+UpgradeEffectModifier", "modifier"));

        AssertMethod(assembly, "ValueModifier", "Stacking", true, "ValueModifier", "BigDouble");
        AssertMethod(assembly, "ValueModifier", "Stacking", true, "ValueModifier", "System.Guid", "BigDouble");
        AssertMethod(assembly, "ValueModifier", "GetGuid", false, "System.Guid");

        var numberEffectFields = new[]
        {
            "maximumMultiplier", "MaximumMultiplier", "maxMultiplier", "MaxMultiplier", "maximum", "Maximum", "cap", "Cap",
        };
        Assert.All(numberEffectFields, name => Assert.Throws<InvalidOperationException>(
            () => assembly.GetFieldType("NumberVariable+PersistentEffect", name)));
    }

    [GameAssemblyFact]
    public void BigDouble_MatchesPrecisionBridgeContract()
    {
        using var firstPass = new GameAssemblyMetadata(GameAssemblyPaths.Require().FirstPass);

        Assert.Equal("System.Double", firstPass.GetFieldType("BigDouble", "mantissa"));
        Assert.Equal("System.Int64", firstPass.GetFieldType("BigDouble", "exponent"));
        AssertMethod(firstPass, "BigDouble", ".ctor", false, "System.Void", "System.Double");
        AssertMethod(firstPass, "BigDouble", ".ctor", false, "System.Void", "System.Double", "System.Int64");
        AssertMethod(firstPass, "BigDouble", "op_Implicit", true, "BigDouble", "System.Double");
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
