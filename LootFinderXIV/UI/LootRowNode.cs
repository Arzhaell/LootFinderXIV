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
    private const float DetailMinWidth = 70.0f;
    private const float DetailMaxRatio = 0.45f; // part maximale de la ligne pour le détail
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

        // Noms et détails trop longs : coupés avec « … » au lieu de déborder sur la colonne voisine.
        nameNode = new TextNode
        {
            AlignmentType = AlignmentType.Left,
            FontSize = 14,
            TextColor = DefaultTextColor,
            TextFlags = TextFlags.Ellipsis,
        };
        nameNode.AttachNode(this);

        detailNode = new TextNode
        {
            AlignmentType = AlignmentType.Right,
            FontSize = 12,
            TextColor = DimTextColor,
            TextFlags = TextFlags.Ellipsis,
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
        set
        {
            detailNode.String = value;
            if (Width > 0)
                OnSizeChanged(); // la largeur de la colonne dépend du texte
        }
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

        // Colonne du détail : au moins 70 px, élargie si son texte est plus long (ex. « 15 + 25 + 35 + 45 »).
        var detailWidth = Math.Clamp(detailNode.GetTextDrawSize(false).X + 4.0f, DetailMinWidth, Math.Max(DetailMinWidth, Width * DetailMaxRatio));
        detailNode.Size = new Vector2(detailWidth, Height);
        detailNode.Position = new Vector2(statusNode.X - detailWidth - 4.0f, 0.0f);

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
