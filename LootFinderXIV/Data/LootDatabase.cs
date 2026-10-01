using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;

namespace LootFinderXIV.Data;

/// <summary>
/// Construit, pour chaque mission (ContentFinderCondition), une fiche façon base de données :
/// loots rares, un bloc par coffre, mémoquartz et gils.
/// Les coffres viennent des tables communautaires de LuminaSupplemental (le jeu ne les contient pas),
/// les mémoquartz et les gils de la feuille InstanceContent du jeu.
/// </summary>
public sealed class LootDatabase
{
    // Valeurs de ItemAction.Action pour les objets à débloquer.
    private const uint ActionCompanion = 853;
    private const uint ActionBuddyEquip = 1013;
    private const uint ActionMount = 1322;
    private const uint ActionSecretRecipeBook = 2136;
    private const uint ActionUnlockLink = 2633;
    private const uint ActionTripleTriadCard = 3357;
    private const uint ActionFolkloreTome = 4107;
    private const uint ActionFieldNotes = 19743;
    private const uint ActionOrnament = 20086;
    private const uint ActionOrchestrionRoll = 25183;
    private const uint ActionFramersKit = 29459;
    private const uint ActionGlasses = 37312;

    // ContentFinderCondition.ContentLinkType pour une InstanceContent.
    private const byte ContentLinkInstance = 1;

    public IReadOnlyList<DutySheet> Duties { get; }
    public IReadOnlyDictionary<uint, DutySheet> ByContentFinderCondition { get; }

    /// <summary>Tous les objets de loot connus (hors mémoquartz).</summary>
    public IReadOnlyDictionary<uint, LootItem> Items { get; }

    /// <summary>Objet → ligne de la feuille Cabinet (armoire).</summary>
    public IReadOnlyDictionary<uint, uint> CabinetIds { get; }

    private LootDatabase(List<DutySheet> duties, Dictionary<uint, LootItem> items, Dictionary<uint, uint> cabinetIds)
    {
        Duties = duties;
        ByContentFinderCondition = duties.ToDictionary(d => d.ContentFinderConditionId);
        Items = items;
        CabinetIds = cabinetIds;
    }

    public static LootDatabase Load(GameData data, IPluginLog log)
    {
        var bosses = LoadCsv<DungeonBoss>(data, log, CsvLoader.DungeonBossResourceName);
        var bossChests = LoadCsv<DungeonBossChest>(data, log, CsvLoader.DungeonBossChestResourceName);
        var bossDrops = LoadCsv<DungeonBossDrop>(data, log, CsvLoader.DungeonBossDropResourceName);
        var chests = LoadCsv<DungeonChest>(data, log, CsvLoader.DungeonChestResourceName);
        var chestItems = LoadCsv<DungeonChestItem>(data, log, CsvLoader.DungeonChestItemResourceName);
        var dutyDrops = LoadCsv<DungeonDrop>(data, log, CsvLoader.DungeonDropItemResourceName);

        var cfcSheet = data.Excel.GetSheet<ContentFinderCondition>();
        var itemSheet = data.Excel.GetSheet<Item>();
        var bnpcNames = data.Excel.GetSheet<BNpcName>();
        var maps = data.Excel.GetSheet<Map>();
        var instanceContents = data.Excel.GetSheet<InstanceContent>();

        var items = new Dictionary<uint, LootItem>();
        LootItem? GetItem(uint itemId)
        {
            if (items.TryGetValue(itemId, out var existing))
                return existing;
            if (CreateItem(itemSheet, itemId) is not { } item)
                return null;
            return items[itemId] = item;
        }

        // Mémoquartz des trois emplacements de récompense d'InstanceContent (A, B, C) : feuille Tomestones 1, 2, 3.
        var tomestones = new Dictionary<uint, LootItem>();
        foreach (var row in data.Excel.GetSheet<TomestonesItem>())
        {
            if (row.Tomestones.RowId is >= 1 and <= 3 && CreateItem(itemSheet, row.Item.RowId) is { } tomestone)
                tomestones[row.Tomestones.RowId] = tomestone;
        }

        var bossByRowId = bosses.ToDictionary(b => b.RowId);
        var bossNames = bosses
            .GroupBy(b => (b.ContentFinderConditionId, b.FightNo))
            .ToDictionary(g => g.Key, g => g.First().BNpcNameId);
        var chestItemsByChest = chestItems.ToLookup(c => c.ChestId);
        var bossChestsByDuty = bossChests.ToLookup(c => c.ContentFinderConditionId);
        var bossDropsByDuty = bossDrops.ToLookup(c => c.ContentFinderConditionId);
        var chestsByDuty = chests.ToLookup(c => c.ContentFinderConditionId);
        var dutyDropsByDuty = dutyDrops.ToLookup(c => c.ContentFinderConditionId);
        var bossesByDuty = bosses.ToLookup(b => b.ContentFinderConditionId);

        var dutyIds = bossChests.Select(r => r.ContentFinderConditionId)
            .Concat(bossDrops.Select(r => r.ContentFinderConditionId))
            .Concat(chests.Select(r => r.ContentFinderConditionId))
            .Concat(dutyDrops.Select(r => r.ContentFinderConditionId))
            .ToHashSet();

        var duties = new List<DutySheet>();
        foreach (var cfcId in dutyIds)
        {
            if (cfcSheet.GetRowOrDefault(cfcId) is not { } cfc)
                continue;
            var dutyName = cfc.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(dutyName))
                continue;

            BossRef Boss(uint fight) => new(fight,
                bossNames.TryGetValue((cfcId, fight), out var bnpcId)
                && bnpcNames.GetRowOrDefault(bnpcId)?.Singular.ExtractText() is { Length: > 0 } name
                    ? Capitalize(name)
                    : null);

            var instance = cfc.ContentLinkType == ContentLinkInstance
                ? instanceContents.GetRowOrDefault(cfc.Content.RowId)
                : null;

            var duty = new DutySheet
            {
                ContentFinderConditionId = cfcId,
                Name = Capitalize(dutyName),
                ContentTypeId = cfc.ContentType.RowId,
                ContentTypeName = cfc.ContentType.ValueNullable?.Name.ExtractText() is { Length: > 0 } typeName
                    ? Capitalize(typeName)
                    : "?",
                Level = cfc.ClassJobLevelRequired,
                ItemLevel = cfc.ItemLevelRequired,
                Icon = cfc.ContentType.ValueNullable?.Icon ?? 0,
                ClearGil = instance?.InstanceClearGil ?? 0,
            };

            // Où trouver chaque objet rare ou carte (coffre + chance, boss...).
            var featuredSources = new Dictionary<uint, List<LootSource>>();
            void NoteFeatured(LootItem item, LootSource source)
            {
                if (!item.HasOwnSection)
                    return;
                if (!featuredSources.TryGetValue(item.ItemId, out var list))
                    featuredSources[item.ItemId] = list = [];
                if (!list.Contains(source))
                    list.Add(source);
            }

            // Coffres de boss. Les coffres au trésor rattachés à un boss sont ses coffres de fin de combat :
            // ils donnent les chances d'apparition des objets.
            var bossChestChances = new Dictionary<uint, Dictionary<uint, decimal?>>(); // combat → objet → chance
            foreach (var chest in chestsByDuty[cfcId])
            {
                if (chest.DungeonBossId == 0 || !bossByRowId.TryGetValue(chest.DungeonBossId, out var boss) || boss.ContentFinderConditionId != cfcId)
                    continue;
                if (!bossChestChances.TryGetValue(boss.FightNo, out var chances))
                    bossChestChances[boss.FightNo] = chances = [];
                foreach (var entry in chestItemsByChest[chest.RowId])
                    chances[entry.ItemId] = Max(chances.GetValueOrDefault(entry.ItemId), entry.Probability);
            }

            var coffersByFight = bossChestsByDuty[cfcId]
                .GroupBy(c => c.FightNo)
                .ToDictionary(g => g.Key, g => g.GroupBy(c => c.CofferNo).OrderBy(c => c.Key).Select(c => c.Select(i => i.ItemId).Distinct().ToList()).ToList());
            foreach (var fight in coffersByFight.Keys.Concat(bossChestChances.Keys).Distinct().Order())
            {
                var chances = bossChestChances.GetValueOrDefault(fight) ?? [];
                var coffers = coffersByFight.GetValueOrDefault(fight) ?? [];
                if (coffers.Count == 0)
                    coffers = [chances.Keys.ToList()];

                // Objets vus seulement dans le coffre avec chances : on les ajoute au premier coffre.
                var known = coffers.SelectMany(c => c).ToHashSet();
                coffers[0].AddRange(chances.Keys.Where(id => !known.Contains(id)));

                for (var i = 0; i < coffers.Count; i++)
                {
                    var lootChest = new LootChest
                    {
                        Kind = ChestKind.BossCoffer,
                        Boss = Boss(fight),
                        Number = coffers.Count > 1 ? i + 1 : 0,
                    };
                    foreach (var itemId in coffers[i])
                    {
                        if (GetItem(itemId) is not { } item)
                            continue;
                        var chance = chances.GetValueOrDefault(itemId);
                        NoteFeatured(item, new LootSource(LootSourceKind.Chest, lootChest, null, chance));
                        lootChest.Entries.Add(new LootEntry(item, chance));
                    }
                    AddChest(duty, lootChest);
                }
            }

            // Coffres au trésor placés dans la mission.
            var treasureNo = 0;
            foreach (var chest in chestsByDuty[cfcId].OrderBy(c => c.ChestNo))
            {
                if (chest.DungeonBossId != 0 && bossByRowId.TryGetValue(chest.DungeonBossId, out var boss) && boss.ContentFinderConditionId == cfcId)
                    continue;

                var entries = chestItemsByChest[chest.RowId]
                    .GroupBy(i => i.ItemId)
                    .Select(g => (ItemId: g.Key, Chance: g.Max(i => i.Probability)))
                    .ToList();
                if (entries.Count == 0)
                    continue;

                Vector2? coordinates = maps.GetRowOrDefault(chest.MapId) is { } map
                    ? MapUtil.WorldToMap(new Vector2(chest.Position.X, chest.Position.Z), map)
                    : null;
                var lootChest = new LootChest { Kind = ChestKind.TreasureChest, Number = ++treasureNo, MapCoordinates = coordinates };
                foreach (var (itemId, chance) in entries)
                {
                    if (GetItem(itemId) is not { } item)
                        continue;
                    NoteFeatured(item, new LootSource(LootSourceKind.Chest, lootChest, null, chance));
                    lootChest.Entries.Add(new LootEntry(item, chance));
                }
                AddChest(duty, lootChest);
            }

            // Objets lâchés directement (cartes Triple Triad...) ou obtenus ailleurs dans la mission.
            var other = new LootChest { Kind = ChestKind.Other };
            foreach (var drop in bossDropsByDuty[cfcId].OrderBy(d => d.FightNo))
            {
                if (GetItem(drop.ItemId) is not { } item || other.Entries.Any(e => e.Item.ItemId == item.ItemId))
                    continue;
                NoteFeatured(item, new LootSource(LootSourceKind.BossDrop, null, Boss(drop.FightNo), null));
                other.Entries.Add(new LootEntry(item, null));
            }
            foreach (var drop in dutyDropsByDuty[cfcId])
            {
                if (GetItem(drop.ItemId) is not { } item || other.Entries.Any(e => e.Item.ItemId == item.ItemId))
                    continue;
                NoteFeatured(item, new LootSource(LootSourceKind.DutyDrop, null, null, null));
                other.Entries.Add(new LootEntry(item, null));
            }
            AddChest(duty, other);

            if (duty.Chests.Count == 0 && featuredSources.Count == 0)
                continue;

            foreach (var (itemId, sources) in featuredSources)
            {
                var featured = new FeaturedLoot(items[itemId], sources);
                (featured.Item.IsRare ? duty.RareItems : duty.TripleTriadCards).Add(featured);
            }
            Comparison<FeaturedLoot> byCategoryThenName = (a, b) => a.Item.Category != b.Item.Category
                ? a.Item.Category.CompareTo(b.Item.Category)
                : string.Compare(a.Item.Name, b.Item.Name, StringComparison.CurrentCulture);
            duty.RareItems.Sort(byCategoryThenName);
            duty.TripleTriadCards.Sort(byCategoryThenName);

            if (instance is { } content)
                AddTomestones(duty, content, tomestones, bossesByDuty[cfcId].Select(b => b.FightNo).DefaultIfEmpty().Max(), Boss);

            duty.Items.AddRange(duty.RareItems.Select(r => r.Item)
                .Concat(duty.TripleTriadCards.Select(c => c.Item))
                .Concat(duty.Chests.SelectMany(c => c.Entries).Select(e => e.Item))
                .DistinctBy(i => i.ItemId));
            duties.Add(duty);
        }

        duties.Sort((a, b) => a.ContentFinderConditionId.CompareTo(b.ContentFinderConditionId));

        var cabinetIds = new Dictionary<uint, uint>();
        foreach (var cabinet in data.Excel.GetSheet<Cabinet>())
        {
            if (items.ContainsKey(cabinet.Item.RowId))
                cabinetIds.TryAdd(cabinet.Item.RowId, cabinet.RowId);
        }

        log.Information($"Loot chargé : {duties.Count} missions, {items.Count} objets.");
        return new LootDatabase(duties, items, cabinetIds);
    }

    /// <summary>Ajoute un coffre sans ses rares ni ses cartes (ils ont leur propre section) ; ignoré s'il devient vide.</summary>
    private static void AddChest(DutySheet duty, LootChest chest)
    {
        chest.Entries.RemoveAll(e => e.Item.HasOwnSection);
        chest.Entries.Sort((a, b) =>
        {
            var c = b.Item.ItemLevel.CompareTo(a.Item.ItemLevel);
            return c != 0 ? c : string.Compare(a.Item.Name, b.Item.Name, StringComparison.CurrentCulture);
        });
        if (chest.Entries.Count > 0)
            duty.Chests.Add(chest);
    }

    private static void AddTomestones(DutySheet duty, InstanceContent content, Dictionary<uint, LootItem> tomestones,
        uint lastFight, Func<uint, BossRef> boss)
    {
        var slots = new[]
        {
            (Id: 1u, Bosses: content.BossCurrencyA, Final: content.FinalBossCurrencyA, Bonus: content.NewPlayerBonusA),
            (Id: 2u, Bosses: content.BossCurrencyB, Final: content.FinalBossCurrencyB, Bonus: content.NewPlayerBonusB),
            (Id: 3u, Bosses: content.BossCurrencyC, Final: content.FinalBossCurrencyC, Bonus: (ushort)0),
        };

        foreach (var (id, bossAmounts, final, bonus) in slots)
        {
            if (!tomestones.TryGetValue(id, out var tomestone))
                continue;

            var amounts = new List<(BossRef, bool, uint)>();
            for (var i = 0; i < bossAmounts.Count; i++)
            {
                if (bossAmounts[i] > 0)
                    amounts.Add((boss((uint)i), false, bossAmounts[i]));
            }
            if (final > 0)
                amounts.Add((boss(lastFight), true, final));

            if (amounts.Count > 0)
                duty.Tomestones.Add(new TomestoneReward { Tomestone = tomestone, Amounts = amounts, NewPlayerBonus = bonus });
        }
    }

    private static LootItem? CreateItem(Lumina.Excel.ExcelSheet<Item> sheet, uint itemId)
    {
        if (sheet.GetRowOrDefault(itemId) is not { } row)
            return null;
        var name = row.Name.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? null : new LootItem { Row = row, Name = name, Category = Categorize(row) };
    }

    private static decimal? Max(decimal? a, decimal? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    private static List<T> LoadCsv<T>(GameData data, IPluginLog log, string resourceName) where T : ICsv, new()
    {
        var rows = CsvLoader.LoadResource<T>(resourceName, true, out var failedLines, out var exceptions,
            data, data.Options.DefaultExcelLanguage);
        foreach (var line in failedLines)
            log.Warning($"{typeof(T).Name} : ligne ignorée \"{line}\"");
        foreach (var exception in exceptions)
            log.Warning(exception, $"{typeof(T).Name} : erreur de lecture");
        return rows;
    }

    private static LootCategory Categorize(Item item)
    {
        var action = item.ItemAction.ValueNullable?.Action.RowId ?? 0;
        return action switch
        {
            ActionCompanion => LootCategory.Minion,
            ActionMount => LootCategory.Mount,
            ActionOrchestrionRoll => LootCategory.Orchestrion,
            ActionTripleTriadCard => LootCategory.TripleTriadCard,
            ActionBuddyEquip => LootCategory.Barding,
            ActionOrnament => LootCategory.FashionAccessory,
            ActionGlasses => LootCategory.Glasses,
            ActionSecretRecipeBook or ActionUnlockLink or ActionFolkloreTome or ActionFieldNotes or ActionFramersKit
                => LootCategory.OtherUnlock,
            _ when item.EquipSlotCategory.RowId != 0 => LootCategory.Equipment,
            _ => LootCategory.Other,
        };
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];
}
