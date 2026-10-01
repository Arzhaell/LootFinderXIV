using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using LootFinderXIV.Data;

namespace LootFinderXIV.Services;

/// <summary>
/// Détermine la mission à afficher en sondant régulièrement le jeu : la mission sélectionnée dans
/// l'outil de mission ou de recherche de raid s'il est ouvert, sinon la mission en cours.
/// </summary>
public sealed unsafe class DutyWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly LootDatabase database;
    private DateTime nextPoll = DateTime.MinValue;

    public DutyWatcher(LootDatabase database)
    {
        this.database = database;
        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose() => Plugin.Framework.Update -= OnUpdate;

    /// <summary>Déclenché (sur le thread du jeu) quand l'une des propriétés ci-dessous change.</summary>
    public event Action? Changed;

    /// <summary>Mission à afficher, null si aucune mission connue n'est sélectionnée ni en cours.</summary>
    public DutySheet? ActiveDuty { get; private set; }

    /// <summary>L'outil de mission ou l'outil de recherche de raid est affiché.</summary>
    public bool FinderOpen { get; private set; }

    /// <summary>Le joueur est dans une mission.</summary>
    public bool InDuty { get; private set; }

    private void OnUpdate(IFramework framework)
    {
        if (DateTime.UtcNow < nextPoll)
            return;
        nextPoll = DateTime.UtcNow + PollInterval;

        var currentId = (uint)GameMain.Instance()->CurrentContentFinderConditionId;
        var (finderOpen, selectedId) = ReadFinderSelection();
        var id = finderOpen ? selectedId : currentId;
        var duty = id != 0 ? database.ByContentFinderCondition.GetValueOrDefault(id) : null;
        var inDuty = currentId != 0;

        if (duty == ActiveDuty && finderOpen == FinderOpen && inDuty == InDuty)
            return;

        ActiveDuty = duty;
        FinderOpen = finderOpen;
        InDuty = inDuty;
        Changed?.Invoke();
    }

    private static (bool Open, uint SelectedId) ReadFinderSelection()
    {
        var contentsFinder = AgentContentsFinder.Instance();
        if (contentsFinder != null && contentsFinder->IsAddonShown())
        {
            var selected = contentsFinder->SelectedDuty;
            // Les missions aléatoires n'ont pas de loot propre.
            return (true, selected.ContentType == ContentsType.Regular ? selected.Id : 0);
        }

        var raidFinder = AgentRaidFinder.Instance();
        if (raidFinder != null && raidFinder->IsAddonShown())
        {
            var tabs = raidFinder->Tabs;
            if (raidFinder->SelectedTab < tabs.Length)
            {
                var entries = tabs[(int)raidFinder->SelectedTab].Entries;
                if (raidFinder->SelectedEntry < entries.Length)
                    return (true, entries[(int)raidFinder->SelectedEntry].ContentFinderConditionId);
            }
            return (true, 0);
        }

        return (false, 0);
    }
}
