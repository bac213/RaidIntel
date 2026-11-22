using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT.Hideout;

namespace RaidIntel.Client.Core;

/// <summary>
/// Indexes all future Hideout area construction and upgrade ItemRequirements from
/// HideoutRepresentation.AreaDatas, keyed O(1) by TemplateId.
/// </summary>
internal sealed class HideoutRequirementIndex
{
    internal static readonly HideoutRequirementIndex Instance = new();

    private static readonly HideoutReqEntry[] EmptyResult = Array.Empty<HideoutReqEntry>();

    private volatile Dictionary<string, List<HideoutReqEntry>> _byTemplate = new(StringComparer.Ordinal);
    private volatile bool _dirty = true;

    internal void MarkDirty()
    {
        _dirty = true;
    }

    internal void RebuildFromHideout()
    {
        HideoutRepresentation hideout = Singleton<HideoutRepresentation>.Instantiated
            ? Singleton<HideoutRepresentation>.Instance
            : null;
        if (hideout?.AreaDatas == null || hideout.AreaDatas.Count == 0)
        {
            return;
        }

        var nextMap = new Dictionary<string, List<HideoutReqEntry>>(256, StringComparer.Ordinal);
        List<AreaData> areas = hideout.AreaDatas;

        for (int i = 0; i < areas.Count; i++)
        {
            AreaData area = areas[i];
            if (area?.Template?.Stages == null)
            {
                continue;
            }

            bool currentNextAlreadyPaid = IsNextLevelAlreadyPaid(area.Status);
            int firstUnpaidLevel = currentNextAlreadyPaid ? area.CurrentLevel + 2 : area.CurrentLevel + 1;
            int maxLevel = area.Template.MaxLevel;
            if (firstUnpaidLevel > maxLevel)
            {
                continue;
            }

            string areaName = area.Template.Name;

            for (int lvl = firstUnpaidLevel; lvl <= maxLevel; lvl++)
            {
                Stage stage = area.StageAt(lvl);
                List<Requirement> reqs = stage?.Requirements?.Data;
                if (reqs == null || reqs.Count == 0)
                {
                    continue;
                }

                bool isNext = lvl == firstUnpaidLevel;

                for (int j = 0; j < reqs.Count; j++)
                {
                    if (reqs[j] is not ItemRequirement itemReq || string.IsNullOrEmpty(itemReq.TemplateId))
                    {
                        continue;
                    }

                    var entry = new HideoutReqEntry
                    {
                        AreaName = areaName,
                        TargetLevel = lvl,
                        RequiredCount = itemReq.IntCount,
                        OnlyFoundInRaid = itemReq.IsSpawnedInSession,
                        IsNextLevel = isNext,
                    };

                    if (!nextMap.TryGetValue(itemReq.TemplateId, out List<HideoutReqEntry> bucket))
                    {
                        bucket = new List<HideoutReqEntry>(2);
                        nextMap[itemReq.TemplateId] = bucket;
                    }
                    bucket.Add(entry);
                }
            }
        }

        foreach (List<HideoutReqEntry> list in nextMap.Values)
        {
            list.Sort(CompareHideoutEntries);
        }

        _byTemplate = nextMap;
        _dirty = false;
        RaidIntelPlugin.DebugLog($"HideoutRequirementIndex rebuilt ({nextMap.Count} unique templates)");
    }

    internal IReadOnlyList<HideoutReqEntry> Query(string templateId)
    {
        if (string.IsNullOrEmpty(templateId))
        {
            return EmptyResult;
        }

        if (_dirty)
        {
            RebuildFromHideout();
        }

        return _byTemplate.TryGetValue(templateId, out List<HideoutReqEntry> list) ? list : EmptyResult;
    }

    private static bool IsNextLevelAlreadyPaid(EAreaStatus status)
    {
        return status == EAreaStatus.Constructing
            || status == EAreaStatus.ReadyToInstallConstruct
            || status == EAreaStatus.Upgrading
            || status == EAreaStatus.ReadyToInstallUpgrade
            || status == EAreaStatus.AutoUpgrading;
    }

    private static int CompareHideoutEntries(HideoutReqEntry a, HideoutReqEntry b)
    {
        if (a.IsNextLevel != b.IsNextLevel)
        {
            return a.IsNextLevel ? -1 : 1;
        }
        int cmp = string.Compare(a.AreaName, b.AreaName, StringComparison.Ordinal);
        return cmp != 0 ? cmp : a.TargetLevel.CompareTo(b.TargetLevel);
    }
}
