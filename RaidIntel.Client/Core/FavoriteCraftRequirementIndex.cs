using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Hideout;

namespace RaidIntel.Client.Core;

/// <summary>
/// Indexes material and tool ItemRequirements from all Hideout favorite crafting recipes
/// (<see cref="HideoutRepresentation.FavoriteProductionSchemes"/>), keyed O(1) by TemplateId.
/// </summary>
internal sealed class FavoriteCraftRequirementIndex
{
    internal static readonly FavoriteCraftRequirementIndex Instance = new();

    private static readonly FavoriteCraftReqEntry[] EmptyResult = Array.Empty<FavoriteCraftReqEntry>();

    private volatile Dictionary<string, List<FavoriteCraftReqEntry>> _byTemplate = new(StringComparer.Ordinal);
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

        var areaNames = new Dictionary<EAreaType, string>(hideout.AreaDatas.Count);
        List<AreaData> areas = hideout.AreaDatas;
        for (int i = 0; i < areas.Count; i++)
        {
            AreaData area = areas[i];
            if (area?.Template != null)
            {
                areaNames[area.Template.Type] = area.Template.Name;
            }
        }

        var nextMap = new Dictionary<string, List<FavoriteCraftReqEntry>>(64, StringComparer.Ordinal);
        int schemeCount = 0;

        IEnumerable<ProductionScheme> favorites = hideout.FavoriteProductionSchemes;
        if (favorites != null)
        {
            foreach (ProductionScheme scheme in favorites)
            {
                if (scheme == null || !scheme.HasRequirements || scheme.requirements == null)
                {
                    continue;
                }

                schemeCount++;

                var areaType = (EAreaType)scheme.areaType;
                if (!areaNames.TryGetValue(areaType, out string areaName) || string.IsNullOrEmpty(areaName))
                {
                    areaName = areaType.ToString();
                }

                string productName = scheme.endProduct.LocalizedName();
                if (string.IsNullOrEmpty(productName))
                {
                    productName = scheme._id ?? string.Empty;
                }

                string productShortName = scheme.endProduct.LocalizedShortName();
                if (string.IsNullOrEmpty(productShortName))
                {
                    productShortName = productName;
                }

                int areaLevel = Math.Max(1, scheme.Level);
                int productCount = Math.Max(1, scheme.count);
                int favoriteIndex = scheme.FavoriteIndex;
                Requirement[] reqs = scheme.requirements;

                for (int i = 0; i < reqs.Length; i++)
                {
                    if (reqs[i] is not ItemRequirement itemReq || string.IsNullOrEmpty(itemReq.TemplateId))
                    {
                        continue;
                    }

                    int requiredCount = itemReq.IntCount > 0 ? itemReq.IntCount : Math.Max(1, itemReq.BaseCount);
                    var entry = new FavoriteCraftReqEntry
                    {
                        SchemeId = scheme._id ?? string.Empty,
                        AreaName = areaName,
                        AreaLevel = areaLevel,
                        ProductName = productName,
                        ProductShortName = productShortName,
                        ProductCount = productCount,
                        RequiredCount = requiredCount,
                        OnlyFoundInRaid = itemReq.IsSpawnedInSession,
                        IsTool = itemReq is ToolRequirement,
                        FavoriteIndex = favoriteIndex,
                    };

                    if (!nextMap.TryGetValue(itemReq.TemplateId, out List<FavoriteCraftReqEntry> bucket))
                    {
                        bucket = new List<FavoriteCraftReqEntry>(2);
                        nextMap[itemReq.TemplateId] = bucket;
                    }
                    bucket.Add(entry);
                }
            }
        }

        foreach (List<FavoriteCraftReqEntry> list in nextMap.Values)
        {
            list.Sort(CompareEntries);
        }

        _byTemplate = nextMap;
        _dirty = false;
        RaidIntelPlugin.DebugLog(
            $"FavoriteCraftRequirementIndex rebuilt ({schemeCount} favorite schemes, {nextMap.Count} unique templates)");
    }

    internal IReadOnlyList<FavoriteCraftReqEntry> Query(string templateId)
    {
        if (string.IsNullOrEmpty(templateId))
        {
            return EmptyResult;
        }

        if (_dirty)
        {
            RebuildFromHideout();
        }

        if (!_byTemplate.TryGetValue(templateId, out List<FavoriteCraftReqEntry> list) || list.Count == 0)
        {
            return EmptyResult;
        }

        if (RaidIntelPlugin.IncludeFavoriteCraftTools == null || RaidIntelPlugin.IncludeFavoriteCraftTools.Value)
        {
            return list;
        }

        List<FavoriteCraftReqEntry> filtered = null;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].IsTool)
            {
                if (filtered == null)
                {
                    filtered = new List<FavoriteCraftReqEntry>(list.Count - 1);
                    for (int j = 0; j < i; j++)
                    {
                        filtered.Add(list[j]);
                    }
                }
            }
            else if (filtered != null)
            {
                filtered.Add(list[i]);
            }
        }

        return filtered ?? (IReadOnlyList<FavoriteCraftReqEntry>)list;
    }

    private static int CompareEntries(FavoriteCraftReqEntry a, FavoriteCraftReqEntry b)
    {
        int favCmp = a.FavoriteIndex.CompareTo(b.FavoriteIndex);
        if (favCmp != 0)
        {
            return favCmp;
        }

        if (a.IsTool != b.IsTool)
        {
            return a.IsTool ? 1 : -1;
        }

        int areaCmp = string.Compare(a.AreaName, b.AreaName, StringComparison.Ordinal);
        if (areaCmp != 0)
        {
            return areaCmp;
        }

        int nameCmp = string.Compare(a.ProductName, b.ProductName, StringComparison.Ordinal);
        return nameCmp != 0 ? nameCmp : string.Compare(a.SchemeId, b.SchemeId, StringComparison.Ordinal);
    }
}
