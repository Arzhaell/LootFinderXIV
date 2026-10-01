using System.Collections.Generic;
using System.Numerics;
using Lumina.Excel.Sheets;

namespace LootFinderXIV.Data;

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

    public bool IsTripleTriadCard => Category == LootCategory.TripleTriadCard;

    /// <summary>Présenté dans sa propre section de la fiche (rares, cartes) plutôt que dans les coffres.</summary>
    public bool HasOwnSection => IsRare || IsTripleTriadCard;
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

/// <summary>Un boss de la mission ; Name est null quand son nom n'est pas connu.</summary>
public readonly record struct BossRef(uint FightNo, string? Name);

public sealed class LootChest
{
    public required ChestKind Kind { get; init; }

    /// <summary>Boss qui fait apparaître le coffre (coffres de boss uniquement).</summary>
    public BossRef? Boss { get; init; }

    /// <summary>Numéro du coffre au trésor, ou du coffre de boss quand le boss en a plusieurs (sinon 0).</summary>
    public int Number { get; init; }

    /// <summary>Coordonnées sur la carte du jeu (coffres au trésor uniquement).</summary>
    public Vector2? MapCoordinates { get; init; }

    /// <summary>Identifiant stable dans la mission (pour mémoriser les sections repliées).</summary>
    public string Key => $"{Kind}-{Boss?.FightNo}-{Number}";

    public List<LootEntry> Entries { get; } = [];
}

public enum LootSourceKind
{
    Chest,
    BossDrop,
    DutyDrop,
}

/// <summary>Où un objet peut s'obtenir : un coffre (avec sa chance), un boss, ou ailleurs dans la mission.</summary>
public sealed record LootSource(LootSourceKind Kind, LootChest? Chest, BossRef? Boss, decimal? Probability);

/// <summary>Un objet présenté dans sa propre section (rare ou carte) et les endroits où l'obtenir.</summary>
public sealed record FeaturedLoot(LootItem Item, IReadOnlyList<LootSource> Sources);

/// <summary>Mémoquartz d'un type donné rapportés par la mission.</summary>
public sealed class TomestoneReward
{
    public required LootItem Tomestone { get; init; }

    /// <summary>Quantité par boss, dans l'ordre de la mission (IsFinal : boss final).</summary>
    public required IReadOnlyList<(BossRef Boss, bool IsFinal, uint Amount)> Amounts { get; init; }

    /// <summary>Bonus quand un joueur fait la mission pour la première fois.</summary>
    public uint NewPlayerBonus { get; init; }

    public uint Total
    {
        get
        {
            uint total = 0;
            foreach (var (_, _, amount) in Amounts)
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

    /// <summary>Icône du type de mission (donjon, défi, raid...).</summary>
    public uint Icon { get; init; }

    /// <summary>Coffres de boss dans l'ordre des combats, puis coffres au trésor, puis autres objets.</summary>
    public List<LootChest> Chests { get; } = [];

    /// <summary>Mascottes, montures et orchestrion, avec leurs sources.</summary>
    public List<FeaturedLoot> RareItems { get; } = [];

    /// <summary>Cartes Triple Triad, avec leurs sources.</summary>
    public List<FeaturedLoot> TripleTriadCards { get; } = [];

    public List<TomestoneReward> Tomestones { get; } = [];
    public uint ClearGil { get; init; }

    /// <summary>Tous les objets de la mission, sans doublon.</summary>
    public List<LootItem> Items { get; } = [];
}
