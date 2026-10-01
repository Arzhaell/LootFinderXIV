using System.Collections.Generic;
using System.Numerics;
using Lumina.Excel.Sheets;

namespace LootfinderXIV.Data;

public enum LootCategory
{
    Minion,
    Mount,
    Orchestrion,
    TripleTriadCard,
    Barding,
    FashionAccessory,
    Glasses,
    OtherUnlock,
    Equipment,
    Other,
}

public sealed class LootItem
{
    public required Item Row { get; init; }
    public required string Name { get; init; }
    public required LootCategory Category { get; init; }

    public uint ItemId => Row.RowId;
    public uint Icon => Row.Icon;
    public byte Rarity => Row.Rarity;
    public uint ItemLevel => Row.LevelItem.RowId;
    public bool IsEquipment => Category == LootCategory.Equipment;

    /// <summary>Mascotte, monture ou rouleau d'orchestrion.</summary>
    public bool IsRare => Category is LootCategory.Minion or LootCategory.Mount or LootCategory.Orchestrion;
}

/// <summary>Un objet dans un coffre, avec sa chance d'apparition (en %) quand elle est connue.</summary>
public sealed record LootEntry(LootItem Item, decimal? Probability);

public enum ChestKind
{
    /// <summary>Coffre qui apparaît après un boss.</summary>
    BossCoffer,

    /// <summary>Coffre au trésor placé dans la mission.</summary>
    TreasureChest,

    /// <summary>Objets lâchés directement par les boss ou obtenus ailleurs dans la mission.</summary>
    Other,
}

public sealed class LootChest
{
    public required string Title { get; init; }
    public required ChestKind Kind { get; init; }

    /// <summary>Coordonnées sur la carte du jeu (coffres au trésor uniquement).</summary>
    public Vector2? MapCoordinates { get; init; }

    public List<LootEntry> Entries { get; } = [];
}

/// <summary>Un objet rare et les coffres qui peuvent le contenir.</summary>
public sealed record RareLoot(LootItem Item, IReadOnlyList<string> Sources);

/// <summary>Mémoquartz d'un type donné rapportés par la mission.</summary>
public sealed class TomestoneReward
{
    public required LootItem Tomestone { get; init; }

    /// <summary>Quantité par boss (« Boss final » compris), dans l'ordre de la mission.</summary>
    public required IReadOnlyList<(string Source, uint Amount)> Amounts { get; init; }

    /// <summary>Bonus quand un joueur fait la mission pour la première fois.</summary>
    public uint NewPlayerBonus { get; init; }

    public uint Total
    {
        get
        {
            uint total = 0;
            foreach (var (_, amount) in Amounts)
                total += amount;
            return total;
        }
    }
}

public sealed class DutySheet
{
    public required uint ContentFinderConditionId { get; init; }
    public required string Name { get; init; }
    public required uint ContentTypeId { get; init; }
    public required string ContentTypeName { get; init; }
    public required byte Level { get; init; }
    public required ushort ItemLevel { get; init; }

    /// <summary>Coffres de boss dans l'ordre des combats, puis coffres au trésor, puis autres objets.</summary>
    public List<LootChest> Chests { get; } = [];

    /// <summary>Mascottes, montures et orchestrion, avec leurs sources.</summary>
    public List<RareLoot> RareItems { get; } = [];

    public List<TomestoneReward> Tomestones { get; } = [];
    public uint ClearGil { get; init; }

    /// <summary>Tous les objets de la mission, sans doublon.</summary>
    public List<LootItem> Items { get; } = [];
}
