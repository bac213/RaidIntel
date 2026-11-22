# RaidIntel (SPT 4.1.6 Server + Client mod) — working notes

Split out from `HideoutQoL` Phase 2 into a standalone SPT 4.1.6 (`net10.0` server + `net472` BepInEx client) mod providing in-raid and out-of-raid item requirement intelligence.

## Architecture

### 1. Server (`RaidIntel.Server`, `net10.0`)
- `ModMetadata.cs`: SPT mod metadata (`com.spt.raidintel`, `0.1.0`, SPT `~4.1.5`).
- `Models.cs`: JSON DTOs for `/raidIntel/quests`.
- `QuestRequirementService.cs`: Reads `TemplateTable.Quests`, `SaveServer.GetProfile(sessionId)?.CharacterData?.PmcData`, `QuestHelper`, and `LocaleService`. Performs static eligibility and iterative prerequisite reachability filtering (`MaxReachabilityIterations = 15`), extracts unfinished item/category/weapon-assembly and plant/mark (`LeaveItemAtLocation` / `PlaceBeacon`, `isPlant`) conditions as well as repeatable (Daily/Weekly) quest requirements with live `TaskConditionCounters` progress.
- `RaidIntelRouter.cs`: Registers `GET /raidIntel/quests` via `StaticRouter` and `RaidIntelRouterCallback`.

### 2. Client (`RaidIntel.Client`, `net472`)
- `RaidIntelPlugin.cs` + `ConfigText.cs`: BepInEx entry (`com.spt.raidintel.client`, `0.1.0`) with Chinese F12 settings (`1. 通用开关`, `2. 悬停提示 (Tooltip)`, `3. 物品检视窗 (Inspect)`, `9. 调试`).
- `Core/StashTracker.cs`: Lazily indexes PMC `Inventory.Stash` + `QuestStashItems` (`_counts`) and out-of-raid `Inventory.Equipment` (`_menuEquipCounts`) via `GetAllItemsNonAlloc(..., onlyMerged: false, includeParent: false)`. Freezes the stash snapshot in `TarkovApplication.LocalGameCreate` Prefix before EFT nulls `profile.Inventory.Stash`.
- `Core/RaidCarryTracker.cs`: Tracks in-raid carried items on `GameWorld.MainPlayer` (`Profile.Inventory.Equipment`) for both PMC and Scav raids.
- `Core/QuestRequirementIndex.cs`: Fetches `/raidIntel/quests` asynchronously via `SPT.Common.Http.RequestHandler.GetJson`, builds O(1) `TemplateId` + category-chain index (`ItemTemplate.Parent`), overlays live PMC `QuestsData` / `TaskConditionCounters`, merges multi-zone `isPlant` (`LeaveItemAtLocation` / `PlaceBeacon`) conditions per quest, and provides a client-only active-quest fallback.
- `Core/HideoutRequirementIndex.cs`: Indexes all unpaid Hideout `Stage.Requirements.Data` (`ItemRequirement`) from `HideoutRepresentation.AreaDatas`, distinguishing next-level vs future stages.
- `Core/ItemIntelAggregator.cs` + `Core/IntelModels.cs`: Aggregates owned breakdown (stash + carry/equipment) with quest/hideout requirements and formats the compact hover tooltip.
- `Patches/LifecyclePatch.cs`: Hooks `HideoutRepresentation.SetInventoryController`, `TarkovApplication.LocalGameCreate`, `GameWorld.OnGameStarted` / `OnDispose`, `QuestControllerClientBackend` (`HandoverItem`, `AcceptQuest`, `FinishQuest`), and `HideoutRepresentation` (`UpgradeZone`, `CompleteUpgradeZone`).
- `Patches/TooltipPatch.cs`: Postfix on `GridItemView.ShowTooltip` using `SimpleTooltip.SetText` (preserves the 0.6s hover delay timer).
- `UI/InspectIntelBlock.cs` + `Patches/InspectPanelPatch.cs`: Embeds a native-styled Tarkov dark card (left accent bar, owned/need stat strip, column-aligned quest/hideout rows with status badge + progress bar) as the sibling right after `_itemLabels._description` on `method_1`, hidden in `Close`. With `InspectMergeDescription` (default on) the card copies `_description.text` (EFT only ever sets its text, never toggles it) into a top "物品描述" section and deactivates the native label; every hide path (`HideIfExists`, null intel, option off, `OnDestroy`) re-activates it.
- `UI/InspectWindowSizer.cs`: Narrow-and-tall inspect window. Verified via runtime layout dump: the `InfoWindow` root (same GO as `ItemSpecificationPanel`) has `LayoutElement prefW=670 prefH=500` + `ContentSizeFitter(Preferred)`, so vanilla windows are a fixed 670×500; the description `Scroll Area` (`ScrollRectNoDrag` with `ControlSize=false`, `LayoutElement minH=50 flexH=1`) absorbs the leftover height. We set the root `preferredWidth = InspectWindowWidth` (default 460; the panel's mod-slot `minWidth` still wins for weapons) and `preferredHeight = Screen.height / scale × InspectWindowHeightRatio` (default 0.85, floor 500), once per window only (a `SizedMarker`; later `method_1` refreshes must not undo a user's drag-resize), then `CorrectPositionResolution` next frame. Vanilla drag-resize is a `UI.InfoWindow.StretchWindow` whose `StretchArea`s write that same LayoutElement's preferred size clamped to a serialized `_maxSize`; we raise `_maxSize` (and the areas' copy, if already initialized) to at least the screen size so the taller default stays freely resizable. `InspectIntelBlock` column widths are tuned for the ~395px content width.
- `UI/InspectScrollMerge.cs`: Once per `InfoWindow` (marker component), re-parents the characteristic/compact/mods blocks (`_charPanelContainer`, `_compactPanel`, `_modsPanel`, mastering/durability/examine panels — each climbed to its child-of-main-column branch; branches containing the scroll area, preview or interaction buttons are skipped) to the front of the description `Scroll Area` content, so preview + buttons stay fixed and everything else is one scroll column. Safe because EFT drives them by field reference and the window is destroyed on close. Nested `ScrollRect`s get a `ScrollForwarder` (wheel → outer scroll when the inner can't scroll vertically); outer `scrollSensitivity` is raised to ≥30. Logs `[RaidIntel ScrollMerge]` once per session. Toggle: `InspectMergeIntoScroll`.

## Build & Deploy

```bash
cd E:/SPT/raid-intel-main

# Server (net10.0)
dotnet build -c Release RaidIntel.Server/RaidIntel.Server.csproj
# Deploy server mod folder:
# E:/SPT/SPT_Runtime/user/mods/RaidIntel/ (copy RaidIntel.Server/bin/Release/RaidIntel.Server/*)

# Client (net472)
dotnet build -c Release RaidIntel.Client/RaidIntel.Client.csproj
# Ensure game is closed before overwriting the DLL
cp RaidIntel.Client/bin/Release/RaidIntel.Client.dll E:/SPT/BepInEx/plugins/
```

Verify in logs:
- Client (`E:/SPT/BepInEx/LogOutput.log`):
  - `RaidIntel 0.1.0 loaded`
  - `RaidIntel: patched LifecyclePatch`
  - `RaidIntel: patched TooltipPatch`
  - `RaidIntel: patched InspectPanelPatch`
- Server log: `RaidIntel` mod loaded without errors and `/raidIntel/quests` serves JSON to the client.
