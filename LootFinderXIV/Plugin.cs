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
using LootFinderXIV.Localization;
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
    private OverviewWindow? overviewWindow;
    private SettingsWindow? settingsWindow;
    private IDtrBarEntry? dtrEntry;
    private DutySheet? lastActiveDuty;

    // Ouverture automatique avec l'outil de mission.
    private bool autoOpened;
    private bool autoOpenDismissed;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Strings.SetLanguage(PluginInterface.UiLanguage);

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

        overviewWindow = new OverviewWindow
        {
            InternalName = "LootFinderXIVList",
            Title = LootWindow.DefaultTitle,
            Size = OverviewWindow.DefaultSize,
            Database = database,
            Ownership = ownership,
            Config = config,
            OnDutySelected = ShowDutyFromList,
        };

        settingsWindow = new SettingsWindow(config, OnSettingsChanged);
        windowSystem.AddWindow(settingsWindow);

        await Framework.RunOnFrameworkThread(() =>
        {
            dtrEntry = DtrBar.Get("LootFinderXIV");
            dtrEntry.OnClick = OnServerInfoClick;
            watcher.Changed += OnDutyChanged;
            ownership.Changed += OnOwnershipChanged;
            UpdateServerInfoEntry();
        });

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = Strings.CommandHelp,
        });
        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand) { ShowInHelp = false });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleOverview;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;
        PluginInterface.LanguageChanged += OnLanguageChanged;
    }

    public async ValueTask DisposeAsync()
    {
        PluginInterface.LanguageChanged -= OnLanguageChanged;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleOverview;
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
        if (overviewWindow != null)
            await overviewWindow.DisposeAsync();

        await KamiToolKitLibrary.DisposeAsync();
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "config":
                ToggleSettings();
                break;
            case "fiche" or "sheet":
                ToggleLootWindow();
                break;
            default:
                ToggleOverview();
                break;
        }
    }

    private void ToggleSettings() => settingsWindow?.Toggle();

    private void ToggleOverview() => overviewWindow?.Toggle();

    private void ToggleLootWindow()
    {
        if (lootWindow == null)
            return;
        autoOpened = false;
        lootWindow.Toggle();
    }

    private void ShowDutyFromList(DutySheet duty)
    {
        if (lootWindow == null)
            return;
        autoOpened = false;
        lootWindow.ShowDuty(duty);
    }

    private void OnServerInfoClick(DtrInteractionEvent interaction)
    {
        // Clic droit, ou aucune mission active : liste de toutes les missions.
        if (interaction.ClickType == MouseClickType.Right || watcher?.ActiveDuty == null)
            ToggleOverview();
        else
            ToggleLootWindow();
    }

    private void OnLanguageChanged(string languageCode)
    {
        Strings.SetLanguage(languageCode);
        Framework.RunOnFrameworkThread(() =>
        {
            UpdateServerInfoEntry();
            lootWindow?.QueueRefresh();
            overviewWindow?.QueueRefresh(rebuild: true);
        });
    }

    private void OnSettingsChanged()
    {
        UpdateServerInfoEntry();
        lootWindow?.QueueRefresh();
        overviewWindow?.QueueRefresh(rebuild: true);
    }

    private void OnOwnershipChanged()
    {
        UpdateServerInfoEntry();
        lootWindow?.QueueRefresh();
        overviewWindow?.QueueRefresh();
    }

    private void OnDutyChanged()
    {
        if (watcher == null || lootWindow == null)
            return;

        UpdateServerInfoEntry();

        // Nouvelle mission active : la fiche cesse d'afficher la mission choisie dans la liste.
        if (watcher.ActiveDuty != lastActiveDuty)
        {
            lastActiveDuty = watcher.ActiveDuty;
            lootWindow.ClearPinnedDuty();
        }

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

        if (!config.ShowServerInfoEntry)
        {
            dtrEntry.Shown = false;
            return;
        }

        var duty = watcher.ActiveDuty;
        var (obtained, total) = duty != null ? ownership.GetRareProgress(duty) : (0, 0);
        dtrEntry.Text = Strings.ServerInfoText(obtained, total);
        dtrEntry.Tooltip = Strings.ServerInfoTooltip(duty, obtained, total);
        dtrEntry.Shown = true;
    }
}
