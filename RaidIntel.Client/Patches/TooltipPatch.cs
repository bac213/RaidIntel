using System;
using System.Reflection;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using RaidIntel.Client.Core;

namespace RaidIntel.Client.Patches;

internal static class TooltipPatch
{
    private const char PatchedSentinel = '\u200B';

    private static readonly FieldInfo ItemUiContextField =
        AccessTools.Field(typeof(ItemView), "ItemUiContext");

    internal static void Apply(Harmony harmony)
    {
        MethodInfo showTooltip = AccessTools.Method(typeof(GridItemView), nameof(GridItemView.ShowTooltip));
        if (showTooltip == null)
        {
            throw new MissingMethodException(typeof(GridItemView).FullName, nameof(GridItemView.ShowTooltip));
        }

        harmony.Patch(showTooltip, postfix: new HarmonyMethod(typeof(TooltipPatch), nameof(ShowTooltipPostfix)));
    }

    private static void ShowTooltipPostfix(GridItemView __instance)
    {
        try
        {
            if (!RaidIntelPlugin.IsActiveForCurrentContext() || !RaidIntelPlugin.EnableHoverTooltip.Value)
            {
                return;
            }

            if (__instance == null || __instance.Item == null || !__instance.Examined || !__instance.IsSearched)
            {
                return;
            }

            var uiContext = ItemUiContextField?.GetValue(__instance) as ItemUiContext;
            SimpleTooltip tooltip = uiContext?.Tooltip;
            if (tooltip == null || !tooltip.Displayed || tooltip._label == null)
            {
                return;
            }

            string currentText = tooltip._label.text;
            if (string.IsNullOrEmpty(currentText) || currentText.IndexOf(PatchedSentinel) >= 0)
            {
                return;
            }

            ItemIntelResult intel = ItemIntelAggregator.Instance.Query(__instance.Item);
            string intelText = ItemIntelAggregator.Instance.FormatHoverTooltip(intel);
            if (string.IsNullOrEmpty(intelText))
            {
                return;
            }

            // Use SetText so we never restart the 0.6s hover delay coroutine.
            tooltip.SetText(string.Concat(currentText, "\n", intelText, PatchedSentinel));
        }
        catch (Exception ex)
        {
            RaidIntelPlugin.DebugLog($"TooltipPatch error: {ex.Message}");
        }
    }
}
