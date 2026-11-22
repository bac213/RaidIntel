using System.Collections;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntel.Client.UI;

/// <summary>
/// Gives the item inspect window (<see cref="InfoWindow"/>) a fixed narrow-and-tall shape.
///
/// Vanilla sizing (verified from a runtime layout dump, SPT 4.1.6): the window root — the same
/// GameObject as <see cref="ItemSpecificationPanel"/> — carries
/// <c>LayoutElement prefW=670 prefH=500</c> plus <c>ContentSizeFitter(Preferred, Preferred)</c>,
/// so every inspect window is 670×500. The description <c>Scroll Area</c> has
/// <c>LayoutElement flexH=1</c> and absorbs whatever height is left over.
///
/// We override the root preferred width/height. The panel's own mod-slot driven
/// <c>LayoutElement.minWidth</c> still wins over our preferred width (LayoutUtility uses
/// max(min, preferred)), so weapons with many slots keep a usable width. Extra height flows
/// into the description scroll area.
/// </summary>
internal static class InspectWindowSizer
{
    private static bool _loggedOnce;

    private sealed class SizerState : MonoBehaviour
    {
        public float OriginalPreferredWidth;
        public float OriginalPreferredHeight;
    }

    internal static void Apply(ItemSpecificationPanel panel)
    {
        InfoWindow window = panel != null ? panel.GetComponentInParent<InfoWindow>(true) : null;
        RectTransform windowRt = window != null ? window.transform as RectTransform : null;
        if (windowRt == null)
        {
            return; // e.g. handbook preview: not a floating window.
        }

        LayoutElement le = windowRt.GetComponent<LayoutElement>();
        if (le == null)
        {
            RaidIntelPlugin.DebugLog("InspectWindowSizer: window root has no LayoutElement");
            return;
        }

        SizerState state = windowRt.GetComponent<SizerState>();
        if (state == null)
        {
            state = windowRt.gameObject.AddComponent<SizerState>();
            state.OriginalPreferredWidth = le.preferredWidth;
            state.OriginalPreferredHeight = le.preferredHeight;
        }

        float width;
        float height;
        if (RaidIntelPlugin.IsActiveForCurrentContext() && RaidIntelPlugin.InspectResizeWindow.Value)
        {
            float scale = windowRt.lossyScale.y > 0.0001f ? windowRt.lossyScale.y : 1f;
            width = RaidIntelPlugin.InspectWindowWidth.Value;
            height = Mathf.Max(state.OriginalPreferredHeight,
                Mathf.Floor(Screen.height / scale * RaidIntelPlugin.InspectWindowHeightRatio.Value));
        }
        else
        {
            width = state.OriginalPreferredWidth;
            height = state.OriginalPreferredHeight;
        }

        if (Mathf.Approximately(le.preferredWidth, width) && Mathf.Approximately(le.preferredHeight, height))
        {
            return;
        }

        le.preferredWidth = width;
        le.preferredHeight = height;
        LayoutRebuilder.ForceRebuildLayoutImmediate(windowRt);

        if (!_loggedOnce)
        {
            _loggedOnce = true;
            RaidIntelPlugin.Log?.LogInfo(
                $"[RaidIntel] inspect window {state.OriginalPreferredWidth:0}x{state.OriginalPreferredHeight:0} -> preferred {width:0}x{height:0}, actual {windowRt.rect.width:0}x{windowRt.rect.height:0} (screen {Screen.width}x{Screen.height})");
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

    private static IEnumerator CorrectPositionNextFrame(RectTransform windowRt)
    {
        yield return null;
        if (windowRt != null)
        {
            windowRt.CorrectPositionResolution();
        }
    }
}
