using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils.Json;

namespace RaidIntel.Server;

[Injectable]
public class QuestRequirementService(
    TemplateTable templateTable,
    QuestHelper questHelper,
    SaveServer saveServer,
    LocaleService localeService,
    ISptLogger<QuestRequirementService> logger)
{
    private const int MaxReachabilityIterations = 15;

    public QuestIntelResponse BuildQuestIntel(MongoId sessionId)
    {
        PmcData? pmc = saveServer.GetProfile(sessionId)?.CharacterData?.PmcData;
        if (pmc == null)
        {
            return new QuestIntelResponse();
        }

        // Build lookup of PMC quest statuses by QId
        var pmcQuestById = new Dictionary<MongoId, QuestStatus>();
        if (pmc.Quests != null)
        {
            foreach (var qs in pmc.Quests)
            {
                pmcQuestById[qs.QId] = qs;
            }
        }

        // Build lookup of condition counters by condition MongoId
        var conditionCounters = new Dictionary<MongoId, int>();
        if (pmc.TaskConditionCounters != null)
        {
            foreach (var (key, counter) in pmc.TaskConditionCounters)
            {
                var value = (int)Math.Floor(counter?.Value ?? 0);
                conditionCounters[key] = value;
                if (counter?.Id is { } counterId)
                {
                    conditionCounters[counterId] = value;
                }
            }
        }

        // Reachability filtering:
        // 1) Exclude permanently terminal quests from output.
        // 2) Exclude quests not in pmc.Quests that fail static eligibility checks; mark them unreachable.
        var excludedFromOutput = new HashSet<MongoId>();
        var unreachableQuests = new HashSet<MongoId>();

        foreach (var quest in templateTable.Quests.Values)
        {
            if (pmcQuestById.TryGetValue(quest.Id, out var qStatus))
            {
                if (IsTerminalStatus(qStatus.Status, quest))
                {
                    excludedFromOutput.Add(quest.Id);
                }
            }
            else if (!IsStaticallyEligible(pmc, quest))
            {
                excludedFromOutput.Add(quest.Id);
                unreachableQuests.Add(quest.Id);
            }
        }

        // 3) Fixed-point prerequisite reachability pass:
        // Any quest not already active whose AvailableForStart Quest prerequisites are impossible
        // becomes unreachable, propagating down locked-out chains.
        for (var iter = 0; iter < MaxReachabilityIterations; iter++)
        {
            var addedAny = false;

            foreach (var quest in templateTable.Quests.Values)
            {
                if (unreachableQuests.Contains(quest.Id))
                {
                    continue;
                }

                if (pmcQuestById.TryGetValue(quest.Id, out var qStatus))
                {
                    // Already active or terminal (terminal handled separately; active is already unlocked)
                    if (IsActiveOrRestartableStatus(qStatus.Status, quest) || IsTerminalStatus(qStatus.Status, quest))
                    {
                        continue;
                    }
                }

                var startPrereqs = quest.Conditions?.AvailableForStart?.GetQuestConditions();
                if (startPrereqs == null || startPrereqs.Count == 0)
                {
                    continue;
                }

                var impossible = false;
                foreach (var cond in startPrereqs)
                {
                    if (!string.Equals(cond.ConditionType, "Quest", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var targets = ExtractTargets(cond.Target);
                    if (targets.Count == 0)
                    {
                        continue;
                    }

                    if (targets.All(t => IsPrereqTargetImpossible(t, cond.Status, pmcQuestById, unreachableQuests)))
                    {
                        impossible = true;
                        break;
                    }
                }

                if (impossible)
                {
                    unreachableQuests.Add(quest.Id);
                    excludedFromOutput.Add(quest.Id);
                    addedAny = true;
                }
            }

            if (!addedAny)
            {
                break;
            }
        }

        var availableForStartIds = new HashSet<MongoId>();
        try
        {
            var clientQuests = questHelper.GetClientQuests(sessionId);
            if (clientQuests != null)
            {
                foreach (var cq in clientQuests)
                {
                    if (cq.SptStatus == QuestStatusEnum.AvailableForStart)
                    {
                        availableForStartIds.Add(cq.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"RaidIntel: GetClientQuests failed for session {sessionId}: {ex.Message}");
        }

        var localeDb = localeService.GetLocaleDb();
        var resultQuests = new List<QuestIntelEntry>();
        var emittedQuestIds = new HashSet<MongoId>();

        foreach (var quest in templateTable.Quests.Values)
        {
            if (excludedFromOutput.Contains(quest.Id))
            {
                continue;
            }

            pmcQuestById.TryGetValue(quest.Id, out var qStatus);
            var requirements = ExtractItemRequirements(quest, qStatus, conditionCounters);
            if (requirements.Count == 0)
            {
                continue;
            }

            emittedQuestIds.Add(quest.Id);
            resultQuests.Add(new QuestIntelEntry
            {
                QuestId = quest.Id,
                FallbackName = ResolveFallbackName(quest, localeDb),
                TraderId = quest.TraderId,
                Status = MapQuestStatus(qStatus, quest, availableForStartIds),
                RepeatableKind = "",
                Requirements = requirements,
            });
        }

        // Active Repeatable Quests (Daily / Weekly)
        if (pmc.RepeatableQuests != null)
        {
            foreach (var group in pmc.RepeatableQuests)
            {
                if (group?.ActiveQuests == null)
                {
                    continue;
                }

                var repeatableKind = group.Name?.Contains("weekly", StringComparison.OrdinalIgnoreCase) == true
                    ? "Weekly"
                    : "Daily";

                foreach (var rq in group.ActiveQuests)
                {
                    if (emittedQuestIds.Contains(rq.Id))
                    {
                        continue;
                    }

                    if (!pmcQuestById.TryGetValue(rq.Id, out var qStatus))
                    {
                        continue;
                    }

                    if (qStatus.Status is not (QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish))
                    {
                        continue;
                    }

                    var requirements = ExtractItemRequirements(rq, qStatus, conditionCounters);
                    if (requirements.Count == 0)
                    {
                        continue;
                    }

                    emittedQuestIds.Add(rq.Id);
                    resultQuests.Add(new QuestIntelEntry
                    {
                        QuestId = rq.Id,
                        FallbackName = ResolveFallbackName(rq, localeDb),
                        TraderId = rq.TraderId,
                        Status = MapQuestStatus(qStatus, rq, availableForStartIds),
                        RepeatableKind = repeatableKind,
                        Requirements = requirements,
                    });
                }
            }
        }

        logger.Debug($"RaidIntel: built {resultQuests.Count} quest intel entries for session {sessionId}");
        return new QuestIntelResponse { Quests = resultQuests };
    }

    private bool IsStaticallyEligible(PmcData pmc, Quest quest)
    {
        if (!string.IsNullOrEmpty(pmc.Info?.Side) && questHelper.QuestIsForOtherSide(pmc.Info.Side, quest.Id))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(pmc.Info?.GameVersion))
        {
            if (questHelper.QuestIsProfileBlacklisted(pmc.Info.GameVersion, quest.Id))
            {
                return false;
            }

            if (!questHelper.QuestIsProfileWhitelisted(pmc.Info.GameVersion, quest.Id))
            {
                return false;
            }
        }

        if (!questHelper.ShowEventQuestToPlayer(quest.Id))
        {
            return false;
        }

        if (pmc.TradersInfo != null && !pmc.TradersInfo.ContainsKey(quest.TraderId))
        {
            return false;
        }

        return true;
    }

    private static bool IsTerminalStatus(QuestStatusEnum status, Quest quest)
    {
        return status switch
        {
            QuestStatusEnum.Success => true,
            QuestStatusEnum.MarkedAsFailed => true,
            QuestStatusEnum.Expired => true,
            QuestStatusEnum.Fail => !quest.Restartable,
            QuestStatusEnum.FailRestartable => !quest.Restartable,
            _ => false,
        };
    }

    private static bool IsActiveOrRestartableStatus(QuestStatusEnum status, Quest quest)
    {
        return status switch
        {
            QuestStatusEnum.Started => true,
            QuestStatusEnum.AvailableForFinish => true,
            QuestStatusEnum.AvailableForStart => true,
            QuestStatusEnum.AvailableAfter => true,
            QuestStatusEnum.Fail or QuestStatusEnum.FailRestartable => quest.Restartable,
            _ => false,
        };
    }

    private bool IsPrereqTargetImpossible(
        string targetQuestId,
        HashSet<QuestStatusEnum>? requiredStatuses,
        Dictionary<MongoId, QuestStatus> pmcQuestById,
        HashSet<MongoId> unreachableQuests)
    {
        if (!MongoId.IsValidMongoId(targetQuestId))
        {
            return true;
        }

        var prereqId = new MongoId(targetQuestId);

        if (pmcQuestById.TryGetValue(prereqId, out var prereqStatus)
            && templateTable.Quests.TryGetValue(prereqId, out var prereqQuest)
            && IsTerminalStatus(prereqStatus.Status, prereqQuest))
        {
            // Prereq has a permanent terminal status: impossible iff requiredStatuses doesn't allow it.
            return requiredStatuses != null
                && requiredStatuses.Count > 0
                && !requiredStatuses.Contains(prereqStatus.Status);
        }

        // Not terminal in PMC data: impossible if missing from DB or itself unreachable.
        if (!templateTable.Quests.ContainsKey(prereqId))
        {
            return true;
        }

        return unreachableQuests.Contains(prereqId);
    }

    private List<QuestItemReqDto> ExtractItemRequirements(
        Quest quest,
        QuestStatus? qStatus,
        Dictionary<MongoId, int> conditionCounters)
    {
        var finishConditions = quest.Conditions?.AvailableForFinish;
        if (finishConditions == null || finishConditions.Count == 0)
        {
            return new List<QuestItemReqDto>();
        }

        var completedSet = qStatus?.CompletedConditions != null
            ? new HashSet<string>(qStatus.CompletedConditions, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var results = new List<QuestItemReqDto>();
        var handoverEntries = new List<(QuestItemReqDto Dto, HashSet<string> TargetSet)>();
        var completedHandoverTargetSets = new List<HashSet<string>>();
        var findConditions = new List<(QuestCondition Cond, List<string> Targets, HashSet<string> TargetSet)>();
        var plantGroups = new List<(HashSet<string> TargetSet, List<(QuestCondition Cond, List<string> Targets, int RequiredCount, int CounterVal, bool IsCompleted)> Items)>();

        foreach (var cond in finishConditions)
        {
            var condType = cond.ConditionType;
            if (condType is not ("HandoverItem" or "FindItem" or "WeaponAssembly" or "LeaveItemAtLocation" or "PlaceBeacon"))
            {
                continue;
            }

            var targets = ExtractTargets(cond.Target);
            if (targets.Count == 0)
            {
                continue;
            }

            var targetSet = new HashSet<string>(targets, StringComparer.Ordinal);

            if (condType is "LeaveItemAtLocation" or "PlaceBeacon")
            {
                if (AreAllTargetsQuestItems(targets))
                {
                    continue;
                }

                var plantRequired = Math.Max(1, (int)Math.Round(cond.Value ?? 1));
                var plantCounter = Math.Max(0, conditionCounters.GetValueOrDefault(cond.Id, 0));
                var plantCompleted = completedSet.Contains(cond.Id) || plantCounter >= plantRequired;

                var existingGroup = plantGroups.FirstOrDefault(g => g.TargetSet.SetEquals(targetSet));
                if (existingGroup.Items == null)
                {
                    existingGroup = (targetSet, new List<(QuestCondition, List<string>, int, int, bool)>());
                    plantGroups.Add(existingGroup);
                }

                existingGroup.Items.Add((cond, targets, plantRequired, plantCounter, plantCompleted));
                continue;
            }

            var isCompleted = completedSet.Contains(cond.Id);

            if (isCompleted)
            {
                if (condType == "HandoverItem")
                {
                    completedHandoverTargetSets.Add(targetSet);
                }
                continue;
            }

            if (condType == "FindItem")
            {
                findConditions.Add((cond, targets, targetSet));
                continue;
            }

            var requiredCount = Math.Max(1, (int)Math.Round(cond.Value ?? 1));
            var handedInCount = conditionCounters.GetValueOrDefault(cond.Id, 0);
            var dto = new QuestItemReqDto
            {
                ConditionId = cond.Id,
                Targets = targets,
                RequiredCount = requiredCount,
                HandedInCount = handedInCount,
                Completed = false,
                OnlyFoundInRaid = cond.OnlyFoundInRaid ?? false,
                IsWeaponAssembly = condType == "WeaponAssembly",
                IsCategoryTarget = HasCategoryTarget(targets),
            };

            results.Add(dto);
            if (condType == "HandoverItem")
            {
                handoverEntries.Add((dto, targetSet));
            }
        }

        foreach (var (_, items) in plantGroups)
        {
            if (items.All(static x => x.IsCompleted))
            {
                continue;
            }

            foreach (var (cond, targets, requiredCount, counterVal, isCompleted) in items)
            {
                results.Add(new QuestItemReqDto
                {
                    ConditionId = cond.Id,
                    Targets = targets,
                    RequiredCount = requiredCount,
                    HandedInCount = isCompleted ? requiredCount : Math.Min(requiredCount, counterVal),
                    Completed = isCompleted,
                    OnlyFoundInRaid = cond.OnlyFoundInRaid ?? false,
                    IsWeaponAssembly = false,
                    IsCategoryTarget = HasCategoryTarget(targets),
                    IsPlant = true,
                });
            }
        }

        foreach (var (cond, targets, targetSet) in findConditions)
        {
            var pairedHandover = handoverEntries.FirstOrDefault(h => h.TargetSet.SetEquals(targetSet));
            if (pairedHandover.Dto != null)
            {
                if (cond.OnlyFoundInRaid == true)
                {
                    pairedHandover.Dto.OnlyFoundInRaid = true;
                }
                continue;
            }

            if (completedHandoverTargetSets.Any(s => s.SetEquals(targetSet)))
            {
                continue;
            }

            var requiredCount = Math.Max(1, (int)Math.Round(cond.Value ?? 1));
            results.Add(new QuestItemReqDto
            {
                ConditionId = cond.Id,
                Targets = targets,
                RequiredCount = requiredCount,
                HandedInCount = 0,
                Completed = false,
                OnlyFoundInRaid = cond.OnlyFoundInRaid ?? false,
                IsWeaponAssembly = false,
                IsCategoryTarget = HasCategoryTarget(targets),
            });
        }

        return results;
    }

    private bool AreAllTargetsQuestItems(List<string> targets)
    {
        if (targets.Count == 0)
        {
            return false;
        }

        foreach (var targetId in targets)
        {
            if (!MongoId.IsValidMongoId(targetId)
                || !templateTable.Items.TryGetValue(new MongoId(targetId), out var item)
                || item.Properties?.QuestItem != true)
            {
                return false;
            }
        }

        return true;
    }

    private bool HasCategoryTarget(List<string> targets)
    {
        foreach (var targetId in targets)
        {
            if (MongoId.IsValidMongoId(targetId)
                && templateTable.Items.TryGetValue(new MongoId(targetId), out var item)
                && string.Equals(item.Type, "Node", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> ExtractTargets(ListOrT<string>? target)
    {
        if (target == null)
        {
            return new List<string>();
        }

        if (target.IsList && target.List != null)
        {
            return target.List.Where(static t => !string.IsNullOrEmpty(t)).ToList();
        }

        if (target.IsItem && !string.IsNullOrEmpty(target.Item))
        {
            return new List<string> { target.Item };
        }

        return new List<string>();
    }

    private static string ResolveFallbackName(Quest quest, Dictionary<string, string> localeDb)
    {
        if (localeDb.TryGetValue($"{quest.Id} name", out var localized) && !string.IsNullOrEmpty(localized))
        {
            return localized;
        }

        if (!string.IsNullOrEmpty(quest.Name)
            && localeDb.TryGetValue(quest.Name, out var byNameKey)
            && !string.IsNullOrEmpty(byNameKey))
        {
            return byNameKey;
        }

        if ((quest is RepeatableQuest || quest.Name?.StartsWith("DailyQuestName/", StringComparison.Ordinal) == true)
            && localeDb.TryGetValue($"DailyQuestName/{quest.Type}", out var dailyName)
            && !string.IsNullOrEmpty(dailyName))
        {
            return dailyName;
        }

        if (!string.IsNullOrEmpty(quest.QuestName))
        {
            return quest.QuestName;
        }

        return "";
    }

    private static string MapQuestStatus(QuestStatus? qStatus, Quest quest, HashSet<MongoId> availableForStartIds)
    {
        if (qStatus == null)
        {
            return availableForStartIds.Contains(quest.Id) ? "AvailableForStart" : "Locked";
        }

        return qStatus.Status switch
        {
            QuestStatusEnum.Started => "Started",
            QuestStatusEnum.AvailableForFinish => "AvailableForFinish",
            QuestStatusEnum.AvailableForStart => "AvailableForStart",
            QuestStatusEnum.AvailableAfter => "AvailableAfter",
            QuestStatusEnum.Fail or QuestStatusEnum.FailRestartable when quest.Restartable => "AvailableForStart",
            _ => "Locked",
        };
    }
}
