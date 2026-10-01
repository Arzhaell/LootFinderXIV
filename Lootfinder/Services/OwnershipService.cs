using System;
using System.Collections.Generic;
using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lootfinder.Data;
using LootItem = Lootfinder.Data.LootItem;

namespace Lootfinder.Services;

public enum ObtainedSource
{
    None,
    NotLoggedIn,
    Unlocked,
    Cabinet,
    Held,
    Seen,
    Manual,
}

public readonly record struct LootItemState(bool IsUnlockable, bool Obtained, ObtainedSource Source);

/// <summary>
/// Détermine si un objet a déjà été obtenu :
/// - objets à débloquer (mascottes, montures, orchestrion, cartes...) : état de déblocage du jeu ;
/// - autres objets : présence actuelle ou passée dans l'inventaire, l'arsenal, les sacoches, le coffre à
///   apparences ou l'armoire (ces deux derniers une fois ouverts), mémorisée par personnage,
///   avec possibilité de forcer le statut à la main.
/// </summary>
public sealed unsafe class OwnershipService : IDisposable
{
    private static readonly GameInventoryType[] ScannedContainers =
    [
        GameInventoryType.Inventory1, GameInventoryType.Inventory2,
        GameInventoryType.Inventory3, GameInventoryType.Inventory4,
        GameInventoryType.EquippedItems,
        GameInventoryType.ArmoryMainHand, GameInventoryType.ArmoryOffHand,
        GameInventoryType.ArmoryHead, GameInventoryType.ArmoryBody, GameInventoryType.ArmoryHands,
        GameInventoryType.ArmoryWaist, GameInventoryType.ArmoryLegs, GameInventoryType.ArmoryFeets,
        GameInventoryType.ArmoryEar, GameInventoryType.ArmoryNeck, GameInventoryType.ArmoryWrist,
        GameInventoryType.ArmoryRings,
        GameInventoryType.SaddleBag1, GameInventoryType.SaddleBag2,
        GameInventoryType.PremiumSaddleBag1, GameInventoryType.PremiumSaddleBag2,
    ];

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(2);

    private readonly Configuration config;
    private readonly LootDatabase database;
    private readonly Dictionary<uint, bool> unlockableCache = [];
    private readonly Dictionary<uint, bool> unlockedCache = [];
    private HashSet<uint> held = [];
    private DateTime nextScan = DateTime.MinValue;

    public OwnershipService(Configuration config, LootDatabase database)
    {
        this.config = config;
        this.database = database;
        Plugin.Framework.Update += OnUpdate;
        Plugin.GameInventory.InventoryChanged += OnInventoryChanged;
        Plugin.GameGui.AgentUpdate += OnAgentUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        Plugin.GameInventory.InventoryChanged -= OnInventoryChanged;
        Plugin.GameGui.AgentUpdate -= OnAgentUpdate;
    }

    /// <summary>Déclenché (sur le thread du jeu) quand le statut d'au moins un objet a pu changer.</summary>
    public event Action? Changed;

    public CharacterData? Character
    {
        get
        {
            var contentId = Plugin.PlayerState.ContentId;
            if (!Plugin.PlayerState.IsLoaded || contentId == 0)
                return null;
            if (!config.Characters.TryGetValue(contentId, out var data))
                config.Characters[contentId] = data = new CharacterData();
            return data;
        }
    }

    public bool IsUnlockable(LootItem item)
    {
        if (!unlockableCache.TryGetValue(item.ItemId, out var unlockable))
            unlockableCache[item.ItemId] = unlockable = Plugin.UnlockState.IsItemUnlockable(item.Row);
        return unlockable;
    }

    public LootItemState GetState(LootItem item)
    {
        var unlockable = IsUnlockable(item);
        if (Character is not { } character)
            return new LootItemState(unlockable, false, ObtainedSource.NotLoggedIn);

        if (unlockable)
        {
            if (!unlockedCache.TryGetValue(item.ItemId, out var unlocked))
                unlockedCache[item.ItemId] = unlocked = Plugin.UnlockState.IsItemUnlocked(item.Row);
            return new LootItemState(true, unlocked, unlocked ? ObtainedSource.Unlocked : ObtainedSource.None);
        }

        if (character.ManualOverrides.TryGetValue(item.ItemId, out var manual))
            return new LootItemState(false, manual, ObtainedSource.Manual);
        if (character.CabinetItems.Contains(item.ItemId))
            return new LootItemState(false, true, ObtainedSource.Cabinet);
        if (held.Contains(item.ItemId))
            return new LootItemState(false, true, ObtainedSource.Held);
        if (character.SeenItems.Contains(item.ItemId))
            return new LootItemState(false, true, ObtainedSource.Seen);
        return new LootItemState(false, false, ObtainedSource.None);
    }

    /// <summary>Nombre de loots rares obtenus / total pour une mission.</summary>
    public (int Obtained, int Total) GetRareProgress(DutySheet duty)
    {
        var obtained = 0;
        foreach (var rare in duty.RareItems)
        {
            if (GetState(rare.Item).Obtained)
                obtained++;
        }
        return (obtained, duty.RareItems.Count);
    }

    public void SetManual(LootItem item, bool? obtained)
    {
        if (Character is not { } character)
            return;
        if (obtained is { } value)
            character.ManualOverrides[item.ItemId] = value;
        else
            character.ManualOverrides.Remove(item.ItemId);
        config.Save();
        Changed?.Invoke();
    }

    private void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events) => nextScan = DateTime.MinValue;

    private void OnAgentUpdate(Dalamud.Game.Gui.AgentUpdateFlag flag)
    {
        if (!flag.HasFlag(Dalamud.Game.Gui.AgentUpdateFlag.UnlocksUpdate))
            return;
        unlockedCache.Clear();
        Changed?.Invoke();
    }

    private void OnUpdate(IFramework framework)
    {
        if (DateTime.UtcNow < nextScan)
            return;
        nextScan = DateTime.UtcNow + ScanInterval;

        if (Character is not { } character)
            return;

        var changed = false;
        var current = new HashSet<uint>();
        foreach (var type in ScannedContainers)
        {
            foreach (ref readonly var slot in Plugin.GameInventory.GetInventoryItems(type))
            {
                if (!slot.IsEmpty && database.Items.ContainsKey(slot.BaseItemId))
                    current.Add(slot.BaseItemId);
            }
        }

        // Coffre à apparences : chargé seulement après l'avoir ouvert une fois dans la session.
        var mirage = MirageManager.Instance();
        if (mirage != null && mirage->PrismBoxLoaded)
        {
            foreach (var id in mirage->PrismBoxItemIds)
            {
                var itemId = id % 500_000; // retire les marqueurs HQ (+1 000 000) et collectable (+500 000)
                if (itemId != 0 && database.Items.ContainsKey(itemId))
                    current.Add(itemId);
            }
        }

        // Armoire : on mémorise son contenu quand elle est chargée (après ouverture).
        var uiState = UIState.Instance();
        if (uiState != null && uiState->Cabinet.IsCabinetLoaded())
        {
            var stored = new HashSet<uint>();
            foreach (var (itemId, cabinetId) in database.CabinetIds)
            {
                if (uiState->Cabinet.IsItemInCabinet(cabinetId))
                    stored.Add(itemId);
            }
            if (!stored.SetEquals(character.CabinetItems))
            {
                character.CabinetItems = stored;
                changed = true;
            }
        }

        var seenChanged = false;
        foreach (var itemId in current)
            seenChanged |= character.SeenItems.Add(itemId);
        if (seenChanged || changed)
            config.Save();

        changed |= seenChanged || !current.SetEquals(held);
        held = current;
        if (changed)
            Changed?.Invoke();
    }
}
