using System.Globalization;
using LootFinderXIV.Data;
using LootFinderXIV.Services;

namespace LootFinderXIV.Localization;

/// <summary>
/// Textes de l'interface, en français ou en anglais selon la langue de Dalamud
/// (les noms d'objets, de missions et de boss viennent du jeu, dans la langue du client).
/// </summary>
public static class Strings
{
    private static bool french;

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Code de langue de Dalamud (« fr », « en », « de », « ja »...) ; tout sauf « fr » donne l'anglais.</summary>
    public static void SetLanguage(string languageCode)
    {
        french = languageCode == "fr";
        Culture = CultureInfo.GetCultureInfo(french ? "fr-FR" : "en-US");
    }

    private static string T(string fr, string en) => french ? fr : en;

    private static string F(string fr, string en, params object[] args) => string.Format(Culture, T(fr, en), args);

    // Fiche

    public static string HideObtained => T("Masquer les obtenus", "Hide obtained");

    public static string NoDutySelected => T(
        "Sélectionnez une mission dans l'outil de mission,\nou entrez dans une mission, pour voir son butin.",
        "Select a duty in the Duty Finder,\nor enter a duty, to see its loot.");

    public static string DutyInfo(DutySheet duty) => duty.ItemLevel > 0
        ? F("{0} · Niv. {1} · iLvl {2}", "{0} · Lv. {1} · iLvl {2}", duty.ContentTypeName, duty.Level, duty.ItemLevel)
        : F("{0} · Niv. {1}", "{0} · Lv. {1}", duty.ContentTypeName, duty.Level);

    public static string RareSectionNone => T("Récompenses rares : aucune", "Rare rewards: none");
    public static string RareSectionComplete(int obtained, int total) => F("Récompenses rares : tout obtenu ({0}/{1})", "Rare rewards: all obtained ({0}/{1})", obtained, total);
    public static string RareSection(int obtained, int total) => F("Récompenses rares : {0}/{1}", "Rare rewards: {0}/{1}", obtained, total);
    public static string NoRareHere => T("Pas de mascotte, monture ni rouleau d'orchestrion ici.", "No minion, mount or orchestrion roll here.");
    public static string WhereToGet => T("Où l'obtenir :", "Where to get it:");

    public static string RewardsSection => T("Mémoquartz et gils", "Tomestones and gil");
    public static string CardSection(int obtained, int total) => F("Cartes Triple Triad : {0}/{1}", "Triple Triad cards: {0}/{1}", obtained, total);
    public static string AllCardsObtained => T("Toutes les cartes sont obtenues.", "All cards are obtained.");
    public static string ClearGil => T("Gils en fin de mission", "Gil on completion");
    public static string NewPlayerBonus(uint amount) => F("Bonus si un joueur découvre la mission : +{0}", "Bonus when a player is new to the duty: +{0}", amount);
    public static string Amount(uint amount) => string.Format(Culture, "×{0:N0}", amount);
    public static string AmountLine(string source, uint amount) => F("{0} : {1}", "{0}: {1}", source, amount);

    public static string ChestHeader(LootChest chest, int obtained, int total) => chest.MapCoordinates is { } c
        ? string.Format(Culture, "{0}  (X {1:0.0} · Y {2:0.0})   {3}/{4}", ChestTitle(chest), c.X, c.Y, obtained, total)
        : string.Format(Culture, "{0}   {1}/{2}", ChestTitle(chest), obtained, total);

    public static string ChestAllObtained => T("Tout est obtenu dans ce coffre.", "Everything in this coffer is obtained.");

    public static string Percent(decimal value) => string.Format(Culture, T("{0:0.#} %", "{0:0.#}%"), value);
    public static string ItemLevel(uint level) => string.Format(Culture, "iLvl {0}", level);

    // Liste de toutes les missions

    public static string OverviewTitle => T("Toutes les missions", "All duties");
    public static string HideCompletedDuties => T("Masquer les complètes", "Hide completed");
    public static string Level(byte level) => F("Niv. {0}", "Lv. {0}", level);
    public static string Progress(int obtained, int total) => total == 0 ? "—" : $"{obtained}/{total}";
    public static string OverviewSection(string contentType, int obtained, int total) => $"{contentType}   {Progress(obtained, total)}";
    public static string OpenSheetTooltip => T("Cliquer pour afficher la fiche.", "Click to show the loot sheet.");

    // Coffres et sources

    public static string BossName(BossRef boss) => boss.Name ?? F("Boss {0}", "Boss {0}", boss.FightNo + 1);

    public static string FinalBoss(BossRef boss) => F("{0} (boss final)", "{0} (final boss)", BossName(boss));

    public static string ChestTitle(LootChest chest) => chest.Kind switch
    {
        ChestKind.BossCoffer when chest.Number > 0 => F("Coffre {0} : {1}", "Coffer {0}: {1}", chest.Number, BossName(chest.Boss!.Value)),
        ChestKind.BossCoffer => F("Coffre : {0}", "Coffer: {0}", BossName(chest.Boss!.Value)),
        ChestKind.TreasureChest => F("Coffre au trésor n°{0}", "Treasure coffer #{0}", chest.Number),
        _ => T("Autres objets", "Other items"),
    };

    public static string Source(LootSource source) => source.Kind switch
    {
        LootSourceKind.Chest when source.Probability is { } p => $"{ChestTitle(source.Chest!)} ({Percent(p)})",
        LootSourceKind.Chest => ChestTitle(source.Chest!),
        LootSourceKind.BossDrop => F("Lâché par {0}", "Dropped by {0}", BossName(source.Boss!.Value)),
        _ => T("Dans la mission", "In the duty"),
    };

    // Objets

    public static string Status(LootItemState state) => state switch
    {
        { Source: ObtainedSource.NotLoggedIn } => string.Empty,
        { Obtained: false } => T("Manquant", "Missing"),
        { Source: ObtainedSource.Unlocked } => T("Débloqué", "Unlocked"),
        { Source: ObtainedSource.Cabinet } => T("Armoire", "Armoire"),
        { Source: ObtainedSource.Held } => T("Possédé", "Owned"),
        _ => T("Obtenu", "Obtained"),
    };

    public static string Category(LootCategory category) => category switch
    {
        LootCategory.Minion => T("Mascotte", "Minion"),
        LootCategory.Mount => T("Monture", "Mount"),
        LootCategory.Orchestrion => T("Orchestrion", "Orchestrion"),
        LootCategory.TripleTriadCard => T("Carte TT", "TT card"),
        LootCategory.Barding => T("Barde", "Barding"),
        LootCategory.FashionAccessory => T("Accessoire", "Accessory"),
        LootCategory.Glasses => T("Lunettes", "Glasses"),
        LootCategory.OtherUnlock => T("Déblocage", "Unlock"),
        LootCategory.Equipment => T("Équipement", "Equipment"),
        _ => T("Divers", "Misc"),
    };

    public static string LinkInChat => T("Lien dans le chat", "Link in chat");
    public static string TryOn => T("Essayer", "Try on");
    public static string MarkObtained => T("Marquer comme obtenu", "Mark as obtained");
    public static string MarkNotObtained => T("Marquer comme non obtenu", "Mark as not obtained");
    public static string AutomaticDetection => T("Revenir à la détection automatique", "Back to automatic detection");

    // Paramètres, commandes, barre d'infos serveur

    public static string SettingsTitle => T("LootFinderXIV : paramètres", "LootFinderXIV: settings");
    public static string SettingOpenWithDutyFinder => T("Ouvrir la fiche avec l'outil de mission", "Open the sheet with the Duty Finder");
    public static string SettingServerInfoEntry => T("Afficher « Butin » dans la barre d'infos serveur", "Show \"Loot\" in the server info bar");
    public static string SettingHideObtained => T("Masquer les objets déjà obtenus", "Hide items already obtained");

    public static string CommandHelp => T(
        "Affiche la liste de toutes les missions. « fiche » : fiche de la mission sélectionnée ou en cours ; « config » : paramètres.",
        "Shows the list of all duties. \"sheet\": loot sheet of the selected or current duty; \"config\": settings.");

    public static string ServerInfoText(int obtained, int total) => total > 0
        ? F("Butin {0}/{1}", "Loot {0}/{1}", obtained, total)
        : T("Butin", "Loot");

    public static string ServerInfoTooltip(DutySheet? duty, int obtained, int total)
    {
        if (duty == null)
            return "LootFinderXIV\n" + T("Cliquer pour voir toutes les missions.", "Click to see all duties.");

        return $"LootFinderXIV: {duty.Name}\n"
            + (total > 0 ? F("Récompenses rares obtenues : {0}/{1}\n", "Rare rewards obtained: {0}/{1}\n", obtained, total) : string.Empty)
            + T("Clic gauche : fiche de la mission\nClic droit : toutes les missions", "Left click: duty loot sheet\nRight click: all duties");
    }
}
