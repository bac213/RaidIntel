using System.Collections.Generic;
using System.Text;
using EFT.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RaidIntel.Client.UI;

/// <summary>
/// Moves the inspect window's characteristics / compact stats / mod-slot blocks into the
/// description "Scroll Area", so everything below the preview and interaction buttons becomes
/// one continuous scrollable reading column.
///
/// Vanilla layout (runtime dump, SPT 4.1.6):
/// <code>
/// Inner └ Contents [VLG]                       ← "main column"
///          ├ preview / buttons ...
///          ├ characteristics, compact stats, mods ...   ← moved
///          └ DescriptionPanel └ DescriptionPanel └ Scroll Area [ScrollRectNoDrag, flexH=1]
///                                                   └ Contents [VLG + CSF]  ← moved blocks go first
///                                                      ├ Description
///                                                      └ RaidIntel card
/// </code>
/// EFT drives these blocks through serialized field references (not hierarchy paths), and the
/// InfoWindow is destroyed on close, so re-parenting once per window is safe and needs no undo.
/// </summary>
internal static class InspectScrollMerge
{
    private const float MinScrollSensitivity = 30f;

    private static bool _loggedOnce;

    private sealed class MergedMarker : MonoBehaviour
    {
    }

    internal static void Apply(ItemSpecificationPanel panel)
    {
        if (panel == null || !RaidIntelPlugin.IsActiveForCurrentContext() || !RaidIntelPlugin.InspectMergeIntoScroll.Value)
        {
            return;
        }

        InfoWindow window = panel.GetComponentInParent<InfoWindow>(true);
        if (window == null || window.GetComponent<MergedMarker>() != null)
        {
            return;
        }

#pragma warning disable CS0618
        Transform desc = panel._itemLabels?._description?.transform;
#pragma warning restore CS0618
        ScrollRect scroll = desc != null ? desc.GetComponentInParent<ScrollRect>(true) : null;
        RectTransform scrollContent = scroll != null ? scroll.content : null;
        if (scrollContent == null || !desc.IsChildOf(scrollContent))
        {
            Log("skip: description scroll area not found");
            window.gameObject.AddComponent<MergedMarker>();
            return;
        }

        List<Transform> blocks = CollectBlocks(panel, scroll.transform, out Transform mainColumn, out string why);
        window.gameObject.AddComponent<MergedMarker>();
        if (blocks == null || blocks.Count == 0)
        {
            Log("skip: " + why);
            return;
        }

        var moved = new StringBuilder(128);
        for (int i = 0; i < blocks.Count; i++)
        {
            Transform block = blocks[i];
            block.SetParent(scrollContent, worldPositionStays: false);
            block.SetSiblingIndex(i);
            AttachScrollForwarders(block, scroll);
            moved.Append(i == 0 ? "" : ", ").Append(block.name);
        }

        if (scroll.scrollSensitivity < MinScrollSensitivity)
        {
            scroll.scrollSensitivity = MinScrollSensitivity;
        }
        scroll.verticalNormalizedPosition = 1f;

        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)window.transform);

        Log($"moved [{moved}] from '{mainColumn.name}' into '{scrollContent.name}' (scrollSensitivity {scroll.scrollSensitivity:0})");
    }

    /// <summary>
    /// For each characteristic / mod field, climbs to the child of the "main column" (the lowest
    /// common ancestor of the scroll area and the characteristic container) that contains it.
    /// Branches that also hold the scroll area, preview, or interaction buttons are never moved.
    /// </summary>
    private static List<Transform> CollectBlocks(ItemSpecificationPanel panel, Transform scrollTransform, out Transform mainColumn, out string why)
    {
        mainColumn = null;
        why = null;

#pragma warning disable CS0618
        Transform[] fields =
        {
            panel._charPanelContainer,
            panel._compactPanel,
            panel._modsPanel,
            panel._masteringPanel != null ? panel._masteringPanel.transform : null,
            panel._charDurability != null ? panel._charDurability.transform : null,
            panel._charMastering != null ? panel._charMastering.transform : null,
            panel._examineCharacteristicPanel != null ? panel._examineCharacteristicPanel.transform : null,
        };
        Transform[] keep =
        {
            panel._interactionButtonsContainer != null ? panel._interactionButtonsContainer.transform : null,
            panel._cameraImage != null ? panel._cameraImage.transform : null,
            panel._itemLabels?._previewPanel != null ? panel._itemLabels._previewPanel.transform : null,
        };
#pragma warning restore CS0618

        Transform anchor = panel._charPanelContainer;
        if (anchor == null)
        {
            why = "_charPanelContainer is null";
            return null;
        }

        for (Transform t = scrollTransform.parent; t != null && t != panel.transform.parent; t = t.parent)
        {
            if (anchor.IsChildOf(t))
            {
                mainColumn = t;
                break;
            }
        }

        if (mainColumn == null)
        {
            why = "no common ancestor of scroll area and characteristics";
            return null;
        }

        var result = new List<Transform>(fields.Length);
        for (int i = 0; i < fields.Length; i++)
        {
            Transform f = fields[i];
            if (f == null || f.IsChildOf(scrollTransform))
            {
                continue;
            }

            Transform branch = ChildOfAncestor(f, mainColumn);
            if (branch == null || result.Contains(branch))
            {
                continue;
            }

            if (scrollTransform.IsChildOf(branch) || ContainsAny(branch, keep))
            {
                why = $"block '{branch.name}' also contains the scroll area / preview / buttons";
                continue;
            }

            result.Add(branch);
        }

        // Preserve the vanilla visual order.
        result.Sort((a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));
        if (result.Count == 0 && why == null)
        {
            why = "no movable characteristic blocks";
        }
        return result;
    }

    private static Transform ChildOfAncestor(Transform t, Transform ancestor)
    {
        while (t != null && t.parent != ancestor)
        {
            t = t.parent;
        }
        return t;
    }

    private static bool ContainsAny(Transform branch, Transform[] items)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] != null && items[i].IsChildOf(branch))
            {
                return true;
            }
        }
        return false;
    }

    private static void AttachScrollForwarders(Transform block, ScrollRect outer)
    {
        ScrollRect[] inner = block.GetComponentsInChildren<ScrollRect>(true);
        for (int i = 0; i < inner.Length; i++)
        {
            if (inner[i] == outer || inner[i].GetComponent<ScrollForwarder>() != null)
            {
                continue;
            }
            ScrollForwarder fwd = inner[i].gameObject.AddComponent<ScrollForwarder>();
            fwd.Inner = inner[i];
            fwd.Outer = outer;
        }
    }

    private static void Log(string message)
    {
        if (!_loggedOnce)
        {
            _loggedOnce = true;
            RaidIntelPlugin.Log?.LogInfo("[RaidIntel ScrollMerge] " + message);
        }
        else
        {
            RaidIntelPlugin.DebugLog("InspectScrollMerge: " + message);
        }
    }

    /// <summary>
    /// A nested ScrollRect swallows wheel events (ExecuteHierarchy stops at the first handler),
    /// so the outer reading column would not scroll while hovering e.g. the mod slots. Runs after
    /// the inner ScrollRect on the same GameObject and forwards the wheel to the outer scroll when
    /// the inner one cannot scroll vertically itself.
    /// </summary>
    private sealed class ScrollForwarder : MonoBehaviour, IScrollHandler
    {
        public ScrollRect Inner;
        public ScrollRect Outer;

        public void OnScroll(PointerEventData eventData)
        {
            if (Outer == null || !Outer.isActiveAndEnabled)
            {
                return;
            }

            if (Inner != null && Inner.isActiveAndEnabled && Inner.vertical && Inner.content != null)
            {
                RectTransform view = Inner.viewport != null ? Inner.viewport : (RectTransform)Inner.transform;
                if (Inner.content.rect.height > view.rect.height + 1f)
                {
                    return; // inner list really scrolls vertically; let it own the wheel
                }
            }

            Outer.OnScroll(eventData);
        }
    }
}
