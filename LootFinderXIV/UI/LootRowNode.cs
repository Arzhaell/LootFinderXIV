using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;

namespace LootFinderXIV.UI;

/// <summary>
/// Une ligne de la fiche : icône, nom, détail (chance, niveau...) et statut, alignés en colonnes.
/// </summary>
public sealed unsafe class LootRowNode : SimpleComponentNode
{
    public const float RowHeight = 30.0f;
    private const float IconSize = 26.0f;
    private const float DetailWidth = 70.0f;
    private const float StatusWidth = 72.0f;

    public static readonly Vector4 DefaultTextColor = new(1.0f, 1.0f, 1.0f, 1.0f);
    public static readonly Vector4 DimTextColor = new(0.72f, 0.72f, 0.72f, 1.0f);

    private readonly IconImageNode iconNode;
    private readonly TextNode nameNode;
    private readonly TextNode detailNode;
    private readonly TextNode statusNode;

    public LootRowNode()
    {
        iconNode = new IconImageNode { FitTexture = true };
        iconNode.AttachNode(this);

        nameNode = new TextNode
        {
            AlignmentType = AlignmentType.Left,
            FontSize = 14,
            TextColor = DefaultTextColor,
        };
        nameNode.AttachNode(this);

        detailNode = new TextNode
        {
            AlignmentType = AlignmentType.Right,
            FontSize = 12,
            TextColor = DimTextColor,
        };
        detailNode.AttachNode(this);

        statusNode = new TextNode
        {
            AlignmentType = AlignmentType.Right,
            FontSize = 12,
        };
        statusNode.AttachNode(this);

        Height = RowHeight;
        CollisionNode.AddEvent(AtkEventType.MouseClick, OnMouseClick);
    }

    /// <summary>Clic droit sur la ligne.</summary>
    public Action? OnRightClick { get; set; }

    /// <summary>Clic gauche sur la ligne (affiche aussi le curseur de clic).</summary>
    public Action? OnLeftClick
    {
        get;
        set
        {
            field = value;
            CollisionNode.ShowClickableCursor = value != null;
        }
    }

    public uint IconId
    {
        set => iconNode.IconId = value;
    }

    /// <summary>Objet dont l'infobulle du jeu s'affiche au survol de l'icône (0 = aucune).</summary>
    public uint TooltipItemId
    {
        set => iconNode.ItemTooltip = value;
    }

    public string Name
    {
        set => nameNode.String = value;
    }

    public Vector4 NameColor
    {
        set => nameNode.TextColor = value;
    }

    public string Detail
    {
        set => detailNode.String = value;
    }

    public string Status
    {
        set => statusNode.String = value;
    }

    public Vector4 StatusColor
    {
        set => statusNode.TextColor = value;
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();

        var iconY = (Height - IconSize) / 2;
        iconNode.Size = new Vector2(IconSize);
        iconNode.Position = new Vector2(2.0f, iconY);

        var textX = iconNode.X + IconSize + 6.0f;
        statusNode.Size = new Vector2(StatusWidth, Height);
        statusNode.Position = new Vector2(Width - StatusWidth - 4.0f, 0.0f);

        detailNode.Size = new Vector2(DetailWidth, Height);
        detailNode.Position = new Vector2(statusNode.X - DetailWidth - 4.0f, 0.0f);

        nameNode.Size = new Vector2(Math.Max(0.0f, detailNode.X - textX - 4.0f), Height);
        nameNode.Position = new Vector2(textX, 0.0f);
    }

    private void OnMouseClick(AtkEventListener* listener, AtkEventType eventType, int param, AtkEvent* atkEvent, AtkEventData* data)
    {
        if (data->IsRightClick)
            OnRightClick?.Invoke();
        else if (data->IsLeftClick)
            OnLeftClick?.Invoke();
    }
}
