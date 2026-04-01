# Documentation technique — AnimeDiscover

## 1. Vue d’ensemble

`AnimeDiscover` est une application desktop **WPF** en **.NET 10** (`net10.0-windows`) dédiée à la découverte d’animes via l’API Jikan (MyAnimeList).

Objectifs principaux :
- consulter les animes de saison,
- rechercher et filtrer,
- gérer une liste personnelle (vu, note, épisodes vus),
- obtenir des recommandations IA basées strictement sur les données API,
- personnaliser l’UI (langue, thème, animations).

---

## 2. Stack technique

- **Framework UI** : WPF
- **Langage** : C# (nullable activé)
- **Target** : `.NET 10`
- **Sérialisation** : `System.Text.Json` (principal), `Newtonsoft.Json` référencé
- **HTTP** : `HttpClient`
- **API externe** : `https://api.jikan.moe/v4`
- **Persistance locale** : fichiers JSON dans `%AppData%` / `%LocalAppData%`s

---

## 3. Architecture logique

Architecture de type **MVC/MVVM léger orienté contrôleurs** :

- **Views (`Views/*`)**
  - pages XAML + code-behind UI.
- **Services (`Services/*`)**
  - contrôleurs métier/navigation (`MainController`, `HomeController`, `AnimeController`, `AnimeListController`),
  - accès données (`JikanService`),
  - préférences (`UserDataService`, `ThemeManager`, `UiPreferencesManager`),
  - utilitaires UI (`AppMessageBox`).
- **Models (`Models/*`)**
  - DTO API Jikan (`Datum`, `Root`, etc.),
  - modèles utilitaires (`UserAnimeData`, `AnimeApiCriteria`).

Navigation centralisée par `MainController` via `NavigateAction`.

---

## 4. Démarrage de l’application

1. `App.xaml` charge les dictionnaires de ressources :
   - `Themes/AppTheme.xaml`
   - `Themes/Strings.fr-FR.xaml` (par défaut)
2. `MainWindow` :
   - instancie `MainController`,
   - applique préférences sauvegardées (langue, thème, animations),
   - branche les handlers UI globaux,
   - charge la page d’accueil.

---

## 5. Modules fonctionnels

## 5.1 Accueil / Recherche (`HomeController`, `HomePage`)
Responsabilités :
- chargement des animes de saison (`LoadCurrentSeasonAsync`),
- recherche texte avec variantes de requêtes,
- filtres genre/type,
- suggestions live (debounce dans `MainWindow`),
- fallback API si recherche vide ou pauvre.

Caractéristiques :
- pagination de recherche,
- scoring de pertinence,
- déduplication par `mal_id`.

## 5.2 Liste utilisateur (`AnimeListController`, `AnimeListPage`)
Responsabilités :
- chargement des animes marqués en local (lookup API par ID),
- synchronisation du statut vu / note / épisodes vus,
- navigation vers la fiche détail.

Persistance :
- fichier `%AppData%\AnimeDiscover\userdata.json`.

## 5.3 Détail anime (`AnimeController`, `AnimePage`)
Responsabilités :
- affichage détaillé (métadonnées, genres, thèmes, studios, synopsis, trailer),
- mise à jour statut utilisateur,
- ouverture trailer externe.

Particularité :
- localisation dynamique synopsis/background (cache mémoire + service texte externe),
- libellés UI localisés via ressources.

## 5.4 Assistant IA (`AiConversationPage`)
Responsabilités :
- conversation persistée localement (`%LocalAppData%\AnimeDiscover\ai-conversation-history.json`),
- extraction de critères depuis prompt,
- requêtes API paginées avec budget de requêtes,
- tri + filtrage strict/souple + nouveauté de recommandations.

Pipeline résumé :
1. normalisation du prompt,
2. extraction critères (thèmes, genres, type, statut, score, épisodes, années, source, rating, hints titres),
3. requêtes `SearchAnimeByCriteriaAsync` + `SearchAnimeAsync`,
4. filtrage (`MatchesCriteria`, `HasThemeMatch`, `HasGenreMatch`),
5. scoring (`ComputeRelevanceScore`),
6. sélection finale (`SelectWithNovelty`).

> Note actuelle : le filtrage thèmes est pensé autour des thèmes API Jikan (via `anime.Themes`).

---

## 6. Intégration API Jikan (`IJikanService`, `JikanService`)

Méthodes principales :
- `GetCurrentSeasonAsync()`
- `SearchAnimeAsync(query, page, limit)`
- `SearchAnimeByCriteriaAsync(criteria)`
- `GetAnimeByIdAsync(id)`

Comportements techniques :
- retry exponentiel sur erreurs transitoires / `429`,
- cache mémoire des recherches (`_searchCache`) + cache saison,
- fallback vers recherche texte ou saison en cas d’échec,
- endpoints protégés en `sfw=true`.

---

## 7. Modèle de données (`Models/AnimeData.cs`)

`Datum` est l’entité pivot :
- propriétés brutes API (`title`, `genres`, `themes`, `score`, etc.),
- propriétés facilitatrices `[JsonIgnore]` (`Title`, `ImageUrl`, `Genres`, `Themes`, `TrailerUrl`, etc.),
- état utilisateur local (`IsWatched`, `UserScore`, `EpisodesWatched`).

`AnimeApiCriteria` encapsule les critères de recherche avancée côté API.

---

## 8. Internationalisation (i18n)

Gestion via :
- `UiPreferencesManager.ApplyLanguage(...)`
- dictionnaires `Themes/Strings.fr-FR.xaml` et `Themes/Strings.en-US.xaml`

Bonnes pratiques en place :
- récupération des textes par clé (`UiPreferencesManager.GetText`),
- fallback explicite.

---

## 9. Theming & UX

- `ThemeManager` applique thème clair/sombre en injectant des brushes dynamiques.
- `AppTheme.xaml` centralise styles globaux (`Button`, `TextBox`, `DataGrid`, etc.).
- `AppMessageBox` + `AppMessageBoxWindow` unifient les dialogues.

---

## 10. Persistance locale

- **Données utilisateur anime** : `%AppData%\AnimeDiscover\userdata.json`
- **Préférences UI** : `%AppData%\AnimeDiscover\preferences.json`
- **Historique chat IA** : `%LocalAppData%\AnimeDiscover\ai-conversation-history.json`

Format : JSON indented, tolérant à la casse.

---

## 11. Build / Exécution

Projet :
- `OutputType`: `WinExe`
- `UseWPF`: `true`
- `TargetFramework`: `net10.0-windows`
- `Nullable`: `enable`

Commandes usuelles :
- `dotnet build`
- `dotnet run`
- publication EXE (single-file) possible via `dotnet publish`.

---

## 12. Points d’extension recommandés

- Ajouter tests unitaires pour :
  - parsing prompt IA,
  - scoring/relevance,
  - mapping genres/thèmes.
- Extraire logique IA en service dédié pour réduire le code-behind.
- Ajouter télémétrie interne (temps requêtes, taux fallback, volume cache hit).
- Renforcer filtrage strict des thèmes API (suppression complète des fallbacks texte si nécessaire).

---

## 13. Limites connues

- Dépendance réseau forte (Jikan + service de traduction texte).
- Certaines pages restent orientées code-behind WPF.
- Résilience API présente mais perfectible (circuit breaker, backoff global).