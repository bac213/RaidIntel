using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;

namespace RaidIntel.Client.Core;

/// <summary>
/// Tracks PMC stash and quest-stash item counts (total and FIR) keyed by TemplateId.
/// In menu/hideout, subscribes to InventoryController mutations and lazily rebuilds at most
/// once per mutation for O(1) hover queries. Before entering a raid, freezes an immutable
/// snapshot before EFT nulls out Profile.Inventory.Stash.
/// </summary>
internal sealed class StashTracker
{
    internal static readonly StashTracker Instance = new();

    private readonly Dictionary<string, (int total, int fir)> _counts = new(512, StringComparer.Ordinal);
    private readonly Dictionary<string, (int total, int fir)> _menuEquipCounts = new(128, StringComparer.Ordinal);
    private readonly List<Item> _scratchItems = new(2048);

    private InventoryController _menuController;
    private Profile _pmcProfile;
    private bool _dirty = true;
    private bool _frozenForRaid;

    internal void AttachMenuController(InventoryController controller, Profile pmcProfile)
    {
        if (!ReferenceEquals(_menuController, controller))
        {
            UnsubscribeMenuController();
            _menuController = controller;
            if (_menuController != null)
            {
                _menuController.AddItemEvent += OnAddItem;
                _menuController.RemoveItemEvent += OnRemoveItem;
                _menuController.RefreshItemEvent += OnRefreshItem;
            }
        }

        _pmcProfile = pmcProfile;
        _frozenForRaid = false;
        _dirty = true;

        RaidIntelPlugin.DebugLog($"StashTracker attached to menu controller (profile={pmcProfile?.Id})");
    }

    internal void FreezeSnapshotFromProfile(Profile pmcProfile)
    {
        if (pmcProfile != null)
        {
            _pmcProfile = pmcProfile;
        }

        if (_pmcProfile?.Inventory != null)
        {
            RebuildFromProfile(_pmcProfile);
        }

        _menuEquipCounts.Clear();
        _frozenForRaid = true;
        _dirty = false;
        RaidIntelPlugin.DebugLog($"StashTracker froze snapshot for raid ({_counts.Count} unique templates)");
    }

    internal (int total, int fir) GetCount(string templateId)
    {
        if (string.IsNullOrEmpty(templateId))
        {
            return (0, 0);
        }

        EnsureMenuCountsReady();
        return _counts.TryGetValue(templateId, out var counts) ? counts : (0, 0);
    }

    internal (int total, int fir) GetMenuEquipmentCount(string templateId)
    {
        if (_frozenForRaid || string.IsNullOrEmpty(templateId))
        {
            return (0, 0);
        }

        EnsureMenuCountsReady();
        return _menuEquipCounts.TryGetValue(templateId, out var counts) ? counts : (0, 0);
    }

    internal void MarkDirty()
    {
        if (!_frozenForRaid)
        {
            _dirty = true;
        }
    }

    private void EnsureMenuCountsReady()
    {
        if (!_frozenForRaid && _dirty && _pmcProfile?.Inventory?.Stash != null)
        {
            RebuildFromProfile(_pmcProfile);
            _dirty = false;
        }
    }

    private void RebuildFromProfile(Profile profile)
    {
        _counts.Clear();
        _menuEquipCounts.Clear();
        if (profile?.Inventory == null)
        {
            return;
        }

        InventoryCountHelper.AccumulateContainer(profile.Inventory.Stash, _scratchItems, _counts);
        InventoryCountHelper.AccumulateContainer(profile.Inventory.QuestStashItems, _scratchItems, _counts);
        InventoryCountHelper.AccumulateContainer(profile.Inventory.Equipment, _scratchItems, _menuEquipCounts);
    }

    private void OnAddItem(AddItemEventArgs args)
    {
        if (args.Status == CommandStatus.Succeed)
        {
            _dirty = true;
        }
    }

    private void OnRemoveItem(RemoveItemEventArgs args)
    {
        if (args.Status == CommandStatus.Succeed)
        {
            _dirty = true;
        }
    }

    private void OnRefreshItem(RefreshItemEventArgs args)
    {
        _dirty = true;
    }

    private void UnsubscribeMenuController()
    {
        if (_menuController == null)
        {
            return;
        }

        _menuController.AddItemEvent -= OnAddItem;
        _menuController.RemoveItemEvent -= OnRemoveItem;
        _menuController.RefreshItemEvent -= OnRefreshItem;
        _menuController = null;
    }
}
