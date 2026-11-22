using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using HarmonyLib;
using RaidIntel.Client.Patches;

namespace RaidIntel.Client;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class RaidIntelPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.spt.raidintel.client";
    public const string PluginName = "RaidIntel";
    public const string PluginVersion = "0.1.0";

    internal static ManualLogSource Log { get; private set; }
    internal static RaidIntelPlugin Instance { get; private set; }

    // 1. 通用开关
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> EnableInRaid;
    internal static ConfigEntry<bool> EnableOutOfRaid;
    internal static ConfigEntry<bool> IncludePlantQuests;

    // 2. 悬停提示 (Tooltip)
    internal static ConfigEntry<bool> EnableHoverTooltip;
    internal static ConfigEntry<bool> TooltipShowOwned;
    internal static ConfigEntry<bool> TooltipShowActiveQuests;
    internal static ConfigEntry<bool> TooltipShowNextHideout;
    internal static ConfigEntry<bool> TooltipShowFutureSummary;
    internal static ConfigEntry<bool> TooltipShowOwnedWhenNoReq;
    internal static ConfigEntry<int> TooltipMaxLines;

    // 3. 物品检视窗 (Inspect)
    internal static ConfigEntry<bool> EnableInspectPanel;
    internal static ConfigEntry<bool> InspectShowFutureQuests;
    internal static ConfigEntry<bool> InspectShowFutureHideout;
    internal static ConfigEntry<bool> InspectMergeDescription;
    internal static ConfigEntry<bool> InspectMergeIntoScroll;
    internal static ConfigEntry<bool> InspectResizeWindow;
    internal static ConfigEntry<int> InspectWindowWidth;
    internal static ConfigEntry<float> InspectWindowHeightRatio;

    // 9. 调试
    internal static ConfigEntry<bool> DebugLogging;

    private Harmony _harmony;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        BindConfig();

        _harmony = new Harmony(PluginGuid);
        ApplyPatches();

        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }

    private void BindConfig()
    {
        var t = new ConfigText(Config);

        const string catGeneral = "1. 通用开关";
        Enabled = t.Bind(
            "1. General", "Enabled", true,
            catGeneral, "总开关",
            "启用或关闭 RaidIntel 物品需求情报提示与检视面板。");
        EnableInRaid = t.Bind(
            "1. General", "EnableInRaid", true,
            catGeneral, "战局内启用",
            "进入战局（PMC 或 Scav）时启用物品悬停提示与检视面板（自动统计仓库快照与局内携带数量）。");
        EnableOutOfRaid = t.Bind(
            "1. General", "EnableOutOfRaid", true,
            catGeneral, "局外仓库/商人启用",
            "在主菜单、仓库、商人、跳蚤市场及藏身处界面启用物品需求提示与检视面板。");
        IncludePlantQuests = t.Bind(
            "1. General", "IncludePlantQuests", true,
            catGeneral, "包含埋放/标记类任务",
            "将需要带入战局放置或标记的物品（如信标、摄像头、干扰器、任务埋点物品）纳入任务需求提示。");

        const string catTooltip = "2. 悬停提示 (Tooltip)";
        EnableHoverTooltip = t.Bind(
            "2. Tooltip", "EnableHoverTooltip", true,
            catTooltip, "启用悬停提示追加",
            "鼠标悬停在物品上时，在原版名称下方追加持有量、当前任务与藏身处下一级需求。");
        TooltipShowOwned = t.Bind(
            "2. Tooltip", "TooltipShowOwned", true,
            catTooltip, "显示当前拥有数量",
            "在悬停提示首行显示「拥有：X（FIR：Y）」统计。");
        TooltipShowActiveQuests = t.Bind(
            "2. Tooltip", "TooltipShowActiveQuests", true,
            catTooltip, "显示进行中任务需求",
            "在悬停提示中显示当前进行中或可提交任务对该物品的进度。");
        TooltipShowNextHideout = t.Bind(
            "2. Tooltip", "TooltipShowNextHideout", true,
            catTooltip, "显示藏身处下一级需求",
            "在悬停提示中显示藏身处设施下一未建造/未升级等级所需的材料进度。");
        TooltipShowFutureSummary = t.Bind(
            "2. Tooltip", "TooltipShowFutureSummary", true,
            catTooltip, "显示后续需求汇总行",
            "存在未解锁或更高等级需求时，在悬停提示末尾追加「检视 N 项后续需求」。");
        TooltipShowOwnedWhenNoReq = t.Bind(
            "2. Tooltip", "TooltipShowOwnedWhenNoReq", true,
            catTooltip, "无需求时仍显示拥有数量",
            "物品没有任何当前或后续任务/藏身处需求时，是否仍显示「拥有：X（FIR：Y）」。");
        TooltipMaxLines = t.Bind(
            "2. Tooltip", "TooltipMaxLines", 6,
            catTooltip, "悬停需求最大行数",
            "悬停提示最多显示的当前需求条目行数，超出部分将折叠提示。",
            ConfigText.Range(2, 12));

        const string catInspect = "3. 物品检视窗 (Inspect)";
        EnableInspectPanel = t.Bind(
            "3. Inspect", "EnableInspectPanel", true,
            catInspect, "启用检视窗情报卡片",
            "打开物品检视窗口时，在物品描述位置嵌入完整的需求情报卡片。");
        InspectMergeDescription = t.Bind(
            "3. Inspect", "InspectMergeDescription", true,
            catInspect, "将物品描述并入情报卡片",
            "开启后物品原版描述显示在情报卡片顶部（原版描述文本隐藏）；关闭则保留原版描述，卡片显示在其下方。");
        InspectMergeIntoScroll = t.Bind(
            "3. Inspect", "InspectMergeIntoScroll", true,
            catInspect, "属性与配件并入滚动区",
            "将检视窗的属性条、快速属性与配件格移入下方描述滚动区，预览图与按钮以下合并为一整块可滚动阅读区域。对新打开的检视窗生效。");
        InspectResizeWindow = t.Bind(
            "3. Inspect", "InspectResizeWindow", true,
            catInspect, "窄长检视窗",
            "原版检视窗默认以 670×500 打开。开启后默认改为下方设置的宽度与屏幕高度占比打开（带配件的武器仍保持原版所需的最小宽度），多出的高度用于显示描述与情报卡片；打开后仍可拖动窗口边缘自由缩放。");
        InspectWindowWidth = t.Bind(
            "3. Inspect", "InspectWindowWidth", 460,
            catInspect, "检视窗宽度",
            "窄长检视窗的宽度（UI 单位，原版 670）。对新打开的检视窗生效。",
            ConfigText.Range(400, 670));
        InspectWindowHeightRatio = t.Bind(
            "3. Inspect", "InspectWindowHeightRatio", 0.85f,
            catInspect, "检视窗高度占屏比例",
            "窄长检视窗高度占屏幕高度的比例（不会低于原版 500）。对新打开的检视窗生效。",
            ConfigText.Range(0.5f, 0.95f));
        InspectShowFutureQuests = t.Bind(
            "3. Inspect", "InspectShowFutureQuests", true,
            catInspect, "显示后续未解锁任务",
            "在检视窗需求情报卡片中列出可接取与尚未解锁的后续任务需求。");
        InspectShowFutureHideout = t.Bind(
            "3. Inspect", "InspectShowFutureHideout", true,
            catInspect, "显示后续更高等级藏身处",
            "在检视窗需求情报卡片中列出超出下一级以外的后续藏身处升级需求。");

        t.Advanced = true;
        const string catDebug = "9. 调试";
        DebugLogging = t.Bind(
            "9. Debug", "DebugLogging", false,
            catDebug, "调试日志",
            "向 BepInEx/LogOutput.log 输出生命周期、仓库快照与任务索引刷新调试日志。");
    }

    internal static bool IsActiveForCurrentContext()
    {
        if (Enabled == null || !Enabled.Value)
        {
            return false;
        }

#pragma warning disable CS0618
        bool inRaid = InGameStatus.InRaid;
#pragma warning restore CS0618

        return inRaid ? EnableInRaid.Value : EnableOutOfRaid.Value;
    }

    internal static void DebugLog(string message)
    {
        if (DebugLogging != null && DebugLogging.Value && Log != null)
        {
            Log.LogInfo($"[RaidIntel Debug] {message}");
        }
    }

    private void ApplyPatches()
    {
        TryApply("LifecyclePatch", () => LifecyclePatch.Apply(_harmony));
        TryApply("TooltipPatch", () => TooltipPatch.Apply(_harmony));
        TryApply("InspectPanelPatch", () => InspectPanelPatch.Apply(_harmony));
    }

    private static void TryApply(string name, Action apply)
    {
        try
        {
            apply();
            Log.LogInfo($"{PluginName}: patched {name}");
        }
        catch (Exception ex)
        {
            Log.LogError($"{PluginName}: failed to apply {name}: {ex}");
        }
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
