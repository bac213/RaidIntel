using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;

namespace RaidIntel.Client.Core;

/// <summary>
/// Tracks items carried on GameWorld.MainPlayer (works for both PMC and Scav raids).
/// Subscribes to MainPlayer.InventoryController item events and rebuilds lazily on query.
/// </summary>
internal sealed class RaidCarryTracker
{
    internal static readonly RaidCarryTracker Instance = new();

    private readonly Dictionary<string, (int total, int fir)> _counts = new(128, StringComparer.Ordinal);
    private readonly List<Item> _scratchItems = new(512);

    private Player _mainPlayer;
    private InventoryController _raidController;
    private bool _dirty;

    internal bool IsInRaid { get; private set; }

    internal void OnRaidStart(Player mainPlayer)
    {
        OnRaidEnd();

        if (mainPlayer == null)
        {
            return;
        }

        _mainPlayer = mainPlayer;
        _raidController = mainPlayer.InventoryController;
        if (_raidController != null)
        {
            _raidController.AddItemEvent += OnAddItem;
            _raidController.RemoveItemEvent += OnRemoveItem;
            _raidController.RefreshItemEvent += OnRefreshItem;
        }

        IsInRaid = true;
        _dirty = true;
        RaidIntelPlugin.DebugLog($"RaidCarryTracker started (side={mainPlayer.Profile?.Side})");
    }

    internal void OnRaidEnd()
    {
        if (_raidController != null)
        {
            _raidController.AddItemEvent -= OnAddItem;
            _raidController.RemoveItemEvent -= OnRemoveItem;
            _raidController.RefreshItemEvent -= OnRefreshItem;
            _raidController = null;
        }

        _mainPlayer = null;
        _counts.Clear();
        _scratchItems.Clear();
        IsInRaid = false;
        _dirty = false;
    }

    internal (int total, int fir) GetCount(string templateId)
    {
        if (!IsInRaid || _mainPlayer == null || string.IsNullOrEmpty(templateId))
        {
            return (0, 0);
        }

        if (_dirty)
        {
            _counts.Clear();
            InventoryCountHelper.AccumulateContainer(
                _mainPlayer?.Profile?.Inventory?.Equipment,
                _scratchItems,
                _counts);
            _dirty = false;
        }

        return _counts.TryGetValue(templateId, out var counts) ? counts : (0, 0);
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
}
