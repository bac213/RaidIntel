using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace RaidIntel.Client.Core;

/// <summary>
/// Fetches full quest item requirements (active, available, and future) from the SPT server
/// endpoint /raidIntel/quests, builds an O(1) TemplateId index + category-target list,
/// and overlays live PMC Profile quest/counter state on every query.
/// </summary>
internal sealed class QuestRequirementIndex
{
    internal static readonly QuestRequirementIndex Instance = new();

    private sealed class ServerQuestsResponse
    {
        [JsonProperty("quests")]
        public List<ServerQuestDto> Quests { get; set; }
    }

    private sealed class ServerQuestDto
    {
        [JsonProperty("questId")]
        public string QuestId { get; set; }

        [JsonProperty("fallbackName")]
        public string FallbackName { get; set; }

        [JsonProperty("traderId")]
        public string TraderId { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("repeatableKind")]
        public string RepeatableKind { get; set; }

        [JsonProperty("requirements")]
        public List<ServerQuestRequirementDto> Requirements { get; set; }
    }

    private sealed class ServerQuestRequirementDto
    {
        [JsonProperty("conditionId")]
        public string ConditionId { get; set; }

        [JsonProperty("targets")]
        public List<string> Targets { get; set; }

        [JsonProperty("requiredCount")]
        public int RequiredCount { get; set; }

        [JsonProperty("handedInCount")]
        public int HandedInCount { get; set; }

        [JsonProperty("completed")]
        public bool Completed { get; set; }

        [JsonProperty("onlyFoundInRaid")]
        public bool OnlyFoundInRaid { get; set; }

        [JsonProperty("isWeaponAssembly")]
        public bool IsWeaponAssembly { get; set; }

        [JsonProperty("isCategoryTarget")]
        public bool IsCategoryTarget { get; set; }

        [JsonProperty("isPlant")]
        public bool IsPlant { get; set; }
    }

    private sealed class CachedQuestReq
    {
        public string QuestId;
        public string FallbackName;
        public string TraderId;
        public string Status;
        public string RepeatableKind;
        public string ConditionId;
        public string[] Targets;
        public int RequiredCount;
        public int HandedInCount;
        public bool OnlyFoundInRaid;
        public bool IsWeaponAssembly;
        public bool IsCategoryTarget;
        public bool IsPlant;
        public bool ServerCompleted;
    }

    private sealed class PlantGroup
    {
        public string QuestId;
        public string FallbackName;
        public string TraderId;
        public string Status;
        public string RepeatableKind;
        public int TotalRequired;
        public int TotalPlanted;
        public bool OnlyFoundInRaid;
        public bool IsCategoryTarget;
        public readonly HashSet<string> SeenConditionIds = new(StringComparer.Ordinal);
    }

    private sealed class IndexSnapshot
    {
        public readonly Dictionary<string, List<CachedQuestReq>> ByTemplate;
        public readonly List<CachedQuestReq> CategoryReqs;
        public readonly HashSet<string> KnownQuestIds;

        public IndexSnapshot(
            Dictionary<string, List<CachedQuestReq>> byTemplate,
            List<CachedQuestReq> categoryReqs,
            HashSet<string> knownQuestIds)
        {
            ByTemplate = byTemplate;
            CategoryReqs = categoryReqs;
            KnownQuestIds = knownQuestIds;
        }
    }

    private volatile IndexSnapshot _snapshot;
    private int _refreshInFlight;
    private volatile bool _pendingRefresh;

    internal void RefreshFromServer()
    {
        if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0)
        {
            _pendingRefresh = true;
            return;
        }

        Task.Run(() =>
        {
            try
            {
                do
                {
                    _pendingRefresh = false;
                    FetchAndSwapSnapshot();
                }
                while (_pendingRefresh);
            }
            catch (Exception ex)
            {
                RaidIntelPlugin.Log?.LogWarning($"RaidIntel: failed to refresh quest index from server: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshInFlight, 0);
            }
        });
    }

    private void FetchAndSwapSnapshot()
    {
        string json = RequestHandler.GetJson("/raidIntel/quests");
        if (string.IsNullOrWhiteSpace(json))
        {
            RaidIntelPlugin.DebugLog("QuestRequirementIndex: empty response from /raidIntel/quests");
            return;
        }

        ServerQuestsResponse response = JsonConvert.DeserializeObject<ServerQuestsResponse>(json);
        if (response?.Quests == null)
        {
            RaidIntelPlugin.DebugLog("QuestRequirementIndex: deserialized quests list is null");
            return;
        }

        var byTemplate = new Dictionary<string, List<CachedQuestReq>>(512, StringComparer.Ordinal);
        var categoryReqs = new List<CachedQuestReq>(32);
        var knownQuests = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < response.Quests.Count; i++)
        {
            ServerQuestDto q = response.Quests[i];
            if (q == null || string.IsNullOrEmpty(q.QuestId))
            {
                continue;
            }

            knownQuests.Add(q.QuestId);

            if (q.Requirements == null || q.Requirements.Count == 0)
            {
                continue;
            }

            for (int j = 0; j < q.Requirements.Count; j++)
            {
                ServerQuestRequirementDto r = q.Requirements[j];
                if (r == null || (r.Completed && !r.IsPlant) || r.Targets == null || r.Targets.Count == 0 || r.RequiredCount <= 0)
                {
                    continue;
                }

                var cached = new CachedQuestReq
                {
                    QuestId = q.QuestId,
                    FallbackName = q.FallbackName ?? q.QuestId,
                    TraderId = q.TraderId ?? string.Empty,
                    Status = q.Status ?? "Locked",
                    RepeatableKind = q.RepeatableKind ?? string.Empty,
                    ConditionId = r.ConditionId ?? string.Empty,
                    Targets = r.Targets.ToArray(),
                    RequiredCount = r.RequiredCount,
                    HandedInCount = Math.Max(0, r.HandedInCount),
                    OnlyFoundInRaid = r.OnlyFoundInRaid,
                    IsWeaponAssembly = r.IsWeaponAssembly,
                    IsCategoryTarget = r.IsCategoryTarget,
                    IsPlant = r.IsPlant,
                    ServerCompleted = r.Completed,
                };

                if (cached.IsCategoryTarget)
                {
                    categoryReqs.Add(cached);
                }
                else
                {
                    var seenTargets = new HashSet<string>(StringComparer.Ordinal);
                    for (int k = 0; k < cached.Targets.Length; k++)
                    {
                        string tid = cached.Targets[k];
                        if (string.IsNullOrEmpty(tid) || !seenTargets.Add(tid))
                        {
                            continue;
                        }

                        if (!byTemplate.TryGetValue(tid, out List<CachedQuestReq> bucket))
                        {
                            bucket = new List<CachedQuestReq>(2);
                            byTemplate[tid] = bucket;
                        }
                        bucket.Add(cached);
                    }
                }
            }
        }

        _snapshot = new IndexSnapshot(byTemplate, categoryReqs, knownQuests);
        RaidIntelPlugin.DebugLog(
            $"QuestRequirementIndex refreshed: {knownQuests.Count} quests, {byTemplate.Count} direct templates, {categoryReqs.Count} category reqs");
    }

    internal List<QuestReqEntry> Query(Item item, Profile pmcProfile)
    {
        var results = new List<QuestReqEntry>();
        if (item == null)
        {
            return results;
        }

        string templateId = item.StringTemplateId;
        if (string.IsNullOrEmpty(templateId))
        {
            return results;
        }

        Dictionary<string, QuestDataClass> liveQuests = BuildLiveQuestMap(pmcProfile);
        IndexSnapshot snap = _snapshot;
        bool includePlant = RaidIntelPlugin.IncludePlantQuests == null || RaidIntelPlugin.IncludePlantQuests.Value;
        var plantGroups = new Dictionary<string, PlantGroup>(StringComparer.Ordinal);

        if (snap != null)
        {
            if (snap.ByTemplate.TryGetValue(templateId, out List<CachedQuestReq> directList))
            {
                for (int i = 0; i < directList.Count; i++)
                {
                    TryAddOverlayedEntry(directList[i], item, liveQuests, pmcProfile, includePlant, plantGroups, results);
                }
            }

            if (snap.CategoryReqs.Count > 0 && item.Template != null)
            {
                for (int i = 0; i < snap.CategoryReqs.Count; i++)
                {
                    CachedQuestReq catReq = snap.CategoryReqs[i];
                    if (MatchesAnyCategory(item.Template, templateId, catReq.Targets))
                    {
                        TryAddOverlayedEntry(catReq, item, liveQuests, pmcProfile, includePlant, plantGroups, results);
                    }
                }
            }
        }

        // Client-only fallback for active quests not yet reflected by server snapshot (or when server mod is absent).
        AppendClientFallbackActiveQuests(item, templateId, pmcProfile, snap?.KnownQuestIds, includePlant, plantGroups, results);

        EmitPlantGroups(plantGroups, results);
        results.Sort(CompareQuestEntries);
        return results;
    }

    private static Dictionary<string, QuestDataClass> BuildLiveQuestMap(Profile pmcProfile)
    {
        List<QuestDataClass> questsData = pmcProfile?.QuestsData;
        if (questsData == null || questsData.Count == 0)
        {
            return null;
        }

        var map = new Dictionary<string, QuestDataClass>(questsData.Count, StringComparer.Ordinal);
        for (int i = 0; i < questsData.Count; i++)
        {
            QuestDataClass q = questsData[i];
            if (q != null && !string.IsNullOrEmpty(q.Id))
            {
                map[q.Id] = q;
            }
        }
        return map;
    }

    private static void TryAddOverlayedEntry(
        CachedQuestReq req,
        Item item,
        Dictionary<string, QuestDataClass> liveQuests,
        Profile pmcProfile,
        bool includePlant,
        Dictionary<string, PlantGroup> plantGroups,
        List<QuestReqEntry> results)
    {
        if (req.IsPlant && (!includePlant || item.QuestItem))
        {
            return;
        }

        string status = req.Status;
        int handedIn = req.HandedInCount;
        bool liveCompleted = false;

        if (liveQuests != null && liveQuests.TryGetValue(req.QuestId, out QuestDataClass live))
        {
            if (IsTerminalQuestStatus(live.Status))
            {
                return;
            }

            if (live.CompletedConditions != null && IsValidMongoId(req.ConditionId))
            {
                MongoID condMongoId = req.ConditionId;
                liveCompleted = live.CompletedConditions.Contains(condMongoId);
            }

            if (!req.IsPlant && liveCompleted)
            {
                return;
            }

            status = live.Status.ToString();

            if (pmcProfile?.TaskConditionCounters != null && IsValidMongoId(req.ConditionId))
            {
                MongoID condMongoId = req.ConditionId;
                if (pmcProfile.TaskConditionCounters.TryGetValue(condMongoId, out TaskConditionCounter counter) && counter != null)
                {
                    handedIn = Math.Max(handedIn, counter.Value);
                }
            }
        }
        else if (status == "Success" || status == "Fail" || status == "FailRestartable" || status == "MarkedAsFailed" || status == "Expired")
        {
            return;
        }

        if (req.IsPlant)
        {
            int planted = (req.ServerCompleted || liveCompleted)
                ? req.RequiredCount
                : Math.Min(req.RequiredCount, handedIn);

            AddToPlantGroup(
                plantGroups,
                req.QuestId,
                req.ConditionId,
                req.FallbackName,
                req.TraderId,
                status,
                req.RepeatableKind,
                req.RequiredCount,
                planted,
                req.OnlyFoundInRaid,
                req.IsCategoryTarget);
            return;
        }

        if (handedIn >= req.RequiredCount)
        {
            return;
        }

        (string tooltipName, string inspectName) = FormatQuestNames(req.QuestId, req.FallbackName, req.RepeatableKind);

        results.Add(new QuestReqEntry
        {
            QuestId = req.QuestId,
            ConditionId = req.ConditionId,
            TooltipName = tooltipName,
            InspectName = inspectName,
            TraderId = req.TraderId,
            Status = status,
            RepeatableKind = req.RepeatableKind,
            RequiredCount = req.RequiredCount,
            HandedInCount = handedIn,
            OnlyFoundInRaid = req.OnlyFoundInRaid,
            IsWeaponAssembly = req.IsWeaponAssembly,
            IsCategoryTarget = req.IsCategoryTarget,
            IsPlant = false,
        });
    }

    private static void AppendClientFallbackActiveQuests(
        Item item,
        string templateId,
        Profile pmcProfile,
        HashSet<string> knownQuestIds,
        bool includePlant,
        Dictionary<string, PlantGroup> plantGroups,
        List<QuestReqEntry> results)
    {
        List<QuestDataClass> questsData = pmcProfile?.QuestsData;
        if (questsData == null || questsData.Count == 0)
        {
            return;
        }

        for (int i = 0; i < questsData.Count; i++)
        {
            QuestDataClass q = questsData[i];
            if (q == null || string.IsNullOrEmpty(q.Id) || q.Template == null)
            {
                continue;
            }

            if (knownQuestIds != null && knownQuestIds.Contains(q.Id))
            {
                continue;
            }

            if (q.Status != EQuestStatus.Started && q.Status != EQuestStatus.AvailableForFinish)
            {
                continue;
            }

            if (q.Template.Conditions == null || !q.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var conditions) || conditions == null)
            {
                continue;
            }

            string fallbackName = !string.IsNullOrEmpty(q.Template._questName)
                ? q.Template._questName
                : (q.Template is RepeatableQuestTemplate ? q.Template.Name : q.Id);
            string repeatableKind = DetectRepeatableKindFromTemplate(q);

            // Collect matching item conditions; prefer ConditionHandoverItem over ConditionFindItem if duplicated on same target.
            var matched = new List<(ConditionItem cond, bool isHandover, bool isCategory)>();
            for (int j = 0; j < conditions.Count; j++)
            {
                Condition cond = conditions[j];
                if (cond is ConditionLeaveItemAtLocation or ConditionPlaceBeacon)
                {
                    if (!includePlant || item.QuestItem)
                    {
                        continue;
                    }

                    var condZone = (ConditionZone)cond;
                    if (condZone.target == null || condZone.target.Length == 0 || Array.IndexOf(condZone.target, templateId) < 0)
                    {
                        continue;
                    }

                    int requiredPlant = Math.Max(1, (int)Math.Round(condZone.value));
                    bool condDone = q.CompletedConditions != null && q.CompletedConditions.Contains(condZone.id);
                    int counterVal = 0;
                    if (pmcProfile.TaskConditionCounters != null &&
                        pmcProfile.TaskConditionCounters.TryGetValue(condZone.id, out TaskConditionCounter plantCounter) &&
                        plantCounter != null)
                    {
                        counterVal = Math.Max(0, plantCounter.Value);
                    }

                    int planted = condDone ? requiredPlant : Math.Min(requiredPlant, counterVal);
                    AddToPlantGroup(
                        plantGroups,
                        q.Id,
                        condZone.id.ToString(),
                        fallbackName,
                        q.Template.TraderId ?? string.Empty,
                        q.Status.ToString(),
                        repeatableKind,
                        requiredPlant,
                        planted,
                        onlyFoundInRaid: false,
                        isCategoryTarget: false);
                    continue;
                }

                if (cond is not ConditionItem condItem || condItem.target == null || condItem.target.Length == 0)
                {
                    continue;
                }

                if (q.CompletedConditions != null && q.CompletedConditions.Contains(condItem.id))
                {
                    continue;
                }

                bool isCategory = false;
                if (condItem is ConditionFindItem findCond)
                {
                    try
                    {
                        isCategory = findCond.TargetIsCategory;
                    }
                    catch
                    {
                        isCategory = false;
                    }
                }

                bool hits = isCategory
                    ? (item.Template != null && MatchesAnyCategory(item.Template, templateId, condItem.target))
                    : Array.IndexOf(condItem.target, templateId) >= 0;

                if (!hits)
                {
                    continue;
                }

                matched.Add((condItem, condItem is IConditionHandoverItem, isCategory));
            }

            bool hasHandover = false;
            for (int j = 0; j < matched.Count; j++)
            {
                if (matched[j].isHandover)
                {
                    hasHandover = true;
                    break;
                }
            }

            for (int j = 0; j < matched.Count; j++)
            {
                var (condItem, isHandover, isCategory) = matched[j];
                if (hasHandover && !isHandover && condItem is ConditionFindItem)
                {
                    continue;
                }

                int required = Math.Max(1, (int)Math.Round(condItem.value));
                int handedIn = 0;
                if (isHandover && pmcProfile.TaskConditionCounters != null &&
                    pmcProfile.TaskConditionCounters.TryGetValue(condItem.id, out TaskConditionCounter counter) &&
                    counter != null)
                {
                    handedIn = Math.Max(0, counter.Value);
                }

                if (handedIn >= required)
                {
                    continue;
                }

                (string tooltipName, string inspectName) = FormatQuestNames(q.Id, fallbackName, repeatableKind);

                results.Add(new QuestReqEntry
                {
                    QuestId = q.Id,
                    ConditionId = condItem.id.ToString(),
                    TooltipName = tooltipName,
                    InspectName = inspectName,
                    TraderId = q.Template.TraderId ?? string.Empty,
                    Status = q.Status.ToString(),
                    RepeatableKind = repeatableKind,
                    RequiredCount = required,
                    HandedInCount = handedIn,
                    OnlyFoundInRaid = condItem.onlyFoundInRaid,
                    IsWeaponAssembly = condItem is ConditionWeaponAssembly,
                    IsCategoryTarget = isCategory,
                    IsPlant = false,
                });
            }
        }
    }

    private static void AddToPlantGroup(
        Dictionary<string, PlantGroup> plantGroups,
        string questId,
        string conditionId,
        string fallbackName,
        string traderId,
        string status,
        string repeatableKind,
        int requiredCount,
        int plantedCount,
        bool onlyFoundInRaid,
        bool isCategoryTarget)
    {
        if (!plantGroups.TryGetValue(questId, out PlantGroup group))
        {
            group = new PlantGroup
            {
                QuestId = questId,
                FallbackName = fallbackName,
                TraderId = traderId,
                Status = status,
                RepeatableKind = repeatableKind,
            };
            plantGroups[questId] = group;
        }

        if (!string.IsNullOrEmpty(conditionId) && !group.SeenConditionIds.Add(conditionId))
        {
            return;
        }

        group.Status = status;
        group.TotalRequired += requiredCount;
        group.TotalPlanted += plantedCount;
        group.OnlyFoundInRaid |= onlyFoundInRaid;
        group.IsCategoryTarget |= isCategoryTarget;
    }

    private static void EmitPlantGroups(Dictionary<string, PlantGroup> plantGroups, List<QuestReqEntry> results)
    {
        foreach (PlantGroup group in plantGroups.Values)
        {
            if (group.TotalPlanted >= group.TotalRequired)
            {
                continue;
            }

            (string tooltipName, string inspectName) = FormatQuestNames(group.QuestId, group.FallbackName, group.RepeatableKind);
            results.Add(new QuestReqEntry
            {
                QuestId = group.QuestId,
                ConditionId = string.Empty,
                TooltipName = tooltipName,
                InspectName = inspectName,
                TraderId = group.TraderId,
                Status = group.Status,
                RepeatableKind = group.RepeatableKind,
                RequiredCount = group.TotalRequired,
                HandedInCount = group.TotalPlanted,
                OnlyFoundInRaid = group.OnlyFoundInRaid,
                IsWeaponAssembly = false,
                IsCategoryTarget = group.IsCategoryTarget,
                IsPlant = true,
            });
        }
    }

    private static string DetectRepeatableKindFromTemplate(QuestDataClass q)
    {
        if (q.Template is RepeatableQuestTemplate)
        {
            return "Daily";
        }
        return string.Empty;
    }

    private static bool MatchesAnyCategory(ItemTemplate template, string templateId, IList<string> categoryTargets)
    {
        if (template == null || categoryTargets == null)
        {
            return false;
        }

        for (int i = 0; i < categoryTargets.Count; i++)
        {
            string catId = categoryTargets[i];
            if (string.IsNullOrEmpty(catId))
            {
                continue;
            }

            if (string.Equals(templateId, catId, StringComparison.Ordinal))
            {
                return true;
            }

            ItemTemplate cursor = template.Parent;
            while (cursor != null)
            {
                if (string.Equals(cursor.StringId, catId, StringComparison.Ordinal))
                {
                    return true;
                }
                cursor = cursor.Parent;
            }
        }

        return false;
    }

    private static bool IsTerminalQuestStatus(EQuestStatus status)
    {
        return status == EQuestStatus.Success
            || status == EQuestStatus.Fail
            || status == EQuestStatus.FailRestartable
            || status == EQuestStatus.MarkedAsFailed
            || status == EQuestStatus.Expired;
    }

    private static (string tooltipName, string inspectName) FormatQuestNames(string questId, string fallbackName, string repeatableKind)
    {
        string key = questId + " name";
        string localized = key.Localized();
        string resolvedName;
        if (!string.IsNullOrEmpty(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
        {
            resolvedName = localized;
        }
        else if (!string.IsNullOrEmpty(fallbackName))
        {
            string locFallback = fallbackName.Localized();
            resolvedName = !string.IsNullOrEmpty(locFallback) && !string.Equals(locFallback, fallbackName, StringComparison.Ordinal)
                ? locFallback
                : fallbackName;
        }
        else
        {
            resolvedName = questId;
        }

        if (string.Equals(repeatableKind, "Daily", StringComparison.OrdinalIgnoreCase))
        {
            string inspect = string.IsNullOrEmpty(resolvedName) || string.Equals(resolvedName, questId, StringComparison.Ordinal)
                ? "日常任务"
                : $"[日常] {resolvedName}";
            return ("日常任务", inspect);
        }

        if (string.Equals(repeatableKind, "Weekly", StringComparison.OrdinalIgnoreCase))
        {
            string inspect = string.IsNullOrEmpty(resolvedName) || string.Equals(resolvedName, questId, StringComparison.Ordinal)
                ? "周常任务"
                : $"[周常] {resolvedName}";
            return ("周常任务", inspect);
        }

        return (resolvedName, resolvedName);
    }

    private static int CompareQuestEntries(QuestReqEntry a, QuestReqEntry b)
    {
        int pa = StatusPriority(a);
        int pb = StatusPriority(b);
        if (pa != pb)
        {
            return pa.CompareTo(pb);
        }
        return string.Compare(a.InspectName, b.InspectName, StringComparison.Ordinal);
    }

    private static int StatusPriority(QuestReqEntry q)
    {
        if (q.IsActive) return 0;
        if (q.IsAvailable) return 1;
        return 2;
    }

    private static bool IsValidMongoId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length != 24)
        {
            return false;
        }

        for (int i = 0; i < 24; i++)
        {
            char c = id[i];
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex)
            {
                return false;
            }
        }
        return true;
    }
}
