using System;
using System.Collections.Generic;
using System.Text;
using EFT.InventoryLogic;
using RaidIntel.Client.Patches;

namespace RaidIntel.Client.Core;

internal sealed class ItemIntelAggregator
{
    internal static readonly ItemIntelAggregator Instance = new();

    internal ItemIntelResult Query(Item item)
    {
        string tid = item.StringTemplateId;
        (int stashTotal, int stashFir) = StashTracker.Instance.GetCount(tid);
        bool inRaid = RaidCarryTracker.Instance.IsInRaid;
        (int carryTotal, int carryFir) = inRaid
            ? RaidCarryTracker.Instance.GetCount(tid)
            : StashTracker.Instance.GetMenuEquipmentCount(tid);

        return new ItemIntelResult
        {
            TemplateId = tid,
            Owned = new OwnedBreakdown(stashTotal, stashFir, carryTotal, carryFir, inRaid),
            QuestRequirements = QuestRequirementIndex.Instance.Query(item, LifecyclePatch.GetPmcProfile()),
            HideoutRequirements = HideoutRequirementIndex.Instance.Query(tid),
            FavoriteCraftRequirements = FavoriteCraftRequirementIndex.Instance.Query(tid),
        };
    }

    internal string FormatHoverTooltip(ItemIntelResult intel)
    {
        if (intel == null)
        {
            return string.Empty;
        }

        var currentLines = new List<string>(8);
        int futureCount = 0;

        for (int i = 0; i < intel.QuestRequirements.Count; i++)
        {
            QuestReqEntry q = intel.QuestRequirements[i];
            if (q.IsActive)
            {
                if (RaidIntelPlugin.TooltipShowActiveQuests.Value)
                {
                    int have = q.HandedInCount + (q.OnlyFoundInRaid ? intel.Owned.Fir : intel.Owned.Total);
                    int need = q.RequiredCount;
                    string tagSuffix = q.FormatTagSuffix();
                    string color = have >= need ? "#7FD88A" : "#F2B84B";
                    currentLines.Add($"<color={color}>{q.TooltipName} {have}/{need}{tagSuffix}</color>");
                }
            }
            else
            {
                futureCount++;
            }
        }

        for (int i = 0; i < intel.HideoutRequirements.Count; i++)
        {
            HideoutReqEntry h = intel.HideoutRequirements[i];
            if (h.IsNextLevel)
            {
                if (RaidIntelPlugin.TooltipShowNextHideout.Value)
                {
                    int have = h.OnlyFoundInRaid ? intel.Owned.Fir : intel.Owned.Total;
                    int need = h.RequiredCount;
                    string firSuffix = h.OnlyFoundInRaid ? "（FIR）" : string.Empty;
                    string color = have >= need ? "#7FD88A" : "#6CC8E8";
                    currentLines.Add($"<color={color}>{h.AreaName} {have}/{need}{firSuffix}</color>");
                }
            }
            else
            {
                futureCount++;
            }
        }

        if (RaidIntelPlugin.TooltipShowFavoriteCrafts.Value && intel.FavoriteCraftRequirements != null)
        {
            for (int i = 0; i < intel.FavoriteCraftRequirements.Count; i++)
            {
                FavoriteCraftReqEntry c = intel.FavoriteCraftRequirements[i];
                int have = c.OnlyFoundInRaid ? intel.Owned.Fir : intel.Owned.Total;
                int need = c.RequiredCount;
                string tagSuffix = c.FormatTagSuffix();
                string color = have >= need ? "#7FD88A" : "#C59CE6";
                string label = !string.IsNullOrEmpty(c.ProductShortName) ? c.ProductShortName : c.ProductName;
                currentLines.Add($"<color={color}>收藏·{label} {have}/{need}{tagSuffix}</color>");
            }
        }

        bool hasFutureLine = futureCount > 0 && RaidIntelPlugin.TooltipShowFutureSummary.Value;
        if (currentLines.Count == 0 && futureCount == 0 && !RaidIntelPlugin.TooltipShowOwnedWhenNoReq.Value)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(192);

        if (RaidIntelPlugin.TooltipShowOwned.Value)
        {
            sb.Append("<color=#D8D5CC>拥有：")
              .Append(intel.Owned.Total)
              .Append("（FIR：")
              .Append(intel.Owned.Fir)
              .Append("）</color>");
        }

        int maxLines = Math.Max(2, RaidIntelPlugin.TooltipMaxLines.Value);
        if (currentLines.Count > 0)
        {
            int limit = currentLines.Count > maxLines ? maxLines - 1 : currentLines.Count;
            for (int i = 0; i < limit; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(currentLines[i]);
            }

            if (currentLines.Count > maxLines)
            {
                int overflow = currentLines.Count - limit;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("<color=#9AA8B4>…另有 ").Append(overflow).Append(" 项当前需求</color>");
            }
        }

        if (hasFutureLine)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<color=#9AA8B4>检视 ").Append(futureCount).Append(" 项后续需求</color>");
        }

        return sb.ToString();
    }
}
