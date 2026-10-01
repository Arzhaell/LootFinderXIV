# LootFinderXIV

*[English](README.md) · Français*

Plugin Dalamud pour FINAL FANTASY XIV qui affiche, pour chaque mission, une **fiche façon base de données** :

1. **Récompenses rares** (mascottes, montures, rouleaux d'orchestrion) : obtenues ou non, et où les trouver.
2. **Mémoquartz et gils** : quantité par boss, total, bonus quand un joueur découvre la mission.
3. **Un bloc par coffre** : chaque coffre de boss, puis chaque coffre au trésor (avec ses coordonnées sur la
   carte), avec l'équipement qu'il contient, sa chance d'apparition ou son niveau d'objet, et son statut.
4. **Autres objets** : cartes Triple Triad et objets lâchés directement.

La fiche est une fenêtre native, au style du jeu (bibliothèque [KamiToolKit](https://github.com/MidoriKami/KamiToolKit)).

## Installation

1. En jeu, ouvrir `/xlsettings` → **Expérimental**.
2. Dans **Dépôts de plugins personnalisés**, ajouter l'adresse ci-dessous, cocher **Activé**, puis **Enregistrer** :
   ```
   https://raw.githubusercontent.com/Arzhaell/LootFinderXIV/main/repo.json
   ```
3. Ouvrir `/xlplugins`, chercher **LootFinderXIV** et l'installer. Les mises à jour sont ensuite installées par Dalamud.

## Utilisation

- **Toutes les missions** : **`/lootfinder`** (ou `/lfind`, ou le bouton du plugin dans Dalamud) liste tous les
  donjons, défis, raids… groupés par type, avec le nombre de récompenses rares obtenues (**x/x**). Un clic sur une
  mission ouvre sa fiche. « Masquer les complètes » ne garde que les missions où il reste des rares à obtenir.
- **Outil de mission / outil de recherche de raid** : la fiche s'ouvre à côté et suit la mission sélectionnée,
  puis se referme avec lui (désactivable).
- **Pendant une mission** : **« Butin 1/2 »** dans la barre d'infos serveur (en haut à droite) : clic gauche pour la
  fiche, clic droit pour la liste de toutes les missions.
- **`/lootfinder fiche`** : ouvre / ferme la fiche de la mission sélectionnée ou en cours. **`/lootfinder config`** :
  paramètres.

L'interface est en français ou en anglais, selon la langue choisie dans les réglages de Dalamud.

Dans la fiche : survol de l'icône = infobulle du jeu ; clic droit sur un objet = lien dans le chat, essayer,
forcer le statut « obtenu ». Case « Masquer les obtenus » pour ne garder que ce qu'il reste à obtenir.

## Comment le statut « obtenu » est déterminé

- **Mascottes, montures, orchestrion, cartes…** : état de déblocage du jeu, toujours exact.
- **Équipement et autres** : le jeu ne garde pas d'historique. LootFinderXIV mémorise, par personnage, les objets vus
  dans l'inventaire, l'arsenal, l'équipement porté, les sacoches, le coffre à apparences et l'armoire (ces deux
  derniers une fois ouverts). Un objet revendu avant l'installation n'est pas détecté : clic droit pour le marquer.

## Compiler

Prérequis : SDK .NET 10 et XIVLauncher/Dalamud (le SDK Dalamud trouve les DLL dans
`%APPDATA%\XIVLauncher\addon\Hooks\dev`).

KamiToolKit est un sous-module Git (figé sur le commit `6b7b191`) : cloner avec `--recursive`, ou récupérer le
sous-module après coup.

```bash
git submodule update --init
```

```bash
dotnet build -c Release
```

Si une mise à jour de Dalamud casse la compilation de KamiToolKit, passer le sous-module sur une version plus
récente (`git -C KamiToolKit pull origin main`) puis committer le nouveau commit du sous-module.

Plugin de dev : `LootFinderXIV\bin\Release\LootFinderXIV.dll`.

## Données et licences

- Coffres et chances d'apparition : [LuminaSupplemental](https://github.com/Critical-Impact/LuminaSupplemental)
  (données communautaires, GPL-3.0).
- Mémoquartz, gils, noms, icônes, déblocages : fichiers du jeu (feuilles `InstanceContent`, `TomestonesItem`...).
- KamiToolKit : MIT.

LootFinderXIV est distribué sous licence **GPL-3.0** (voir `LICENSE`), comme l'exige LuminaSupplemental.
