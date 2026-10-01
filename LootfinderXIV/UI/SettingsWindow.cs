using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace LootfinderXIV.UI;

public sealed class SettingsWindow : Window
{
    private readonly Configuration config;
    private readonly Action onChanged;

    public SettingsWindow(Configuration config, Action onChanged)
        : base("LootfinderXIV : paramètres###LootfinderXIVSettings", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.config = config;
        this.onChanged = onChanged;
    }

    public override void Draw()
    {
        var changed = ImGui.Checkbox("Ouvrir la fiche avec l'outil de mission", ref config.OpenWithDutyFinder);
        changed |= ImGui.Checkbox("Afficher « Butin » dans la barre d'infos serveur", ref config.ShowServerInfoEntry);
        changed |= ImGui.Checkbox("Masquer les objets déjà obtenus", ref config.HideObtained);

        if (!changed)
            return;
        config.Save();
        onChanged();
    }
}
