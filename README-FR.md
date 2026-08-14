# Simkl pour Emby — édition « parité Trakt »

Fork du plugin officiel [SIMKL/Emby](https://github.com/SIMKL/Emby) qui ajoute les
fonctionnalités du plugin [Trakt pour Emby](https://github.com/MediaBrowser/trakt).
Le plugin d'origine ne faisait que « marquer comme vu à X % ». Cette version fait une
**synchronisation complète, dans les deux sens**.

## Fonctionnalités

| Fonction | Origine | Ce fork |
|----------|:------:|:------:|
| Scrobbling temps réel **start / pause / stop** (statut « en cours ») | ❌ | ✅ |
| **Reprise** : les sessions en pause Simkl → position de lecture Emby | ❌ | ✅ |
| Marquer vu / non-vu manuellement → poussé vers Simkl | ❌ | ✅ |
| Tâche planifiée **Sync library to Simkl** (Emby → Simkl) | ❌ | ✅ |
| Tâche planifiée **Import playstates from Simkl** (Simkl → Emby) | ❌ | ✅ |
| Sync des **notes** (films & séries) | ❌ | ✅ |
| Ajout des titres non vus à **« Plan to watch »** | ❌ | ✅ |
| Multi-utilisateurs, **dossiers exclus** par utilisateur | partiel | ✅ |
| Login par PIN (sans mot de passe) | ✅ | ✅ |
| Recherche par nom de fichier si l'ID manque | ✅ | ✅ (conservé) |

> **« Collection »** : Simkl n'a pas d'équivalent à la collection Trakt (posséder ≠ vu).
> Cette notion est donc volontairement ignorée.

> **Différence assumée avec Trakt** : la tâche d'export **n'efface jamais** d'historique
> côté Simkl (Trakt le fait). C'est pour éviter toute perte de données accidentelle.
> Pour dé-marquer un titre, utilisez le site Simkl.

## Prérequis
- Emby Server 4.7+ (API `MediaBrowser.Server.Core` 4.7).
- Aucune application développeur Simkl à créer : la clé API est intégrée au plugin.

## Compilation
```bash
cd Simkl-Emby
dotnet build -c Release
```
Le plugin est produit dans `Simkl-Emby/bin/Release/netstandard2.0/Simkl.dll`
(une copie prête à l'emploi est dans `dist/Simkl.dll`).

## Installation
1. Copier `Simkl.dll` dans le dossier plugins d'Emby :
   - Windows : `%AppData%\Emby-Server\programdata\plugins\`
   - Linux : `/var/lib/emby/plugins/`
2. Redémarrer le serveur Emby.
3. Tableau de bord → Extensions (Plugins) → **Simkl TV Tracker** → Paramètres.
4. Choisir l'utilisateur, cliquer **Log In**, ouvrir le lien (le PIN est prérempli),
   autoriser sur Simkl, revenir sur la page (rafraîchir si besoin).
5. Régler les options et **Enregistrer**.

## Options (par utilisateur)
- **Scrobble Movies / TV Shows** : envoi temps réel de la lecture.
- **Watched threshold (%)** : seuil pour considérer un média comme vu.
- **Export watched status** : pousse l'état vu (tâche + bascule manuelle).
- **Import resume points** : recrée les points de reprise depuis Simkl.
- **Sync ratings** : notes films & séries (Simkl ne gère pas saison/épisode).
- **Plan to watch** : ajoute les titres en bibliothèque non vus à la liste « à voir ».
- **Don't mark unwatched…** : (recommandé coché) évite qu'Emby efface l'état vu local
  quand un titre n'est pas vu sur Simkl.
- **Excluded folders** : dossiers ignorés pour cet utilisateur.

## Tâches planifiées
Tableau de bord → Planificateur (Scheduled Tasks), catégorie **Simkl** :
- **Sync library to Simkl** — envoie vu + notes (+ watchlist) vers Simkl.
- **Import playstates from Simkl** — ramène vu, nb de lectures, dates et **reprises**
  dans Emby.

Définissez leur fréquence (ex. quotidienne). Le scrobbling temps réel, lui, est
automatique dès qu'une lecture démarre/s'arrête.

## Architecture
```
Simkl-Emby/
  Enums.cs                         MediaStatus (start/pause/stop)
  Configuration/                   UserConfig (options) + PluginConfiguration + configPage.html
  API/
    SimklApi.cs                    scrobble, history(+remove), ratings, add-to-list, all-items, playback
    Objects/                       payloads (SyncItems, ScrobblePayloads) + lecture (AllItems, PlaybackSession)
    Responses/                     réponses OAuth / history
    ServerEndpoint.cs              routes /Simkl/oauth/... utilisées par la page de config
  Helpers/                         UserHelper, Match (Emby↔Simkl), SyncHelper (ids, CanSync, chunk)
  Services/Scrobbler.cs            ServerMediator : événements lecture + bascule vu/non-vu
  ScheduledTasks/                  SyncToSimklTask (export) + SyncFromSimklTask (import)
```
