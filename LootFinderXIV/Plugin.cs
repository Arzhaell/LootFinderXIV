using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using KamiToolKit;
using LootFinderXIV.Data;
using LootFinderXIV.Services;
using LootFinderXIV.UI;

namespace LootFinderXIV;

public sealed class Plugin : IAsyncDalamudPlugin
{
    private const string CommandName = "/lootfinder";
    private const string ShortCommandName = "/lfind";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IUnlockState UnlockState { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("LootFinderXIV");
    private Configuration config = null!;
    private OwnershipService? ownership;
    private DutyWatcher? watcher;
    private LootWindow? lootWindow;
    private SettingsWindow? settingsWindow;
    private IDtrBarEntry? dtrEntry;

    // Ouverture automatique avec l'outil de mission.
    private bool autoOpened;
    private bool autoOpenDismissed;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        await KamiToolKitLibrary.InitializeAsync(PluginInterface, "LootFinderXIV");

        var database = await Task.Run(() => LootDatabase.Load(DataManager.GameData, Log), cancellationToken);
        ownership = new OwnershipService(config, database);
        watcher = new DutyWatcher(database);

        lootWindow = new LootWindow
        {
            InternalName = "LootFinderXIVDuty",
            Title = LootWindow.DefaultTitle,
            Size = LootWindow.DefaultSize,
            Watcher = watcher,
            Ownership = ownership,
            Config = config,
            OnClosed = OnLootWindowClosed,
        };

        settingsWindow = new SettingsWindow(config, OnSettingsChanged);
        windowSystem.AddWindow(settingsWindow);

        await Framework.RunOnFrameworkThread(() =>
        {
            dtrEntry = DtrBar.Get("LootFinderXIV");
            dtrEntry.OnClick = _ => ToggleLootWindow();
            watcher.Changed += OnDutyChanged;
            ownership.Changed += OnOwnershipChanged;
            UpdateServerInfoEntry();
        });

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Affiche la fiche de butin de la mission sélectionnée ou en cours. « config » pour les paramètres.",
        });
        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand) { ShowInHelp = false });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleLootWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;
    }

    public async ValueTask DisposeAsync()
    {
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleLootWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettings;
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ShortCommandName);
        windowSystem.RemoveAllWindows();

        await Framework.RunOnFrameworkThread(() =>
        {
            if (watcher != null)
                watcher.Changed -= OnDutyChanged;
            if (ownership != null)
                ownership.Changed -= OnOwnershipChanged;
            dtrEntry?.Remove();
            watcher?.Dispose();
            ownership?.Dispose();
        });

        if (lootWindow != null)
            await lootWindow.DisposeAsync();

        await KamiToolKitLibrary.DisposeAsync();
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("config", System.StringComparison.OrdinalIgnoreCase))
            ToggleSettings();
        else
            ToggleLootWindow();
    }

    private void ToggleSettings() => settingsWindow?.Toggle();

    private void ToggleLootWindow()
    {
        if (lootWindow == null)
            return;
        autoOpened = false;
        lootWindow.Toggle();
    }

    private void OnSettingsChanged()
    {
        UpdateServerInfoEntry();
        lootWindow?.QueueRefresh();
    }

    private void OnOwnershipChanged()
    {
        UpdateServerInfoEntry();
        lootWindow?.QueueRefresh();
    }

    private void OnDutyChanged()
    {
        if (watcher == null || lootWindow == null)
            return;

        UpdateServerInfoEntry();

        if (!watcher.FinderOpen)
        {
            // Outil de mission fermé : on referme la fiche si c'est lui qui l'avait ouverte.
            autoOpenDismissed = false;
            if (autoOpened && lootWindow.IsOpen)
            {
                autoOpened = false;
                lootWindow.Close();
            }
        }
        else if (config.OpenWithDutyFinder && !autoOpenDismissed && !lootWindow.IsOpen && watcher.ActiveDuty != null)
        {
            autoOpened = true;
            lootWindow.Open();
            Framework.RunOnTick(PlaceNextToDutyFinder, delayTicks: 2);
        }

        lootWindow.QueueRefresh();
    }

    private void OnLootWindowClosed()
    {
        // Fermée par le joueur alors que l'outil de mission est ouvert : on ne la rouvre pas tout de suite.
        if (watcher?.FinderOpen == true)
            autoOpenDismissed = true;
        autoOpened = false;
    }

    private void PlaceNextToDutyFinder()
    {
        if (lootWindow is not { IsOpen: true })
            return;
        foreach (var name in new[] { "ContentsFinder", "RaidFinder" })
        {
            var finder = GameGui.GetAddonByName(name);
            if (finder.IsNull || !finder.IsVisible)
                continue;
            lootWindow.SetWindowPosition(new Vector2(finder.X + finder.ScaledWidth + 8.0f, finder.Y));
            return;
        }
    }

    private void UpdateServerInfoEntry()
    {
        if (dtrEntry == null || watcher == null || ownership == null)
            return;

        if (!config.ShowServerInfoEntry || watcher.ActiveDuty is not { } duty)
        {
            dtrEntry.Shown = false;
            return;
        }

        var (obtained, total) = ownership.GetRareProgress(duty);
        dtrEntry.Text = total > 0 ? $"Butin {obtained}/{total}" : "Butin";
        dtrEntry.Tooltip = $"LootFinderXIV : {duty.Name}\n"
            + (total > 0 ? $"Récompenses rares obtenues : {obtained}/{total}\n" : string.Empty)
            + "Cliquer pour afficher la fiche.";
        dtrEntry.Shown = true;
    }
}
