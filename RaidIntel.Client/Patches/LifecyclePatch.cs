using System;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.InventoryLogic;
using EFT.Quests;
using HarmonyLib;
using RaidIntel.Client.Core;

namespace RaidIntel.Client.Patches;

internal static class LifecyclePatch
{
    private static Profile _pmcProfile;

    internal static Profile GetPmcProfile()
    {
        if (_pmcProfile != null)
        {
            return _pmcProfile;
        }

        if (Singleton<HideoutRepresentation>.Instantiated)
        {
            Profile hideoutProfile = Singleton<HideoutRepresentation>.Instance?._session?.Profile;
            if (hideoutProfile != null)
            {
                _pmcProfile = hideoutProfile;
                return _pmcProfile;
            }
        }

        if (Singleton<ClientApplication<IEftSession>>.Instantiated)
        {
            Profile appProfile = Singleton<ClientApplication<IEftSession>>.Instance?.Session?.Profile;
            if (appProfile != null)
            {
                _pmcProfile = appProfile;
                return _pmcProfile;
            }
        }

        return null;
    }

    internal static void Apply(Harmony harmony)
    {
        MethodInfo setInv = AccessTools.Method(typeof(HideoutRepresentation), nameof(HideoutRepresentation.SetInventoryController));
        if (setInv != null)
        {
            harmony.Patch(setInv, postfix: new HarmonyMethod(typeof(LifecyclePatch), nameof(SetInventoryControllerPostfix)));
        }

        MethodInfo localGameCreate = AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.LocalGameCreate));
        if (localGameCreate != null)
        {
            harmony.Patch(localGameCreate, prefix: new HarmonyMethod(typeof(LifecyclePatch), nameof(LocalGameCreatePrefix)));
        }

        MethodInfo onGameStarted = AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));
        if (onGameStarted != null)
        {
            harmony.Patch(onGameStarted, postfix: new HarmonyMethod(typeof(LifecyclePatch), nameof(OnGameStartedPostfix)));
        }

        PatchAsyncQuestRefresh(harmony, "HandoverItem");
        PatchAsyncQuestRefresh(harmony, "AcceptQuest");
        PatchAsyncQuestRefresh(harmony, "FinishQuest");

        PatchHideoutUpgradeDirty(harmony, "UpgradeZone");
        PatchHideoutUpgradeDirty(harmony, "CompleteUpgradeZone");

        PatchFavoriteSchemeDirty(harmony, nameof(HideoutRepresentation.AddFavoriteScheme));
        PatchFavoriteSchemeDirty(harmony, nameof(HideoutRepresentation.RemoveFavoriteScheme));
        PatchFavoriteSchemeDirty(harmony, nameof(HideoutRepresentation.DeInit));
    }

    private static void PatchAsyncQuestRefresh(Harmony harmony, string methodName)
    {
        MethodInfo method = AccessTools.Method(typeof(QuestControllerClientBackend), methodName);
        if (method != null)
        {
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(LifecyclePatch), nameof(QuestMutationPostfix)));
            RaidIntelPlugin.DebugLog($"LifecyclePatch: hooked QuestControllerClientBackend.{methodName}");
        }
    }

    private static void PatchHideoutUpgradeDirty(Harmony harmony, string methodName)
    {
        MethodInfo method = AccessTools.Method(typeof(HideoutRepresentation), methodName);
        if (method != null)
        {
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(LifecyclePatch), nameof(HideoutUpgradePostfix)));
            RaidIntelPlugin.DebugLog($"LifecyclePatch: hooked HideoutRepresentation.{methodName}");
        }
    }

    private static void PatchFavoriteSchemeDirty(Harmony harmony, string methodName)
    {
        MethodInfo method = AccessTools.Method(typeof(HideoutRepresentation), methodName);
        if (method != null)
        {
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(LifecyclePatch), nameof(FavoriteSchemePostfix)));
            RaidIntelPlugin.DebugLog($"LifecyclePatch: hooked HideoutRepresentation.{methodName}");
        }
    }

    private static void SetInventoryControllerPostfix(
        HideoutRepresentation __instance,
        InventoryController inventoryController)
    {
        try
        {
            if (__instance?._session?.Profile != null)
            {
                _pmcProfile = __instance._session.Profile;
            }

            StashTracker.Instance.AttachMenuController(inventoryController, _pmcProfile);
            HideoutRequirementIndex.Instance.RebuildFromHideout();
            FavoriteCraftRequirementIndex.Instance.RebuildFromHideout();
            QuestRequirementIndex.Instance.RefreshFromServer();
        }
        catch (Exception ex)
        {
            RaidIntelPlugin.Log?.LogError($"RaidIntel: SetInventoryControllerPostfix error: {ex}");
        }
    }

    private static void LocalGameCreatePrefix(TarkovApplication __instance)
    {
        try
        {
            Profile pmcProfile = __instance?.Session?.Profile ?? GetPmcProfile();
            if (pmcProfile != null)
            {
                _pmcProfile = pmcProfile;
            }

            StashTracker.Instance.FreezeSnapshotFromProfile(_pmcProfile);
            HideoutRequirementIndex.Instance.RebuildFromHideout();
            FavoriteCraftRequirementIndex.Instance.RebuildFromHideout();
        }
        catch (Exception ex)
        {
            RaidIntelPlugin.Log?.LogError($"RaidIntel: LocalGameCreatePrefix error: {ex}");
        }
    }

    private static void OnGameStartedPostfix(GameWorld __instance)
    {
        try
        {
            if (__instance == null || __instance is HideoutGameWorld || __instance.MainPlayer == null)
            {
                return;
            }

            RaidCarryTracker.Instance.OnRaidStart(__instance.MainPlayer);
            GameWorld.OnDispose -= OnGameWorldDispose;
            GameWorld.OnDispose += OnGameWorldDispose;
        }
        catch (Exception ex)
        {
            RaidIntelPlugin.Log?.LogError($"RaidIntel: OnGameStartedPostfix error: {ex}");
        }
    }

    private static void OnGameWorldDispose()
    {
        GameWorld.OnDispose -= OnGameWorldDispose;
        RaidCarryTracker.Instance.OnRaidEnd();
    }

    private static void QuestMutationPostfix(Task __result)
    {
        if (__result == null)
        {
            QuestRequirementIndex.Instance.RefreshFromServer();
            return;
        }

        __result.ContinueWith(t =>
        {
            if (!t.IsFaulted && !t.IsCanceled)
            {
                QuestRequirementIndex.Instance.RefreshFromServer();
            }
        });
    }

    private static void HideoutUpgradePostfix(Task __result)
    {
        HideoutRequirementIndex.Instance.MarkDirty();
        __result?.ContinueWith(_ => HideoutRequirementIndex.Instance.MarkDirty());
    }

    private static void FavoriteSchemePostfix()
    {
        FavoriteCraftRequirementIndex.Instance.MarkDirty();
    }
}
