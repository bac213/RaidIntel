using System;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using RaidIntel.Client.Core;
using RaidIntel.Client.UI;

namespace RaidIntel.Client.Patches;

internal static class InspectPanelPatch
{
    private static readonly FieldInfo ItemField =
        AccessTools.Field(typeof(ItemSpecificationPanel), "_item");

    internal static void Apply(Harmony harmony)
    {
        MethodInfo refreshMethod = AccessTools.Method(typeof(ItemSpecificationPanel), "method_1");
        if (refreshMethod == null)
        {
            throw new MissingMethodException(typeof(ItemSpecificationPanel).FullName, "method_1");
        }

        MethodInfo closeMethod = AccessTools.Method(typeof(ItemSpecificationPanel), nameof(ItemSpecificationPanel.Close));
        if (closeMethod == null)
        {
            throw new MissingMethodException(typeof(ItemSpecificationPanel).FullName, nameof(ItemSpecificationPanel.Close));
        }

        harmony.Patch(refreshMethod, postfix: new HarmonyMethod(typeof(InspectPanelPatch), nameof(RefreshPostfix)));
        harmony.Patch(closeMethod, prefix: new HarmonyMethod(typeof(InspectPanelPatch), nameof(ClosePrefix)));
    }

    private static void RefreshPostfix(ItemSpecificationPanel __instance)
    {
        if (__instance == null)
        {
            return;
        }

        try
        {
            UpdateIntelBlock(__instance);
        }
        catch (Exception ex)
        {
            RaidIntelPlugin.DebugLog($"InspectPanelPatch.UpdateIntelBlock error: {ex.Message}");
        }

        try
        {
            InspectScrollMerge.Apply(__instance);
        }
        catch (Exception ex)
        {
            RaidIntelPlugin.Log?.LogWarning($"RaidIntel: InspectScrollMerge error: {ex}");
        }

        try
        {
            InspectWindowSizer.Apply(__instance);
        }
        catch (Exception ex)
        {
            RaidIntelPlugin.Log?.LogWarning($"RaidIntel: InspectWindowSizer error: {ex}");
        }
    }

    private static void UpdateIntelBlock(ItemSpecificationPanel panel)
    {
        var item = ItemField?.GetValue(panel) as Item;
        if (!RaidIntelPlugin.IsActiveForCurrentContext()
            || !RaidIntelPlugin.EnableInspectPanel.Value
            || !panel.Examined
            || item == null)
        {
            InspectIntelBlock.HideIfExists(panel);
            return;
        }

        InspectIntelBlock block = InspectIntelBlock.GetOrCreate(panel);
        if (block == null)
        {
            return;
        }

        ItemIntelResult intel = ItemIntelAggregator.Instance.Query(item);
        block.UpdateContent(intel);
    }

    private static void ClosePrefix(ItemSpecificationPanel __instance)
    {
        try
        {
            if (__instance != null)
            {
                InspectIntelBlock.HideIfExists(__instance);
            }
        }
        catch (Exception ex)
        {
            RaidIntelPlugin.DebugLog($"InspectPanelPatch.ClosePrefix error: {ex.Message}");
        }
    }
}
