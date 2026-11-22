using System;
using System.Collections.Generic;
using EFT.InventoryLogic;

namespace RaidIntel.Client.Core;

internal readonly struct OwnedBreakdown
{
    public readonly int StashTotal;
    public readonly int StashFir;
    public readonly int CarryTotal;
    public readonly int CarryFir;
    public readonly bool InRaid;

    public int Total => StashTotal + CarryTotal;
    public int Fir => StashFir + CarryFir;

    public OwnedBreakdown(int stashTotal, int stashFir, int carryTotal, int carryFir, bool inRaid)
    {
        StashTotal = stashTotal;
        StashFir = stashFir;
        CarryTotal = carryTotal;
        CarryFir = carryFir;
        InRaid = inRaid;
    }
}

internal sealed class QuestReqEntry
{
    public string QuestId;
    public string ConditionId;
    public string TooltipName;
    public string InspectName;
    public string TraderId;
    public string Status;
    public string RepeatableKind;
    public int RequiredCount;
    public int HandedInCount;
    public bool OnlyFoundInRaid;
    public bool IsWeaponAssembly;
    public bool IsCategoryTarget;
    public bool IsPlant;

    public bool IsActive => Status == "Started" || Status == "AvailableForFinish";
    public bool IsAvailable => Status == "AvailableForStart";

    internal string FormatTagSuffix()
    {
        if (IsPlant)
        {
            return OnlyFoundInRaid ? "（埋放·FIR）" : "（埋放）";
        }
        return OnlyFoundInRaid ? "（FIR）" : string.Empty;
    }
}

internal sealed class HideoutReqEntry
{
    public string AreaName;
    public int TargetLevel;
    public int RequiredCount;
    public bool OnlyFoundInRaid;
    public bool IsNextLevel;
}

internal sealed class ItemIntelResult
{
    public string TemplateId;
    public OwnedBreakdown Owned;
    public IReadOnlyList<QuestReqEntry> QuestRequirements;
    public IReadOnlyList<HideoutReqEntry> HideoutRequirements;
}

internal static class InventoryCountHelper
{
    internal static void AccumulateContainer(
        CompoundItem root,
        List<Item> scratch,
        Dictionary<string, (int total, int fir)> counts)
    {
        if (root == null)
        {
            return;
        }

        scratch.Clear();
        root.GetAllItemsNonAlloc(scratch, onlyMerged: false, includeParent: false);

        for (int i = 0; i < scratch.Count; i++)
        {
            Item item = scratch[i];
            if (item == null || item.IsEmptyStack)
            {
                continue;
            }

            string tid = item.StringTemplateId;
            if (string.IsNullOrEmpty(tid))
            {
                continue;
            }

            int stack = item.StackObjectsCount > 0 ? item.StackObjectsCount : 1;
            bool fir = item.MarkedAsSpawnedInSession;

            if (counts.TryGetValue(tid, out var existing))
            {
                counts[tid] = (existing.total + stack, fir ? existing.fir + stack : existing.fir);
            }
            else
            {
                counts[tid] = (stack, fir ? stack : 0);
            }
        }

        scratch.Clear();
    }
}
