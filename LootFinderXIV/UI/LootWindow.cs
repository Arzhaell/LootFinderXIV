using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using LootFinderXIV.Data;
using LootFinderXIV.Localization;
using LootFinderXIV.Services;
using ContextMenu = KamiToolKit.ContextMenu.ContextMenu;

namespace LootFinderXIV.UI;

/// <summary>
/// Fiche de la mission façon base de données : loots rares, mémoquartz et gils, puis un bloc par coffre.
/// </summary>
public sealed class LootWindow : NativeAddon
{
    public const string DefaultTitle = "LootFinderXIV";
    public static readonly Vector2 DefaultSize = new(460.0f, 620.0f);

    private const float HeaderHeight = 24.0f;
    private const uint GilIcon = 65002;

    private static readonly Vector4 ObtainedColor = new(0.55f, 0.95f, 0.55f, 1.0f);
    private static readonly Vector4 MissingColor = new(1.0f, 0.55f, 0.45f, 1.0f);

    private readonly HashSet<string> collapsedSections = [];
    private readonly ContextMenu contextMenu = new();

    private TextNode? infoNode;
    private CheckboxNode? hideObtainedNode;
    private ScrollingNode<VerticalListNode>? body;
    private TextNode? emptyNode;
    private uint shownDutyId;
    private bool refreshQueued;
    private DutySheet? pinnedDuty;

    public required DutyWatcher Watcher { get; init; }
    public required OwnershipService Ownership { get; init; }
    public required Configuration Config { get; init; }

    /// <summary>Appelé quand la fenêtre se ferme (par le joueur ou le plugin).</summary>
    public Action? OnClosed { get; set; }

    /// <summary>Mission affichée : celle choisie dans la liste, sinon la mission active.</summary>
    public DutySheet? CurrentDuty => pinnedDuty ?? Watcher.ActiveDuty;

    /// <summary>Affiche une mission choisie dans la liste (jusqu'au prochain changement de mission active).</summary>
    public void ShowDuty(DutySheet duty)
    {
        pinnedDuty = duty;
        if (IsOpen)
            QueueRefresh();
        else
            Open();
    }

    /// <summary>Revient à la mission active (outil de mission ou mission en cours).</summary>
    public void ClearPinnedDuty() => pinnedDuty = null;

    /// <summary>Reconstruit la fiche à la frame suivante (plusieurs demandes sont regroupées).</summary>
    public void QueueRefresh()
    {
        if (refreshQueued || !IsOpen)
            return;
        refreshQueued = true;
        Plugin.Framework.RunOnTick(() =>
        {
            refreshQueued = false;
            Rebuild();
        });
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        base.OnSetup(addon, atkValueSpan);

        infoNode = new TextNode
        {
            Position = ContentStartPosition,
            Size = new Vector2(ContentSize.X - 190.0f, HeaderHeight),
            AlignmentType = AlignmentType.Left,
            FontSize = 12,
            TextColor = LootRowNode.DimTextColor,
        };
        infoNode.AttachNode(this);

        hideObtainedNode = new CheckboxNode
        {
            Position = ContentStartPosition + new Vector2(ContentSize.X - 185.0f, 0.0f),
            Size = new Vector2(185.0f, HeaderHeight),
            String = Strings.HideObtained,
            IsChecked = Config.HideObtained,
            OnClick = isChecked =>
            {
                Config.HideObtained = isChecked;
                Config.Save();
                QueueRefresh();
            },
        };
        hideObtainedNode.AttachNode(this);

        body = new ScrollingNode<VerticalListNode>
        {
            Position = ContentStartPosition + new Vector2(0.0f, HeaderHeight + 4.0f),
            Size = ContentSize - new Vector2(0.0f, HeaderHeight + 4.0f),
            AutoHideScrollBar = true,
            ScrollSpeed = 30,
        };
        body.ContentNode.FitWidth = true;
        body.ContentNode.FitContents = true;
        body.ContentNode.ItemSpacing = 6.0f;
        body.AttachNode(this);

        emptyNode = new TextNode
        {
            Position = body.Position,
            Size = new Vector2(ContentSize.X, 120.0f),
            AlignmentType = AlignmentType.Center,
            TextFlags = TextFlags.MultiLine | TextFlags.WordWrap,
            FontSize = 14,
            TextColor = LootRowNode.DimTextColor,
            IsVisible = false,
        };
        emptyNode.AttachNode(this);

        shownDutyId = 0;
        Rebuild();
    }

    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        base.OnFinalize(addon);
        infoNode = null;
        hideObtainedNode = null;
        body = null;
        emptyNode = null;
        pinnedDuty = null;
        OnClosed?.Invoke();
    }

    private void Rebuild()
    {
        if (body is null || infoNode is null || emptyNode is null || hideObtainedNode is null)
            return;

        // La langue peut avoir changé depuis l'ouverture.
        hideObtainedNode.String = Strings.HideObtained;
        hideObtainedNode.IsChecked = Config.HideObtained;

        var list = body.ContentNode;
        list.Clear();

        if (CurrentDuty is not { } duty)
        {
            shownDutyId = 0;
            WindowNode?.SetTitle(DefaultTitle);
            infoNode.String = string.Empty;
            emptyNode.String = Strings.NoDutySelected;
            emptyNode.IsVisible = true;
            body.RecalculateSizes();
            return;
        }

        emptyNode.IsVisible = false;
        WindowNode?.SetTitle(duty.Name, DefaultTitle);
        if (duty.ContentFinderConditionId != shownDutyId)
        {
            shownDutyId = duty.ContentFinderConditionId;
            body.ScrollToStart();
        }

        infoNode.String = Strings.DutyInfo(duty);

        list.AddNode(BuildRareSection(duty));
        if (BuildRewardSection(duty) is { } rewards)
            list.AddNode(rewards);
        foreach (var chest in duty.Chests)
        {
            if (BuildChestSection(chest) is { } section)
                list.AddNode(section);
        }

        Relayout();
    }

    private void Relayout()
    {
        if (body is null)
            return;
        body.ContentNode.RecalculateLayout();
        body.RecalculateSizes();
    }

    private CollapsingHeaderNode CreateSection(string key, string title)
    {
        var section = new CollapsingHeaderNode
        {
            String = title,
            FitWidth = true,
            ItemSpacing = 2.0f,
            FirstItemSpacing = 2.0f,
            IsCollapsed = collapsedSections.Contains(key),
        };
        section.OnToggle = expanded =>
        {
            if (expanded)
                collapsedSections.Remove(key);
            else
                collapsedSections.Add(key);
            Relayout();
        };
        return section;
    }

    private CollapsingHeaderNode BuildRareSection(DutySheet duty)
    {
        var (obtained, total) = Ownership.GetRareProgress(duty);
        var title = total == 0
            ? Strings.RareSectionNone
            : obtained == total
                ? Strings.RareSectionComplete(obtained, total)
                : Strings.RareSection(obtained, total);
        var section = CreateSection("rare", title);

        foreach (var rare in duty.RareItems)
        {
            var state = Ownership.GetState(rare.Item);
            if (Config.HideObtained && state.Obtained)
                continue;
            var row = CreateItemRow(rare.Item, state, Strings.Category(rare.Item.Category));
            row.TextTooltip = Strings.WhereToGet + "\n" + string.Join("\n", rare.Sources.Select(Strings.Source));
            section.AddNode(row);
        }

        if (total == 0)
            section.AddNode(CreateNoteRow(Strings.NoRareHere));
        return section;
    }

    private CollapsingHeaderNode? BuildRewardSection(DutySheet duty)
    {
        if (duty.Tomestones.Count == 0 && duty.ClearGil == 0)
            return null;

        var section = CreateSection("rewards", Strings.RewardsSection);
        foreach (var reward in duty.Tomestones)
        {
            var tooltip = string.Join("\n", reward.Amounts.Select(a =>
                Strings.AmountLine(a.IsFinal ? Strings.FinalBoss(a.Boss) : Strings.BossName(a.Boss), a.Amount)));
            if (reward.NewPlayerBonus > 0)
                tooltip += "\n" + Strings.NewPlayerBonus(reward.NewPlayerBonus);

            section.AddNode(new LootRowNode
            {
                IconId = reward.Tomestone.Icon,
                TooltipItemId = reward.Tomestone.ItemId,
                Name = reward.Tomestone.Name,
                Detail = reward.Amounts.Count > 1 ? string.Join(" + ", reward.Amounts.Select(a => a.Amount)) : string.Empty,
                Status = Strings.Amount(reward.Total),
                StatusColor = LootRowNode.DefaultTextColor,
                TextTooltip = tooltip,
            });
        }

        if (duty.ClearGil > 0)
        {
            section.AddNode(new LootRowNode
            {
                IconId = GilIcon,
                Name = Strings.ClearGil,
                Status = Strings.Amount(duty.ClearGil),
                StatusColor = LootRowNode.DefaultTextColor,
            });
        }
        return section;
    }

    private CollapsingHeaderNode? BuildChestSection(LootChest chest)
    {
        var states = chest.Entries.Select(e => (Entry: e, State: Ownership.GetState(e.Item))).ToList();
        var obtained = states.Count(s => s.State.Obtained);
        var section = CreateSection(chest.Key, Strings.ChestHeader(chest, obtained, states.Count));

        var shown = 0;
        foreach (var (entry, state) in states)
        {
            if (Config.HideObtained && state.Obtained)
                continue;
            var detail = entry.Probability is { } p
                ? Strings.Percent(p)
                : entry.Item.IsEquipment ? Strings.ItemLevel(entry.Item.ItemLevel) : Strings.Category(entry.Item.Category);
            section.AddNode(CreateItemRow(entry.Item, state, detail));
            shown++;
        }

        if (shown == 0)
        {
            if (!Config.HideObtained)
                return null;
            section.AddNode(CreateNoteRow(Strings.ChestAllObtained));
        }
        return section;
    }

    private LootRowNode CreateItemRow(LootItem item, LootItemState state, string detail)
    {
        var row = new LootRowNode
        {
            IconId = item.Icon,
            TooltipItemId = item.ItemId,
            Name = item.Name,
            NameColor = RarityColor(item.Rarity),
            Detail = detail,
            Status = Strings.Status(state),
            StatusColor = state.Obtained ? ObtainedColor : MissingColor,
        };
        row.OnRightClick = () => OpenItemMenu(item, state);
        return row;
    }

    private static LootRowNode CreateNoteRow(string text) => new()
    {
        Name = text,
        NameColor = LootRowNode.DimTextColor,
    };

    private unsafe void OpenItemMenu(LootItem item, LootItemState state)
    {
        contextMenu.Clear();
        contextMenu.AddItem(Strings.LinkInChat, () => AgentChatLog.Instance()->LinkItem(item.ItemId));
        if (item.IsEquipment)
            contextMenu.AddItem(Strings.TryOn, () => AgentTryon.TryOn(0, item.ItemId));

        // Statut forcé à la main, pour ce que le jeu ne permet pas de détecter (équipement revendu...).
        if (!state.IsUnlockable && state.Source != ObtainedSource.NotLoggedIn)
        {
            if (state.Obtained)
                contextMenu.AddItem(Strings.MarkNotObtained, () => Ownership.SetManual(item, false));
            else
                contextMenu.AddItem(Strings.MarkObtained, () => Ownership.SetManual(item, true));
            if (state.Source == ObtainedSource.Manual)
                contextMenu.AddItem(Strings.AutomaticDetection, () => Ownership.SetManual(item, null));
        }

        contextMenu.Open();
    }

    private static Vector4 RarityColor(byte rarity) => rarity switch
    {
        2 => new Vector4(0.55f, 0.95f, 0.55f, 1f),
        3 => new Vector4(0.45f, 0.65f, 1f, 1f),
        4 => new Vector4(0.75f, 0.55f, 1f, 1f),
        7 => new Vector4(1f, 0.55f, 0.8f, 1f),
        _ => LootRowNode.DefaultTextColor,
    };
}
