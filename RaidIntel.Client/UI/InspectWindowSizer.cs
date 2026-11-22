using System.Collections;
using System.Collections.Generic;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using StretchArea = EFT.UI.StretchArea;
using StretchWindow = global::UI.InfoWindow.StretchWindow;

namespace RaidIntel.Client.UI;

/// <summary>
/// Opens the item inspect window (<see cref="InfoWindow"/>) narrow and tall by default, while
/// keeping vanilla free resizing.
///
/// Vanilla sizing (verified from a runtime layout dump, SPT 4.1.6): the window root — the same
/// GameObject as <see cref="ItemSpecificationPanel"/> — carries
/// <c>LayoutElement prefW=670 prefH=500</c> plus <c>ContentSizeFitter(Preferred, Preferred)</c>,
/// so every inspect window opens at 670×500. The description <c>Scroll Area</c> has
/// <c>LayoutElement flexH=1</c> and absorbs whatever height is left over.
///
/// Vanilla resizing: a <see cref="StretchWindow"/> child initializes its edge/corner
/// <see cref="StretchArea"/>s in <c>Start</c> with the same LayoutElement and a serialized
/// <c>_maxSize</c>; dragging writes that LayoutElement's preferred size, clamped to
/// <c>[0, _maxSize]</c>.
///
/// So we (1) set the default preferred size exactly once per window — later refreshes of the panel
/// (e.g. after examining) must not undo a user's resize — and (2) raise the drag limit to at least
/// the screen size, so the taller default does not exceed it (otherwise the first drag would snap
/// the window down to the prefab limit). The panel's own mod-slot driven
/// <c>LayoutElement.minWidth</c> still wins over our preferred width.
/// </summary>
internal static class InspectWindowSizer
{
    private static readonly AccessTools.FieldRef<StretchArea, Vector2> AreaMaxSize =
        AccessTools.FieldRefAccess<StretchArea, Vector2>("_maxSize");

    private static bool _loggedOnce;

    private sealed class SizedMarker : MonoBehaviour
    {
    }

    internal static void Apply(ItemSpecificationPanel panel)
    {
        InfoWindow window = panel != null ? panel.GetComponentInParent<InfoWindow>(true) : null;
        RectTransform windowRt = window != null ? window.transform as RectTransform : null;
        if (windowRt == null || windowRt.GetComponent<SizedMarker>() != null)
        {
            return; // not a floating window (e.g. handbook preview), or already sized once
        }

        if (!RaidIntelPlugin.IsActiveForCurrentContext() || !RaidIntelPlugin.InspectResizeWindow.Value)
        {
            return; // leave vanilla 670x500; re-evaluated on the next refresh of this window
        }

        windowRt.gameObject.AddComponent<SizedMarker>();

#pragma warning disable CS0618
        StretchWindow stretch = windowRt.GetComponentInChildren<StretchWindow>(true);
        LayoutElement le = stretch != null && stretch._stretchableObject != null
            ? stretch._stretchableObject
            : windowRt.GetComponent<LayoutElement>();
#pragma warning restore CS0618
        if (le == null)
        {
            RaidIntelPlugin.DebugLog("InspectWindowSizer: window has no LayoutElement");
            return;
        }

        float scale = windowRt.lossyScale.y > 0.0001f ? windowRt.lossyScale.y : 1f;
        var screen = new Vector2(Mathf.Floor(Screen.width / scale), Mathf.Floor(Screen.height / scale));

        float origW = le.preferredWidth;
        float origH = le.preferredHeight;
        float width = RaidIntelPlugin.InspectWindowWidth.Value;
        float height = Mathf.Max(origH, Mathf.Floor(screen.y * RaidIntelPlugin.InspectWindowHeightRatio.Value));

        string limit = RaiseStretchLimit(stretch, screen);

        le.preferredWidth = width;
        le.preferredHeight = height;
        LayoutRebuilder.ForceRebuildLayoutImmediate(windowRt);

        if (!_loggedOnce)
        {
            _loggedOnce = true;
            RaidIntelPlugin.Log?.LogInfo(
                $"[RaidIntel] inspect window {origW:0}x{origH:0} -> preferred {width:0}x{height:0}, actual {windowRt.rect.width:0}x{windowRt.rect.height:0} (screen {screen.x:0}x{screen.y:0}); resize limit {limit}");
        }

        // OpenWindow positions a freshly created window after Show (centered, or offset from the
        // previous window) — keep the taller window on screen once that has happened.
        if (panel.isActiveAndEnabled)
        {
            panel.StartCoroutine(CorrectPositionNextFrame(windowRt));
        }
        else
        {
            windowRt.CorrectPositionResolution();
        }
    }

    /// <summary>
    /// Raises the drag-resize limit to at least the screen size. <see cref="StretchWindow"/> hands
    /// <c>_maxSize</c> to its areas in <c>Start</c> (normally after this runs, since Show happens in
    /// the instantiating frame); already-initialized areas are patched directly as well.
    /// </summary>
    private static string RaiseStretchLimit(StretchWindow stretch, Vector2 screen)
    {
        if (stretch == null)
        {
            return "n/a (no StretchWindow)";
        }

#pragma warning disable CS0618
        Vector2 before = stretch._maxSize;
        Vector2 after = Vector2.Max(before, screen);
        stretch._maxSize = after;

        Dictionary<StretchArea.EStretchAreaType, StretchArea> areas = stretch._stretchAreas;
#pragma warning restore CS0618
        if (areas != null)
        {
            foreach (StretchArea area in areas.Values)
            {
                if (area != null)
                {
                    ref Vector2 max = ref AreaMaxSize(area);
                    max = Vector2.Max(max, after);
                }
            }
        }

        return $"{before.x:0}x{before.y:0} -> {after.x:0}x{after.y:0}";
    }

    private static IEnumerator CorrectPositionNextFrame(RectTransform windowRt)
    {
        yield return null;
        if (windowRt != null)
        {
            windowRt.CorrectPositionResolution();
        }
    }
}
