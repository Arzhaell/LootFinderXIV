# LootFinderXIV

Plugin Dalamud pour FINAL FANTASY XIV qui affiche, pour chaque mission, une **fiche façon base de données** :

1. **Récompenses rares** (mascottes, montures, rouleaux d'orchestrion) : obtenues ou non, et où les trouver.
2. **Mémoquartz et gils** : quantité par boss, total, bonus quand un joueur découvre la mission.
3. **Un bloc par coffre** : chaque coffre de boss, puis chaque coffre au trésor (avec ses coordonnées sur la
   carte), avec l'équipement qu'il contient, sa chance d'apparition ou son niveau d'objet, et son statut.
4. **Autres objets** : cartes Triple Triad et objets lâchés directement.

La fiche est une fenêtre native, au style du jeu (bibliothèque [KamiToolKit](https://github.com/MidoriKami/KamiToolKit)).

## Utilisation

- **Outil de mission / outil de recherche de raid** : la fiche s'ouvre à côté et suit la mission sélectionnée,
  puis se referme avec lui (désactivable).
- **Pendant une mission** : cliquer sur **« Butin 1/2 »** dans la barre d'infos serveur (en haut à droite).
- **`/lootfinder`** (ou `/lfind`) : ouvre / ferme la fiche. **`/lootfinder config`** : paramètres.

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

Plugin de dev : `LootFinderXIV\bin\Release\LootFinderXIV.dll` (déjà ajouté dans la configuration de Dalamud, chargé au
démarrage et rechargé automatiquement à chaque compilation).

## Données et licences

- Coffres et chances d'apparition : [LuminaSupplemental](https://github.com/Critical-Impact/LuminaSupplemental)
  (données communautaires, GPL-3.0).
- Mémoquartz, gils, noms, icônes, déblocages : fichiers du jeu (feuilles `InstanceContent`, `TomestonesItem`...).
- KamiToolKit : MIT.

LootFinderXIV est distribué sous licence **GPL-3.0** (voir `LICENSE`), comme l'exige LuminaSupplemental.
