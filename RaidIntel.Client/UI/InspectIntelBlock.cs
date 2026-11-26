using System;
using System.Collections.Generic;
using System.Text;
using EFT;
using EFT.UI;
using RaidIntel.Client.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntel.Client.UI;

/// <summary>
/// Native-styled Tarkov dark card embedded inside ItemSpecificationPanel in place of the item
/// description label. Optionally absorbs the native description text (the native label is hidden
/// while the card is visible and restored when the card hides), followed by an owned-count stat
/// strip and column-aligned quest / hideout requirement rows with progress bars.
/// All labels share the TMP font asset and material of the native description so Chinese glyphs
/// and Tarkov's Bender font render natively.
/// </summary>
internal sealed class InspectIntelBlock : MonoBehaviour
{
    private const string BlockName = "RaidIntel_InspectBlock";

    // ---- Palette -------------------------------------------------------------------------
    private static readonly Color CardBg = new(0.035f, 0.039f, 0.044f, 0.94f);
    private static readonly Color CardOutline = new(0.26f, 0.27f, 0.25f, 0.75f);
    private static readonly Color Divider = new(0.40f, 0.40f, 0.37f, 0.35f);
    private static readonly Color CellBg = new(1f, 1f, 1f, 0.045f);
    private static readonly Color RowAltBg = new(1f, 1f, 1f, 0.028f);
    private static readonly Color BarTrack = new(1f, 1f, 1f, 0.08f);

    private static readonly Color32 Accent = new(201, 185, 138, 255);     // Tarkov tan
    private static readonly Color32 TextMain = new(218, 215, 204, 255);
    private static readonly Color32 TextDesc = new(189, 186, 170, 255);
    private static readonly Color32 TextMuted = new(124, 133, 139, 255);

    private static readonly Color32 StActiveMet = new(127, 216, 138, 255);
    private static readonly Color32 StActive = new(242, 184, 75, 255);
    private static readonly Color32 StAvailable = new(216, 200, 122, 255);
    private static readonly Color32 StNextLevel = new(108, 200, 232, 255);
    private static readonly Color32 StFavoriteCraft = new(197, 156, 230, 255);
    private static readonly Color32 StFuture = new(138, 148, 158, 255);

    private const string HexMuted = "#7C858B";
    private const string HexFir = "#C9B98A";
    private const string HexDaily = "#8FB4E0";

    // ---- Layout metrics ------------------------------------------------------------------
    // Sized for the narrow inspect window (~395px content width at 460px window width).
    private const float BadgeWidth = 44f;
    private const float NameMinWidth = 110f;
    private const float NamePreferredWidth = 320f;
    private const float BarMinWidth = 36f;
    private const float BarPreferredWidth = 120f;
    private const float CountWidth = 56f;
    private const float FirWidth = 28f;
    private const float RowSpacing = 8f;

    private TMP_Text _nativeDesc;
    private RectTransform _rectTransform;

    private GameObject _descSection;
    private TextMeshProUGUI _descText;
    private GameObject _descDivider;

    private StatCell _statTotal;
    private StatCell _statStash;
    private StatCell _statCarry;
    private StatCell _statNeed;

    private Section _questSection;
    private Section _hideoutSection;
    private Section _craftSection;
    private TextMeshProUGUI _emptyLabel;

    private readonly List<TextMeshProUGUI> _allLabels = new(64);
    private readonly StringBuilder _sb = new(128);

    // ======================================================================================
    // Lifetime
    // ======================================================================================

    internal static InspectIntelBlock GetOrCreate(ItemSpecificationPanel panel)
    {
        TMP_Text desc = GetNativeDescription(panel);
        if (desc == null || desc.transform.parent == null)
        {
            return null;
        }

        Transform parent = desc.transform.parent;
        Transform existing = parent.Find(BlockName);
        InspectIntelBlock block = existing != null ? existing.GetComponent<InspectIntelBlock>() : null;

        if (block == null)
        {
            var go = new GameObject(BlockName, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            block = go.AddComponent<InspectIntelBlock>();
            block.Build(desc);
        }

        block._nativeDesc = desc;
        block.SyncFontFrom(desc);

        int targetSiblingIndex = desc.transform.GetSiblingIndex() + 1;
        if (block.transform.GetSiblingIndex() != targetSiblingIndex)
        {
            block.transform.SetSiblingIndex(targetSiblingIndex);
        }

        return block;
    }

    internal static void HideIfExists(ItemSpecificationPanel panel)
    {
        TMP_Text desc = GetNativeDescription(panel);
        Transform parent = desc != null ? desc.transform.parent : null;
        if (parent == null)
        {
            return;
        }

        bool changed = false;
        if (!desc.gameObject.activeSelf)
        {
            desc.gameObject.SetActive(true);
            changed = true;
        }

        Transform existing = parent.Find(BlockName);
        if (existing != null && existing.gameObject.activeSelf)
        {
            existing.gameObject.SetActive(false);
            changed = true;
        }

        if (changed && parent is RectTransform parentRt)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
        }
    }

    private void OnDestroy()
    {
        if (_nativeDesc != null && !_nativeDesc.gameObject.activeSelf)
        {
            _nativeDesc.gameObject.SetActive(true);
        }
    }

    private static TMP_Text GetNativeDescription(ItemSpecificationPanel panel)
    {
#pragma warning disable CS0618
        return panel?._itemLabels?._description;
#pragma warning restore CS0618
    }

    // ======================================================================================
    // Construction
    // ======================================================================================

    private void Build(TMP_Text template)
    {
        _rectTransform = (RectTransform)transform;

        var bg = gameObject.AddComponent<Image>();
        bg.color = CardBg;
        bg.raycastTarget = false;

        var outline = gameObject.AddComponent<Outline>();
        outline.effectColor = CardOutline;
        outline.effectDistance = new Vector2(1f, -1f);

        AddVlg(gameObject, new RectOffset(14, 12, 10, 10), 8f);

        var fitter = gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var le = gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minHeight = 0f;

        // Left accent bar (outside layout flow).
        GameObject accent = NewGo("Accent", transform);
        var accentImg = accent.AddComponent<Image>();
        accentImg.color = new Color(Accent.r / 255f, Accent.g / 255f, Accent.b / 255f, 0.85f);
        accentImg.raycastTarget = false;
        accent.AddComponent<LayoutElement>().ignoreLayout = true;
        var accentRt = (RectTransform)accent.transform;
        accentRt.anchorMin = new Vector2(0f, 0f);
        accentRt.anchorMax = new Vector2(0f, 1f);
        accentRt.pivot = new Vector2(0f, 0.5f);
        accentRt.sizeDelta = new Vector2(3f, 0f);
        accentRt.anchoredPosition = Vector2.zero;

        // 1. Item description.
        _descSection = NewGo("Description", transform);
        AddVlg(_descSection, new RectOffset(0, 0, 0, 0), 3f);
        NewSectionTitle(_descSection.transform, template, "物品描述");
        float descSize = Mathf.Clamp(template.fontSize, 12f, 15f);
        _descText = NewText(_descSection.transform, "Text", template, descSize, FontStyles.Normal, TextDesc);
        _descText.lineSpacing = 4f;
        _descDivider = NewDivider(transform);

        // 2. Owned / total-need stat strip.
        GameObject stats = NewGo("Stats", transform);
        AddHlg(stats, new RectOffset(0, 0, 0, 0), 6f, TextAnchor.MiddleLeft);
        _statTotal = new StatCell(this, stats.transform, template, "拥有");
        _statStash = new StatCell(this, stats.transform, template, "仓库");
        _statCarry = new StatCell(this, stats.transform, template, "携带");
        NewFlexibleSpacer(stats.transform);
        _statNeed = new StatCell(this, stats.transform, template, "合计需求");

        // 3. Requirement sections.
        _questSection = new Section(this, transform, template, "任务需求");
        _hideoutSection = new Section(this, transform, template, "藏身处升级");
        _craftSection = new Section(this, transform, template, "收藏配方");

        _emptyLabel = NewText(transform, "Empty", template, 13f, FontStyles.Italic, TextMuted);
        _emptyLabel.text = "无未完成任务、藏身处升级或收藏配方需求";
    }

    private void SyncFontFrom(TMP_Text template)
    {
        if (template == null)
        {
            return;
        }

        for (int i = 0; i < _allLabels.Count; i++)
        {
            TextMeshProUGUI target = _allLabels[i];
            if (target == null)
            {
                continue;
            }
            if (template.font != null && target.font != template.font)
            {
                target.font = template.font;
            }
            if (template.fontSharedMaterial != null && target.fontSharedMaterial != template.fontSharedMaterial)
            {
                target.fontSharedMaterial = template.fontSharedMaterial;
            }
        }
    }

    // ======================================================================================
    // Content
    // ======================================================================================

    internal void UpdateContent(ItemIntelResult intel)
    {
        if (intel == null)
        {
            RestoreNativeDescription();
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        UpdateDescription();

        // ---- Requirement rows ----
        int questShown = 0;
        int hideoutShown = 0;
        int craftShown = 0;
        int totalNeed = 0;
        int totalFirNeed = 0;
        int maxToolNeed = 0;
        int maxToolFirNeed = 0;
        OwnedBreakdown owned = intel.Owned;

        bool showFutureQuests = RaidIntelPlugin.InspectShowFutureQuests.Value;
        for (int i = 0; i < intel.QuestRequirements.Count; i++)
        {
            QuestReqEntry q = intel.QuestRequirements[i];
            if (!q.IsActive && !showFutureQuests)
            {
                continue;
            }

            int have = q.HandedInCount + (q.OnlyFoundInRaid ? owned.Fir : owned.Total);
            int need = q.RequiredCount;
            int remaining = Math.Max(0, need - q.HandedInCount);
            totalNeed += remaining;
            if (q.OnlyFoundInRaid)
            {
                totalFirNeed += remaining;
            }

            string badge;
            Color32 color;
            if (q.IsActive)
            {
                badge = "进行中";
                color = have >= need ? StActiveMet : StActive;
            }
            else if (q.IsAvailable)
            {
                badge = "可接取";
                color = StAvailable;
            }
            else
            {
                badge = "后续";
                color = StFuture;
            }

            _questSection.GetRow(questShown).Set(
                questShown,
                badge,
                color,
                FormatQuestName(q),
                q.IsActive || q.IsAvailable,
                have,
                need,
                q.OnlyFoundInRaid);
            questShown++;
        }

        bool showFutureHideout = RaidIntelPlugin.InspectShowFutureHideout.Value;
        for (int i = 0; i < intel.HideoutRequirements.Count; i++)
        {
            HideoutReqEntry h = intel.HideoutRequirements[i];
            if (!h.IsNextLevel && !showFutureHideout)
            {
                continue;
            }

            int have = h.OnlyFoundInRaid ? owned.Fir : owned.Total;
            int need = h.RequiredCount;
            totalNeed += need;
            if (h.OnlyFoundInRaid)
            {
                totalFirNeed += need;
            }

            Color32 color = h.IsNextLevel ? (have >= need ? StActiveMet : StNextLevel) : StFuture;
            string name = $"{h.AreaName}  <color={HexMuted}>Lv.{h.TargetLevel}</color>";

            _hideoutSection.GetRow(hideoutShown).Set(
                hideoutShown,
                h.IsNextLevel ? "下一级" : "后续",
                color,
                name,
                h.IsNextLevel,
                have,
                need,
                h.OnlyFoundInRaid);
            hideoutShown++;
        }

        bool showFavoriteCrafts = RaidIntelPlugin.InspectShowFavoriteCrafts.Value;
        if (showFavoriteCrafts && intel.FavoriteCraftRequirements != null)
        {
            for (int i = 0; i < intel.FavoriteCraftRequirements.Count; i++)
            {
                FavoriteCraftReqEntry c = intel.FavoriteCraftRequirements[i];
                int have = c.OnlyFoundInRaid ? owned.Fir : owned.Total;
                int need = c.RequiredCount;

                if (c.IsTool)
                {
                    if (need > maxToolNeed)
                    {
                        maxToolNeed = need;
                    }
                    if (c.OnlyFoundInRaid && need > maxToolFirNeed)
                    {
                        maxToolFirNeed = need;
                    }
                }
                else
                {
                    totalNeed += need;
                    if (c.OnlyFoundInRaid)
                    {
                        totalFirNeed += need;
                    }
                }

                Color32 color = have >= need ? StActiveMet : StFavoriteCraft;
                _craftSection.GetRow(craftShown).Set(
                    craftShown,
                    c.IsTool ? "工具" : "配方",
                    color,
                    FormatFavoriteCraftName(c),
                    true,
                    have,
                    need,
                    c.OnlyFoundInRaid);
                craftShown++;
            }
        }

        totalNeed += maxToolNeed;
        totalFirNeed += maxToolFirNeed;

        int totalShown = questShown + hideoutShown + craftShown;
        _questSection.Finish(questShown);
        _hideoutSection.Finish(hideoutShown);
        _craftSection.Finish(craftShown);
        _emptyLabel.gameObject.SetActive(totalShown == 0);

        // ---- Stat strip ----
        _statTotal.Set(FormatSplit(owned.Total, owned.Fir), owned.Total > 0 ? TextMain : TextMuted);

        bool showSplit = owned.InRaid || owned.CarryTotal > 0;
        _statStash.SetVisible(showSplit);
        _statCarry.SetVisible(showSplit);
        if (showSplit)
        {
            _statStash.Set(FormatSplit(owned.StashTotal, owned.StashFir), TextMain);
            _statCarry.SetLabel(owned.InRaid ? "携带" : "身上");
            _statCarry.Set(FormatSplit(owned.CarryTotal, owned.CarryFir), TextMain);
        }

        _statNeed.SetVisible(totalShown > 0);
        if (totalShown > 0)
        {
            string needText = totalFirNeed > 0
                ? $"{totalNeed}<size=70%><color={HexFir}>  FIR {totalFirNeed}</color></size>"
                : totalNeed.ToString();
            Color32 needColor = owned.Total >= totalNeed && owned.Fir >= totalFirNeed ? StActiveMet : StActive;
            _statNeed.Set(needText, needColor);
        }

        // ---- Layout ----
        if (_rectTransform != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rectTransform);
        }
        if (transform.parent is RectTransform parentRt)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
        }
    }

    private void UpdateDescription()
    {
        string text = _nativeDesc != null ? _nativeDesc.text : null;
        bool merge = RaidIntelPlugin.InspectMergeDescription.Value
                     && _nativeDesc != null
                     && !string.IsNullOrWhiteSpace(text);

        if (merge)
        {
            _descText.text = text;
            _descSection.SetActive(true);
            _descDivider.SetActive(true);
            if (_nativeDesc.gameObject.activeSelf)
            {
                _nativeDesc.gameObject.SetActive(false);
            }
        }
        else
        {
            _descSection.SetActive(false);
            _descDivider.SetActive(false);
            RestoreNativeDescription();
        }
    }

    private void RestoreNativeDescription()
    {
        if (_nativeDesc != null && !_nativeDesc.gameObject.activeSelf)
        {
            _nativeDesc.gameObject.SetActive(true);
        }
    }

    private string FormatQuestName(QuestReqEntry q)
    {
        _sb.Clear();

        string name = q.InspectName ?? string.Empty;
        string kindLabel = null;
        if (string.Equals(q.RepeatableKind, "Daily", StringComparison.OrdinalIgnoreCase))
        {
            kindLabel = "日常";
        }
        else if (string.Equals(q.RepeatableKind, "Weekly", StringComparison.OrdinalIgnoreCase))
        {
            kindLabel = "周常";
        }

        if (kindLabel != null)
        {
            string prefix = $"[{kindLabel}] ";
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = name.Substring(prefix.Length);
            }
            _sb.Append("<color=").Append(HexDaily).Append(">").Append(kindLabel).Append("</color> ");
        }

        _sb.Append(name);

        string trader = ResolveTraderName(q.TraderId);
        bool hasMeta = !string.IsNullOrEmpty(trader) || q.IsPlant || q.HandedInCount > 0;
        if (hasMeta)
        {
            _sb.Append("<size=85%><color=").Append(HexMuted).Append(">");
            if (!string.IsNullOrEmpty(trader))
            {
                _sb.Append("  · ").Append(trader);
            }
            if (q.IsPlant)
            {
                _sb.Append("  · 埋放");
            }
            if (q.HandedInCount > 0)
            {
                _sb.Append(q.IsPlant ? "  · 已埋 " : "  · 已交 ").Append(q.HandedInCount);
            }
            _sb.Append("</color></size>");
        }

        return _sb.ToString();
    }

    private string FormatFavoriteCraftName(FavoriteCraftReqEntry c)
    {
        _sb.Clear();
        _sb.Append(c.ProductName ?? string.Empty);
        if (c.ProductCount > 1)
        {
            _sb.Append(" ×").Append(c.ProductCount);
        }

        if (!string.IsNullOrEmpty(c.AreaName))
        {
            _sb.Append("<size=85%><color=").Append(HexMuted).Append(">  · ")
               .Append(c.AreaName)
               .Append(" Lv.")
               .Append(c.AreaLevel)
               .Append("</color></size>");
        }

        return _sb.ToString();
    }

    private static string ResolveTraderName(string traderId)
    {
        if (string.IsNullOrEmpty(traderId))
        {
            return null;
        }

        string key = traderId + " Nickname";
        string localized = key.Localized();
        return string.IsNullOrEmpty(localized) || string.Equals(localized, key, StringComparison.Ordinal)
            ? null
            : localized;
    }

    private static string FormatSplit(int total, int fir)
    {
        return fir > 0
            ? $"{total}<size=70%><color={HexFir}>  FIR {fir}</color></size>"
            : total.ToString();
    }

    // ======================================================================================
    // UI building helpers
    // ======================================================================================

    private static GameObject NewGo(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);
        return go;
    }

    private static VerticalLayoutGroup AddVlg(GameObject go, RectOffset padding, float spacing)
    {
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = padding;
        vlg.spacing = spacing;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        return vlg;
    }

    private static HorizontalLayoutGroup AddHlg(GameObject go, RectOffset padding, float spacing, TextAnchor alignment)
    {
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = padding;
        hlg.spacing = spacing;
        hlg.childAlignment = alignment;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        return hlg;
    }

    private static LayoutElement SetLayout(
        GameObject go,
        float minWidth = -1f,
        float preferredWidth = -1f,
        float flexibleWidth = -1f,
        float minHeight = -1f,
        float preferredHeight = -1f)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null)
        {
            le = go.AddComponent<LayoutElement>();
        }
        le.minWidth = minWidth;
        le.preferredWidth = preferredWidth;
        le.flexibleWidth = flexibleWidth;
        le.minHeight = minHeight;
        le.preferredHeight = preferredHeight;
        return le;
    }

    private static Image AddImage(GameObject go, Color color)
    {
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static void NewFlexibleSpacer(Transform parent)
    {
        SetLayout(NewGo("Spacer", parent), minWidth: 0f, flexibleWidth: 1f);
    }

    private static GameObject NewDivider(Transform parent)
    {
        GameObject go = NewGo("Divider", parent);
        AddImage(go, Divider);
        SetLayout(go, minHeight: 1f, preferredHeight: 1f, flexibleWidth: 1f);
        return go;
    }

    private TextMeshProUGUI NewText(
        Transform parent,
        string name,
        TMP_Text template,
        float fontSize,
        FontStyles style,
        Color32 color,
        TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft,
        bool wrap = true)
    {
        GameObject go = NewGo(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (template.font != null)
        {
            tmp.font = template.font;
        }
        if (template.fontSharedMaterial != null)
        {
            tmp.fontSharedMaterial = template.fontSharedMaterial;
        }

        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.enableWordWrapping = wrap;
        tmp.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        tmp.richText = true;
        tmp.raycastTarget = false;
        _allLabels.Add(tmp);
        return tmp;
    }

    private TextMeshProUGUI NewSectionTitle(Transform parent, TMP_Text template, string title)
    {
        TextMeshProUGUI tmp = NewText(parent, "Title", template, 12f, FontStyles.Bold, Accent, wrap: false);
        tmp.characterSpacing = 2f;
        tmp.text = title;
        return tmp;
    }

    // ======================================================================================
    // Sub-widgets
    // ======================================================================================

    private sealed class StatCell
    {
        private readonly GameObject _root;
        private readonly TextMeshProUGUI _label;
        private readonly TextMeshProUGUI _value;

        internal StatCell(InspectIntelBlock owner, Transform parent, TMP_Text template, string label)
        {
            _root = NewGo("Stat", parent);
            AddImage(_root, CellBg);
            var vlg = AddVlg(_root, new RectOffset(8, 10, 3, 4), 0f);
            vlg.childForceExpandWidth = false;
            SetLayout(_root, minWidth: 56f);

            _label = owner.NewText(_root.transform, "Label", template, 11f, FontStyles.Normal, TextMuted, wrap: false);
            _label.text = label;
            _value = owner.NewText(_root.transform, "Value", template, 16f, FontStyles.Bold, TextMain, wrap: false);
        }

        internal void SetLabel(string label)
        {
            if (_label.text != label)
            {
                _label.text = label;
            }
        }

        internal void Set(string value, Color32 color)
        {
            _value.text = value;
            _value.color = color;
        }

        internal void SetVisible(bool visible)
        {
            if (_root.activeSelf != visible)
            {
                _root.SetActive(visible);
            }
        }
    }

    private sealed class Section
    {
        private readonly InspectIntelBlock _owner;
        private readonly TMP_Text _template;
        private readonly GameObject _root;
        private readonly TextMeshProUGUI _count;
        private readonly Transform _rowsParent;
        private readonly List<Row> _rows = new(8);

        internal Section(InspectIntelBlock owner, Transform parent, TMP_Text template, string title)
        {
            _owner = owner;
            _template = template;

            _root = NewGo("Section", parent);
            AddVlg(_root, new RectOffset(0, 0, 0, 0), 3f);

            GameObject header = NewGo("Header", _root.transform);
            AddHlg(header, new RectOffset(0, 0, 0, 2), 8f, TextAnchor.LowerLeft);
            owner.NewSectionTitle(header.transform, template, title);
            _count = owner.NewText(header.transform, "Count", template, 11f, FontStyles.Normal, TextMuted, wrap: false);
            NewFlexibleSpacer(header.transform);

            GameObject rows = NewGo("Rows", _root.transform);
            AddVlg(rows, new RectOffset(0, 0, 0, 0), 0f);
            _rowsParent = rows.transform;
        }

        internal Row GetRow(int index)
        {
            while (_rows.Count <= index)
            {
                _rows.Add(new Row(_owner, _rowsParent, _template));
            }
            return _rows[index];
        }

        internal void Finish(int shown)
        {
            for (int i = shown; i < _rows.Count; i++)
            {
                _rows[i].SetVisible(false);
            }

            _root.SetActive(shown > 0);
            if (shown > 0)
            {
                _count.text = $"{shown} 项";
            }
        }
    }

    private sealed class Row
    {
        private readonly GameObject _root;
        private readonly Image _bg;
        private readonly Image _badgeBg;
        private readonly TextMeshProUGUI _badgeText;
        private readonly TextMeshProUGUI _name;
        private readonly RectTransform _barFill;
        private readonly Image _barFillImg;
        private readonly TextMeshProUGUI _count;
        private readonly TextMeshProUGUI _fir;

        internal Row(InspectIntelBlock owner, Transform parent, TMP_Text template)
        {
            _root = NewGo("Row", parent);
            _bg = AddImage(_root, Color.clear);
            AddHlg(_root, new RectOffset(6, 6, 2, 2), RowSpacing, TextAnchor.MiddleLeft);
            SetLayout(_root, minHeight: 22f);

            // Status badge.
            GameObject badge = NewGo("Badge", _root.transform);
            _badgeBg = AddImage(badge, Color.clear);
            SetLayout(badge, minWidth: BadgeWidth, preferredWidth: BadgeWidth, preferredHeight: 17f);
            _badgeText = owner.NewText(badge.transform, "Text", template, 11f, FontStyles.Bold, TextMain,
                TextAlignmentOptions.Center, wrap: false);
            var badgeTextRt = (RectTransform)_badgeText.transform;
            badgeTextRt.anchorMin = Vector2.zero;
            badgeTextRt.anchorMax = Vector2.one;
            badgeTextRt.offsetMin = Vector2.zero;
            badgeTextRt.offsetMax = Vector2.zero;

            // Name + meta.
            _name = owner.NewText(_root.transform, "Name", template, 13f, FontStyles.Normal, TextMain,
                TextAlignmentOptions.MidlineLeft, wrap: false);
            SetLayout(_name.gameObject, minWidth: NameMinWidth, preferredWidth: NamePreferredWidth, flexibleWidth: 0f);

            // Progress bar.
            GameObject bar = NewGo("Bar", _root.transform);
            AddImage(bar, BarTrack);
            SetLayout(bar, minWidth: BarMinWidth, preferredWidth: BarPreferredWidth, preferredHeight: 4f);
            GameObject fill = NewGo("Fill", bar.transform);
            _barFillImg = AddImage(fill, Color.white);
            _barFill = (RectTransform)fill.transform;
            _barFill.anchorMin = Vector2.zero;
            _barFill.anchorMax = new Vector2(0f, 1f);
            _barFill.offsetMin = Vector2.zero;
            _barFill.offsetMax = Vector2.zero;

            // Count.
            _count = owner.NewText(_root.transform, "Count", template, 13f, FontStyles.Bold, TextMain,
                TextAlignmentOptions.MidlineRight, wrap: false);
            SetLayout(_count.gameObject, minWidth: CountWidth, preferredWidth: CountWidth);

            // FIR marker.
            _fir = owner.NewText(_root.transform, "Fir", template, 10f, FontStyles.Bold, Accent,
                TextAlignmentOptions.MidlineLeft, wrap: false);
            SetLayout(_fir.gameObject, minWidth: FirWidth, preferredWidth: FirWidth);

            NewFlexibleSpacer(_root.transform);
        }

        internal void Set(int index, string badge, Color32 color, string name, bool emphasized, int have, int need, bool fir)
        {
            SetVisible(true);
            _bg.color = index % 2 == 1 ? RowAltBg : Color.clear;

            _badgeText.text = badge;
            _badgeText.color = color;
            _badgeBg.color = new Color(color.r / 255f, color.g / 255f, color.b / 255f, 0.16f);

            _name.text = name;
            _name.color = emphasized ? TextMain : StFuture;

            float fraction = need > 0 ? Mathf.Clamp01((float)have / need) : 1f;
            _barFill.anchorMax = new Vector2(fraction, 1f);
            _barFillImg.color = new Color(color.r / 255f, color.g / 255f, color.b / 255f, emphasized ? 0.9f : 0.55f);

            _count.text = $"{have}<color={HexMuted}>/{need}</color>";
            _count.color = color;

            _fir.text = fir ? "FIR" : string.Empty;
        }

        internal void SetVisible(bool visible)
        {
            if (_root.activeSelf != visible)
            {
                _root.SetActive(visible);
            }
        }
    }
}
