using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace Lootfinder;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    /// <summary>Ouvre la fiche à côté de l'outil de mission quand on y sélectionne une mission.</summary>
    public bool OpenWithDutyFinder = true;

    /// <summary>Affiche l'entrée « Butin » dans la barre d'infos serveur.</summary>
    public bool ShowServerInfoEntry = true;

    public bool HideObtained = false;

    /// <summary>Historique par personnage (clé : ContentId).</summary>
    public Dictionary<ulong, CharacterData> Characters = [];

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

[Serializable]
public sealed class CharacterData
{
    /// <summary>Objets de loot déjà vus dans l'inventaire, l'arsenal, les sacoches ou le coffre à apparences.</summary>
    public HashSet<uint> SeenItems = [];

    /// <summary>Contenu de l'armoire lors de sa dernière ouverture.</summary>
    public HashSet<uint> CabinetItems = [];

    /// <summary>Statut forcé à la main (true = obtenu, false = pas obtenu).</summary>
    public Dictionary<uint, bool> ManualOverrides = [];
}
