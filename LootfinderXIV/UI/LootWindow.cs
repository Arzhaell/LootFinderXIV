using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using LootfinderXIV.Data;
using LootfinderXIV.Services;
using ContextMenu = KamiToolKit.ContextMenu.ContextMenu;

namespace LootfinderXIV.UI;

/// <summary>
/// Fiche de la mission façon base de données : loots rares, mémoquartz et gils, puis un bloc par coffre.
/// </summary>
public sealed class LootWindow : NativeAddon
{
    public const string DefaultTitle = "LootfinderXIV";
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

    public required DutyWatcher Watcher { get; init; }
    public required OwnershipService Ownership { get; init; }
    public required Configuration Config { get; init; }

    /// <summary>Appelé quand la fenêtre se ferme (par le joueur ou le plugin).</summary>
    public Action? OnClosed { get; set; }

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
            String = "Masquer les obtenus",
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
        OnClosed?.Invoke();
    }

    private void Rebuild()
    {
        if (body is null || infoNode is null || emptyNode is null)
            return;

        var list = body.ContentNode;
        list.Clear();

        if (Watcher.ActiveDuty is not { } duty)
        {
            WindowNode?.SetTitle(DefaultTitle);
            infoNode.String = string.Empty;
            emptyNode.String = "Sélectionnez une mission dans l'outil de mission,\nou entrez dans une mission, pour voir son butin.";
            emptyNode.IsVisible = true;
            body.RecalculateSizes();
            return;
        }

        emptyNode.IsVisible = false;
        if (duty.ContentFinderConditionId != shownDutyId)
        {
            shownDutyId = duty.ContentFinderConditionId;
            WindowNode?.SetTitle(duty.Name, DefaultTitle);
            body.ScrollToStart();
        }

        infoNode.String = $"{duty.ContentTypeName} · Niv. {duty.Level}" + (duty.ItemLevel > 0 ? $" · iLvl {duty.ItemLevel}" : string.Empty);

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
            ? "Récompenses rares : aucune"
            : obtained == total
                ? $"Récompenses rares : tout obtenu ({obtained}/{total})"
                : $"Récompenses rares : {obtained}/{total}";
        var section = CreateSection("rare", title);

        foreach (var rare in duty.RareItems)
        {
            var state = Ownership.GetState(rare.Item);
            if (Config.HideObtained && state.Obtained)
                continue;
            var row = CreateItemRow(rare.Item, state, CategoryLabel(rare.Item.Category));
            row.TextTooltip = "Où l'obtenir :\n" + string.Join("\n", rare.Sources);
            section.AddNode(row);
        }

        if (total == 0)
            section.AddNode(CreateNoteRow("Pas de mascotte, monture ni rouleau d'orchestrion ici."));
        return section;
    }

    private CollapsingHeaderNode? BuildRewardSection(DutySheet duty)
    {
        if (duty.Tomestones.Count == 0 && duty.ClearGil == 0)
            return null;

        var section = CreateSection("rewards", "Mémoquartz et gils");
        foreach (var reward in duty.Tomestones)
        {
            var tooltip = string.Join("\n", reward.Amounts.Select(a => $"{a.Source} : {a.Amount}"));
            if (reward.NewPlayerBonus > 0)
                tooltip += $"\nBonus si un joueur découvre la mission : +{reward.NewPlayerBonus}";

            section.AddNode(new LootRowNode
            {
                IconId = reward.Tomestone.Icon,
                TooltipItemId = reward.Tomestone.ItemId,
                Name = reward.Tomestone.Name,
                Detail = reward.Amounts.Count > 1 ? string.Join(" + ", reward.Amounts.Select(a => a.Amount)) : string.Empty,
                Status = $"×{reward.Total}",
                StatusColor = LootRowNode.DefaultTextColor,
                TextTooltip = tooltip,
            });
        }

        if (duty.ClearGil > 0)
        {
            section.AddNode(new LootRowNode
            {
                IconId = GilIcon,
                Name = "Gils en fin de mission",
                Status = $"×{duty.ClearGil:N0}",
                StatusColor = LootRowNode.DefaultTextColor,
            });
        }
        return section;
    }

    private CollapsingHeaderNode? BuildChestSection(LootChest chest)
    {
        var states = chest.Entries.Select(e => (Entry: e, State: Ownership.GetState(e.Item))).ToList();
        var obtained = states.Count(s => s.State.Obtained);

        var title = chest.MapCoordinates is { } coords
            ? $"{chest.Title}  (X {coords.X:0.0} · Y {coords.Y:0.0})"
            : chest.Title;
        var section = CreateSection(chest.Title, $"{title}   {obtained}/{states.Count}");

        var shown = 0;
        foreach (var (entry, state) in states)
        {
            if (Config.HideObtained && state.Obtained)
                continue;
            var detail = entry.Probability is { } p
                ? $"{p:0.#} %"
                : entry.Item.IsEquipment ? $"iLvl {entry.Item.ItemLevel}" : CategoryLabel(entry.Item.Category);
            section.AddNode(CreateItemRow(entry.Item, state, detail));
            shown++;
        }

        if (shown == 0)
        {
            if (!Config.HideObtained)
                return null;
            section.AddNode(CreateNoteRow("Tout est obtenu dans ce coffre."));
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
            Status = StatusLabel(state),
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
        contextMenu.AddItem("Lien dans le chat", () => AgentChatLog.Instance()->LinkItem(item.ItemId));
        if (item.IsEquipment)
            contextMenu.AddItem("Essayer", () => AgentTryon.TryOn(0, item.ItemId));

        // Statut forcé à la main, pour ce que le jeu ne permet pas de détecter (équipement revendu...).
        if (!state.IsUnlockable && state.Source != ObtainedSource.NotLoggedIn)
        {
            if (state.Obtained)
                contextMenu.AddItem("Marquer comme non obtenu", () => Ownership.SetManual(item, false));
            else
                contextMenu.AddItem("Marquer comme obtenu", () => Ownership.SetManual(item, true));
            if (state.Source == ObtainedSource.Manual)
                contextMenu.AddItem("Revenir à la détection automatique", () => Ownership.SetManual(item, null));
        }

        contextMenu.Open();
    }

    private static string StatusLabel(LootItemState state) => state switch
    {
        { Source: ObtainedSource.NotLoggedIn } => string.Empty,
        { Obtained: false } => "Manquant",
        { Source: ObtainedSource.Unlocked } => "Débloqué",
        { Source: ObtainedSource.Cabinet } => "Armoire",
        { Source: ObtainedSource.Held } => "Possédé",
        _ => "Obtenu",
    };

    private static string CategoryLabel(LootCategory category) => category switch
    {
        LootCategory.Minion => "Mascotte",
        LootCategory.Mount => "Monture",
        LootCategory.Orchestrion => "Orchestrion",
        LootCategory.TripleTriadCard => "Carte TT",
        LootCategory.Barding => "Barde",
        LootCategory.FashionAccessory => "Accessoire",
        LootCategory.Glasses => "Lunettes",
        LootCategory.OtherUnlock => "Déblocage",
        LootCategory.Equipment => "Équipement",
        _ => "Divers",
    };

    private static Vector4 RarityColor(byte rarity) => rarity switch
    {
        2 => new Vector4(0.55f, 0.95f, 0.55f, 1f),
        3 => new Vector4(0.45f, 0.65f, 1f, 1f),
        4 => new Vector4(0.75f, 0.55f, 1f, 1f),
        7 => new Vector4(1f, 0.55f, 0.8f, 1f),
        _ => LootRowNode.DefaultTextColor,
    };
}
