using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using LootFinderXIV.Data;
using LootFinderXIV.Localization;
using LootFinderXIV.Services;

namespace LootFinderXIV.UI;

/// <summary>
/// Liste de toutes les missions, groupées par type, avec le nombre de loots rares obtenus.
/// Un clic sur une mission ouvre sa fiche.
/// </summary>
public sealed class OverviewWindow : NativeAddon
{
    public static readonly Vector2 DefaultSize = new(420.0f, 640.0f);

    private const float HeaderHeight = 24.0f;

    private static readonly Vector4 CompleteColor = new(0.55f, 0.95f, 0.55f, 1.0f);

    private readonly HashSet<uint> collapsedTypes = [];
    private readonly List<(DutySheet Duty, LootRowNode Row)> rows = [];
    private readonly List<(string TypeName, List<DutySheet> Duties, CollapsingHeaderNode Header)> sections = [];

    private TextNode? totalNode;
    private CheckboxNode? hideCompletedNode;
    private ScrollingNode<VerticalListNode>? body;
    private bool refreshQueued;
    private bool rebuildQueued;

    public required LootDatabase Database { get; init; }
    public required OwnershipService Ownership { get; init; }
    public required Configuration Config { get; init; }

    /// <summary>Appelé quand le joueur choisit une mission.</summary>
    public required Action<DutySheet> OnDutySelected { get; init; }

    /// <summary>
    /// Met à jour la liste à la frame suivante : compteurs seulement, ou reconstruction complète
    /// (changement de langue ou de filtre).
    /// </summary>
    public void QueueRefresh(bool rebuild = false)
    {
        rebuildQueued |= rebuild;
        if (refreshQueued || !IsOpen)
            return;
        refreshQueued = true;
        Plugin.Framework.RunOnTick(() =>
        {
            refreshQueued = false;
            var fullRebuild = rebuildQueued || Config.HideCompletedDuties;
            rebuildQueued = false;
            if (fullRebuild)
                Rebuild();
            else
                RefreshCounts();
        });
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        base.OnSetup(addon, atkValueSpan);

        totalNode = new TextNode
        {
            Position = ContentStartPosition,
            Size = new Vector2(ContentSize.X - 190.0f, HeaderHeight),
            AlignmentType = AlignmentType.Left,
            FontSize = 12,
            TextColor = LootRowNode.DimTextColor,
        };
        totalNode.AttachNode(this);

        hideCompletedNode = new CheckboxNode
        {
            Position = ContentStartPosition + new Vector2(ContentSize.X - 185.0f, 0.0f),
            Size = new Vector2(185.0f, HeaderHeight),
            String = Strings.HideCompletedDuties,
            IsChecked = Config.HideCompletedDuties,
            OnClick = isChecked =>
            {
                Config.HideCompletedDuties = isChecked;
                Config.Save();
                QueueRefresh(rebuild: true);
            },
        };
        hideCompletedNode.AttachNode(this);

        body = new ScrollingNode<VerticalListNode>
        {
            Position = ContentStartPosition + new Vector2(0.0f, HeaderHeight + 4.0f),
            Size = ContentSize - new Vector2(0.0f, HeaderHeight + 4.0f),
            AutoHideScrollBar = true,
            ScrollSpeed = 40,
        };
        body.ContentNode.FitWidth = true;
        body.ContentNode.FitContents = true;
        body.ContentNode.ItemSpacing = 6.0f;
        body.AttachNode(this);

        Rebuild();
    }

    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        base.OnFinalize(addon);
        rows.Clear();
        sections.Clear();
        totalNode = null;
        hideCompletedNode = null;
        body = null;
    }

    private void Rebuild()
    {
        if (body is null || hideCompletedNode is null)
            return;

        WindowNode?.SetTitle(Strings.OverviewTitle, LootWindow.DefaultTitle);
        hideCompletedNode.String = Strings.HideCompletedDuties;
        hideCompletedNode.IsChecked = Config.HideCompletedDuties;

        var list = body.ContentNode;
        list.Clear();
        rows.Clear();
        sections.Clear();

        var progress = Database.Duties.ToDictionary(d => d.ContentFinderConditionId, Ownership.GetRareProgress);
        var groups = Database.Duties
            .GroupBy(d => d.ContentTypeId)
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var duties = group
                .OrderBy(d => d.Level)
                .ThenBy(d => d.ItemLevel)
                .ThenBy(d => d.ContentFinderConditionId) // ordre du jeu (étages des donjons sans fond...)
                .ToList();
            var visible = Config.HideCompletedDuties
                ? duties.Where(d => !IsComplete(progress[d.ContentFinderConditionId])).ToList()
                : duties;
            if (visible.Count == 0)
                continue;

            var typeId = group.Key;
            var typeName = duties[0].ContentTypeName;
            var header = new CollapsingHeaderNode
            {
                String = SectionTitle(typeName, duties, progress),
                FitWidth = true,
                ItemSpacing = 2.0f,
                FirstItemSpacing = 2.0f,
                IsCollapsed = collapsedTypes.Contains(typeId),
            };
            header.OnToggle = expanded =>
            {
                if (expanded)
                    collapsedTypes.Remove(typeId);
                else
                    collapsedTypes.Add(typeId);
                Relayout();
            };

            foreach (var duty in visible)
            {
                var row = new LootRowNode
                {
                    IconId = duty.Icon,
                    Name = duty.Name,
                    Detail = Strings.Level(duty.Level),
                    TextTooltip = Strings.OpenSheetTooltip,
                    OnLeftClick = () => Plugin.Framework.RunOnTick(() => OnDutySelected(duty)),
                };
                SetProgress(row, progress[duty.ContentFinderConditionId]);
                header.AddNode(row);
                rows.Add((duty, row));
            }

            list.AddNode(header);
            sections.Add((typeName, duties, header));
        }

        UpdateTotal(progress);
        Relayout();
    }

    /// <summary>Met à jour les compteurs sans recréer les lignes.</summary>
    private void RefreshCounts()
    {
        if (body is null)
            return;

        var progress = Database.Duties.ToDictionary(d => d.ContentFinderConditionId, Ownership.GetRareProgress);
        foreach (var (duty, row) in rows)
            SetProgress(row, progress[duty.ContentFinderConditionId]);
        foreach (var (typeName, duties, header) in sections)
            header.String = SectionTitle(typeName, duties, progress);
        UpdateTotal(progress);
    }

    private void Relayout()
    {
        if (body is null)
            return;
        body.ContentNode.RecalculateLayout();
        body.RecalculateSizes();
    }

    private void UpdateTotal(Dictionary<uint, (int Obtained, int Total)> progress)
    {
        if (totalNode is null)
            return;
        var obtained = progress.Values.Sum(p => p.Obtained);
        var total = progress.Values.Sum(p => p.Total);
        totalNode.String = Strings.RareSection(obtained, total);
    }

    private static string SectionTitle(string typeName, List<DutySheet> duties, Dictionary<uint, (int Obtained, int Total)> progress)
    {
        var obtained = duties.Sum(d => progress[d.ContentFinderConditionId].Obtained);
        var total = duties.Sum(d => progress[d.ContentFinderConditionId].Total);
        return Strings.OverviewSection(typeName, obtained, total);
    }

    private static void SetProgress(LootRowNode row, (int Obtained, int Total) progress)
    {
        row.Status = Strings.Progress(progress.Obtained, progress.Total);
        row.StatusColor = progress.Total == 0
            ? LootRowNode.DimTextColor
            : IsComplete(progress) ? CompleteColor : LootRowNode.DefaultTextColor;
    }

    /// <summary>Tous les loots rares obtenus (vrai aussi pour une mission sans loot rare).</summary>
    private static bool IsComplete((int Obtained, int Total) progress) => progress.Obtained == progress.Total;
}
