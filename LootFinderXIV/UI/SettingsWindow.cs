using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using LootFinderXIV.Localization;

namespace LootFinderXIV.UI;

public sealed class SettingsWindow : Window
{
    private const string WindowId = "###LootFinderXIVSettings";

    private readonly Configuration config;
    private readonly Action onChanged;

    public SettingsWindow(Configuration config, Action onChanged)
        : base(Strings.SettingsTitle + WindowId, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.config = config;
        this.onChanged = onChanged;
    }

    public override void PreDraw() => WindowName = Strings.SettingsTitle + WindowId;

    public override void Draw()
    {
        var changed = ImGui.Checkbox(Strings.SettingOpenWithDutyFinder, ref config.OpenWithDutyFinder);
        changed |= ImGui.Checkbox(Strings.SettingServerInfoEntry, ref config.ShowServerInfoEntry);
        changed |= ImGui.Checkbox(Strings.SettingHideObtained, ref config.HideObtained);

        if (!changed)
            return;
        config.Save();
        onChanged();
    }
}
