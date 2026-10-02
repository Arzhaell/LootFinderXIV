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

        ImGui.Spacing();
        ImGui.SetNextItemWidth(250.0f * ImGui.GetIO().FontGlobalScale);
        if (ImGui.BeginCombo(Strings.SettingLanguage, LanguageLabel(config.Language)))
        {
            foreach (var code in new[] { "auto", "fr", "en" })
            {
                if (ImGui.Selectable(LanguageLabel(code), config.Language == code) && config.Language != code)
                {
                    config.Language = code;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }

        if (!changed)
            return;
        config.Save();
        onChanged();
    }

    // Les noms des langues restent dans leur propre langue, pour qu'on s'y retrouve quelle que soit l'interface.
    private static string LanguageLabel(string code) => code switch
    {
        "fr" => "Français",
        "en" => "English",
        _ => Strings.LanguageAuto,
    };
}
