# Simkl pour Emby — édition « parité Trakt »

[![license](https://img.shields.io/github/license/Guiro28/Emby.svg?style=flat-square)][license]

Plugin de suivi **Simkl** pour **Emby**. C'est un fork du plugin officiel
[SIMKL/Emby](https://github.com/SIMKL/Emby), étendu pour offrir **les mêmes
fonctionnalités que le plugin [Trakt pour Emby](https://github.com/MediaBrowser/trakt)**.

Le plugin d'origine se limitait à marquer un média « vu » lorsqu'un certain
pourcentage était atteint. Cette version fait une **synchronisation complète et
bidirectionnelle** entre Emby et Simkl.

---

## Sommaire
- [Fonctionnalités](#fonctionnalités)
- [Installation](#installation)
- [Configuration](#configuration)
- [Tâches planifiées](#tâches-planifiées)
- [Fonctionnement détaillé](#fonctionnement-détaillé)
- [Choix de conception](#choix-de-conception)
- [Compilation](#compilation-depuis-les-sources)
- [Architecture](#architecture)
- [Crédits](#crédits)

---

## Fonctionnalités

| Fonction | Plugin d'origine | Ce fork |
|----------|:---------------:|:------:|
| Scrobbling temps réel **start / pause / stop** (statut « en cours ») | ❌ | ✅ |
| **Reprise de lecture** : les sessions en pause Simkl → position dans Emby | ❌ | ✅ |
| Marquer **vu / non-vu** manuellement → poussé vers Simkl | ❌ | ✅ |
| Tâche planifiée **Sync library to Simkl** (Emby → Simkl) | ❌ | ✅ |
| Tâche planifiée **Import playstates from Simkl** (Simkl → Emby) | ❌ | ✅ |
| Synchronisation des **notes** (films & séries) | ❌ | ✅ |
| Ajout des titres non vus à **« Plan to watch »** | ❌ | ✅ |
| **Multi-utilisateurs** + **dossiers exclus** par utilisateur | partiel | ✅ |
| Connexion par **PIN** (sans mot de passe) | ✅ | ✅ |
| Repli par **nom de fichier** si l'identifiant manque | ✅ | ✅ (conservé) |

Compatible **Emby 4.7+** (testé sur 4.10). La clé API Simkl est intégrée au
plugin : **aucune application développeur à créer**.

---

## Installation

### Depuis la release (recommandé)
1. Télécharger `Simkl.dll` depuis la [dernière release](https://github.com/Guiro28/Emby/releases).
2. Le copier dans le dossier des plugins Emby :
   - **Windows** : `%AppData%\Emby-Server\programdata\plugins\`
   - **Linux** : `/var/lib/emby/plugins/`
   - **Docker / Unraid** : `.../appdata/emby/plugins/` (côté conteneur : `/config/plugins/`)
3. Redémarrer le serveur Emby.
4. Tableau de bord → **Extensions (Plugins)** → **Simkl TV Tracker** → **Paramètres**.

> 💡 **Après une mise à jour du plugin**, faites un **rechargement forcé** du
> navigateur (Ctrl + Shift + R) sur la page des paramètres : Emby met la page de
> configuration en cache de façon agressive.

### Cas Docker / Unraid
Si le partage `appdata` est monté en **lecture seule**, déposez `Simkl.dll` sur un
partage inscriptible puis, depuis le terminal Unraid :
```bash
cp /mnt/user/<partage>/Simkl.dll /mnt/user/appdata/emby/plugins/ \
  && chmod 644 /mnt/user/appdata/emby/plugins/Simkl.dll
```
puis redémarrez le conteneur Emby.

---

## Configuration

Dans **Extensions → Simkl TV Tracker → Paramètres** :

1. Sélectionnez l'utilisateur Emby à configurer.
2. Cliquez sur **Log In**, ouvrez le lien (le PIN est prérempli), validez l'accès
   sur Simkl, puis revenez sur la page. Votre nom de profil Simkl s'affiche une
   fois connecté.
3. Réglez les options puis **Enregistrer**.

### Options (par utilisateur)
| Option | Rôle |
|--------|------|
| **Scrobble Movies / TV Shows** | Envoi de la lecture en temps réel. |
| **Watched threshold (%)** | Pourcentage à partir duquel un média compte comme vu. |
| **Export watched status** | Pousse l'état vu (tâche planifiée + bascule manuelle). |
| **Import resume points** | Recrée les points de reprise depuis Simkl. |
| **Sync ratings** | Notes des films et séries (Simkl ne gère pas saison/épisode). |
| **Plan to watch** | Ajoute les titres présents mais non vus à la liste « à voir ». |
| **Don't mark unwatched…** | *(Recommandé coché)* évite qu'Emby efface l'état vu local quand un titre est absent de Simkl. |
| **Extra logging** | Journalisation détaillée pour le diagnostic. |
| **Excluded folders** | Dossiers de bibliothèque ignorés pour cet utilisateur. |

---

## Tâches planifiées

Tableau de bord → **Tâches planifiées**, catégorie **Simkl** :

- **Sync library to Simkl** — envoie vers Simkl l'état vu, les notes et
  (en option) la watchlist.
- **Import playstates from Simkl** — rapatrie dans Emby l'état vu, le nombre de
  lectures, les dates et surtout les **points de reprise**.

Définissez leur fréquence (par ex. quotidienne). Le scrobbling temps réel, lui,
fonctionne automatiquement dès qu'une lecture démarre ou s'arrête.

---

## Fonctionnement détaillé

- **Scrobbling** : au démarrage d'une lecture → `/scrobble/start` (statut « en
  cours ») ; à l'arrêt → `/scrobble/stop` si terminé (≥ 80 % = marqué vu par
  Simkl), sinon `/scrobble/pause` qui **enregistre un point de reprise**.
- **Bascule manuelle** : cocher « lu » / « non lu » dans Emby pousse vers
  `/sync/history` ou `/sync/history/remove`.
- **Export** (tâche) : les films/épisodes lus localement et absents de Simkl sont
  ajoutés à l'historique ; les notes et la watchlist suivent selon les options.
- **Import** (tâche) : lit `/sync/all-items` et `/sync/playback` pour reporter
  dans Emby l'état vu, les notes et les reprises.

> Si un épisode est marqué « vu » dans Emby **sans date de lecture**, Simkl
> l'horodate au moment de l'envoi : il apparaît alors dans l'historique du jour.
> C'est normal (Emby n'a pas conservé la date d'origine).

---

## Choix de conception

- **« Collection » Trakt ignorée** : Simkl n'a pas d'équivalent à la notion de
  média *possédé* (distincte de *vu*). Elle est donc volontairement omise.
- **Aucune suppression côté Simkl** : contrairement au plugin Trakt, la tâche
  d'export **n'efface jamais** d'historique sur Simkl, pour éviter toute perte de
  données. Pour dé-marquer un titre, utilisez le site Simkl.

---

## Compilation depuis les sources

Prérequis : **.NET SDK** (8.x convient). Cible : `netstandard2.0`.
```bash
cd Simkl-Emby
dotnet build -c Release
```
Le plugin est produit dans `Simkl-Emby/bin/Release/netstandard2.0/Simkl.dll`.

---

## Architecture

```
Simkl-Emby/
  Enums.cs                     MediaStatus (start / pause / stop)
  Plugin.cs                    déclaration du plugin + pages de config
  Configuration/               UserConfig, PluginConfiguration, configPage.html + configPage.js
  API/
    SimklApi.cs                scrobble, history(+remove), ratings, add-to-list, all-items, playback
    Objects/                   payloads d'envoi (SyncItems, ScrobblePayloads) + lecture (AllItems, PlaybackSession)
    Responses/                 réponses OAuth / history
    ServerEndpoint.cs          routes /Simkl/oauth/... utilisées par la page de config
  Helpers/                     UserHelper, Match (Emby ↔ Simkl), SyncHelper (ids, CanSync, découpage)
  Services/Scrobbler.cs        ServerMediator : événements de lecture + bascule vu/non-vu
  ScheduledTasks/              SyncToSimklTask (export) + SyncFromSimklTask (import)
```

Endpoints Simkl utilisés : `/oauth/pin`, `/scrobble/{start,pause,stop}`,
`/sync/history` (+`/remove`), `/sync/ratings`, `/sync/add-to-list`,
`/sync/all-items`, `/sync/playback`, `/users/settings`.

---

## Crédits
- Plugin d'origine : [SIMKL/Emby](https://github.com/SIMKL/Emby) (David Davó).
- Modèle de fonctionnalités : [Trakt pour Emby](https://github.com/MediaBrowser/trakt).

## Liens
- Bugs & demandes : https://github.com/Guiro28/Emby/issues
- Simkl : https://simkl.com/ · Discord Simkl : https://discord.gg/JRtwsfG

[license]: https://github.com/Guiro28/Emby/blob/master/LICENSE
