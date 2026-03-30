using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AnimeDiscover.Models;

// Gère le chargement, la recherche et le filtrage des animes sur la page d'accueil.
namespace AnimeDiscover.Services
{
    public class HomeController
    {
        private const int MaxSearchPages = 4;
        private const int MaxSearchResults = 120;

        private readonly IJikanService _jikanService;
        private readonly MainController _mainController;
        private readonly UserDataService _userDataService;

        public ObservableCollection<Datum> Animes { get; } = new();
        public ObservableCollection<string> Genres { get; } = new();
        public ObservableCollection<string> Types { get; } = new();
        public ObservableCollection<Datum> SearchSuggestions { get; } = new();

        private List<Datum> _allAnimes = new();

        // Initialise le contrôleur d'accueil avec les services nécessaires.
        public HomeController(IJikanService jikanService, MainController mainController, UserDataService userDataService)
        {
            _jikanService = jikanService;
            _mainController = mainController;
            _userDataService = userDataService;

            InitializeFilters();
        }

        // Charge les animes de la saison actuelle (avec fallback API).
        public async Task LoadCurrentSeasonAsync()
        {
            try
            {
                var animes = await _jikanService.SearchAnimeByCriteriaAsync(new AnimeApiCriteria
                {
                    status = "airing",
                    order_by = "score",
                    sort = "desc",
                    limit = 24
                });

                if (animes == null || animes.Count == 0)
                {
                    animes = await _jikanService.SearchAnimeByCriteriaAsync(new AnimeApiCriteria
                    {
                        order_by = "popularity",
                        sort = "desc",
                        limit = 24
                    });
                }
                
                if (animes != null && animes.Count > 0)
                {
                    _allAnimes = animes;
                }
                else
                {
                    _allAnimes = await _jikanService.GetCurrentSeasonAsync();
                }

                LoadGenresAndTypes();
                ApplyFiltersAndUpdate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement: {ex.Message}");
                _allAnimes = await _jikanService.GetCurrentSeasonAsync();
                LoadGenresAndTypes();
                ApplyFiltersAndUpdate();
            }
        }

        // Exécute une recherche puis applique les filtres actifs.
        public async Task SearchAndApplyAsync(string query, string selectedGenre, string selectedType)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                var hasActiveFilterSelection = !IsAllGenreOption(selectedGenre)
                    || !IsAllTypeOption(selectedType);

                if (hasActiveFilterSelection)
                {
                    await LoadRandomByFiltersAsync(selectedGenre, selectedType);
                }
                else
                {
                    ApplyFilters(selectedGenre, selectedType);
                }

                return;
            }

            var normalizedQuery = query.Trim();
            var results = await SearchWithPromptAsync(query);
            _allAnimes = results ?? new List<Datum>();
            ApplyFilters(GetAllGenresLabel(), GetAllTypesLabel());

            var hasActiveFilters = !IsAllGenreOption(selectedGenre)
                || !IsAllTypeOption(selectedType);

            if (hasActiveFilters && Animes.Count == 0 && _allAnimes.Count > 0)
            {
                ApplyFilters(GetAllGenresLabel(), GetAllTypesLabel());
            }

            var hasQueryMatchInDisplayedResults = Animes.Any(a => IsExactTitleMatch(a, normalizedQuery) || IsTitleContains(a, normalizedQuery));
            var hasQueryMatchInSearchResults = _allAnimes.Any(a => IsExactTitleMatch(a, normalizedQuery) || IsTitleContains(a, normalizedQuery));

            if (!hasQueryMatchInDisplayedResults && hasQueryMatchInSearchResults)
            {
                ApplyFilters(GetAllGenresLabel(), GetAllTypesLabel());
            }
        }

        // Alimente les suggestions de recherche en temps réel.
        public async Task SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                SearchSuggestions.Clear();
                return;
            }

            var normalizedQuery = query.Trim();
            var results = await _jikanService.SearchAnimeAsync(normalizedQuery, 1, 10);

            SearchSuggestions.Clear();
            foreach (var anime in results
                .OrderByDescending(a => IsExactTitleMatch(a, normalizedQuery))
                .ThenByDescending(a => IsTitleContains(a, normalizedQuery))
                .Take(8))
            {
                SearchSuggestions.Add(anime);
            }
        }

        // Effectue une recherche enrichie avec variantes de requêtes.
        private async Task<List<Datum>> SearchWithPromptAsync(string query)
        {
            var info = ExtractSearchInfo(query);
            var normalizedQuery = NormalizeText(query);
            var collected = new List<Datum>();

            var queriesToTry = new List<string> { query };
            if (!string.IsNullOrWhiteSpace(info.KeywordQuery) && !string.Equals(info.KeywordQuery, query, StringComparison.OrdinalIgnoreCase))
            {
                queriesToTry.Add(info.KeywordQuery);
            }

            var simplifiedQuery = Regex.Replace(query ?? string.Empty, "[^a-zA-Z0-9\\s]", " ");
            simplifiedQuery = Regex.Replace(simplifiedQuery, "\\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(simplifiedQuery) && queriesToTry.All(q => !string.Equals(q, simplifiedQuery, StringComparison.OrdinalIgnoreCase)))
            {
                queriesToTry.Add(simplifiedQuery);
            }

            var words = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2)
            {
                var compactQuery = string.Join(" ", words.Take(2));
                if (queriesToTry.All(q => !string.Equals(q, compactQuery, StringComparison.OrdinalIgnoreCase)))
                {
                    queriesToTry.Add(compactQuery);
                }
            }

            foreach (var q in queriesToTry)
            {
                var directResults = await FetchPagedSearchAsync(q, maxPages: MaxSearchPages, maxResults: MaxSearchResults);
                foreach (var anime in directResults)
                {
                    if (anime != null && collected.All(x => x.mal_id != anime.mal_id))
                    {
                        collected.Add(anime);
                    }
                }

                if (collected.Count >= MaxSearchResults)
                {
                    break;
                }
            }

            var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (queryTokens.Length >= 2 && !collected.Any(a => HasAllQueryTokensInTitle(a, queryTokens)))
            {
                var fallbackResults = await FetchPagedSearchAsync(queryTokens[0], maxPages: MaxSearchPages, maxResults: MaxSearchResults);
                foreach (var anime in fallbackResults)
                {
                    if (anime != null && collected.All(x => x.mal_id != anime.mal_id))
                    {
                        collected.Add(anime);
                    }
                }
            }

            return collected
                .OrderBy(a => GetTitleMatchPriority(a, normalizedQuery))
                .ThenByDescending(a => GetRelevanceScore(a, info, normalizedQuery))
                .ThenByDescending(a => a.score ?? 0)
                .Take(MaxSearchResults)
                .ToList();
        }

        // Recherche paginée avec déduplication des résultats.
        private async Task<List<Datum>> FetchPagedSearchAsync(string query, int maxPages, int maxResults)
        {
            var allResults = new List<Datum>();
            var safeMaxPages = Math.Max(1, maxPages);
            var safeMaxResults = Math.Max(1, maxResults);

            for (var page = 1; page <= safeMaxPages; page++)
            {
                var pageResults = await _jikanService.SearchAnimeAsync(query, page, 25);
                if (pageResults == null || pageResults.Count == 0)
                {
                    break;
                }

                foreach (var anime in pageResults.Where(a => a != null))
                {
                    if (allResults.All(x => x.mal_id != anime.mal_id))
                    {
                        allResults.Add(anime);
                    }

                    if (allResults.Count >= safeMaxResults)
                    {
                        return allResults;
                    }
                }

                if (pageResults.Count < 25)
                {
                    break;
                }

                await Task.Delay(120);
            }

            return allResults;
        }

        // Extrait les critères de recherche depuis une requête texte libre.
        private static SearchInfo ExtractSearchInfo(string prompt)
        {
            var normalized = (prompt ?? string.Empty).ToLowerInvariant();
            var info = new SearchInfo();

            if (Regex.IsMatch(normalized, "\\b(film|movie)\\b")) info.Type = "movie";
            else if (Regex.IsMatch(normalized, "\\b(ova)\\b")) info.Type = "ova";
            else if (Regex.IsMatch(normalized, "\\b(ona)\\b")) info.Type = "ona";
            else if (Regex.IsMatch(normalized, "\\b(tv|serie|série)\\b")) info.Type = "tv";

            if (Regex.IsMatch(normalized, "\\b(en cours|airing|actuel)\\b")) info.Status = "airing";
            else if (Regex.IsMatch(normalized, "\\b(termin[eé]|fini|complete)\\b")) info.Status = "complete";
            else if (Regex.IsMatch(normalized, "\\b(a venir|à venir|prochain|upcoming)\\b")) info.Status = "upcoming";

            var genreMap = new Dictionary<string, (int id, string canonical)>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = (1, "action"),
                ["aventure"] = (2, "adventure"),
                ["adventure"] = (2, "adventure"),
                ["automobile"] = (3, "cars"),
                ["voiture"] = (3, "cars"),
                ["cars"] = (3, "cars"),
                ["racing"] = (3, "cars"),
                ["comedie"] = (4, "comedy"),
                ["comédie"] = (4, "comedy"),
                ["drame"] = (8, "drama"),
                ["drama"] = (8, "drama"),
                ["fantasy"] = (10, "fantasy"),
                ["horreur"] = (14, "horror"),
                ["horror"] = (14, "horror"),
                ["romance"] = (22, "romance"),
                ["science fiction"] = (24, "sci-fi"),
                ["sci-fi"] = (24, "sci-fi"),
                ["sports"] = (30, "sports"),
                ["sport"] = (30, "sports"),
                ["slice of life"] = (36, "slice of life"),
                ["surnaturel"] = (37, "supernatural"),
                ["supernatural"] = (37, "supernatural")
            };

            foreach (var kvp in genreMap)
            {
                if (normalized.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    if (!info.GenreIds.Contains(kvp.Value.id)) info.GenreIds.Add(kvp.Value.id);
                    if (!info.RequiredTerms.Contains(kvp.Value.canonical)) info.RequiredTerms.Add(kvp.Value.canonical);
                }
            }

            var terms = Regex.Split(normalized, "[^a-zA-Z0-9+\\-]+")
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Where(t => t.Length >= 3)
                .Where(t => !new[] { "anime", "animes", "manga", "genre", "types", "type", "avec", "pour", "dans", "des", "une", "un" }.Contains(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

            foreach (var term in terms)
            {
                if (!info.RequiredTerms.Contains(term))
                {
                    info.RequiredTerms.Add(term);
                }
            }

            info.KeywordQuery = info.RequiredTerms.Count > 0 ? string.Join(" ", info.RequiredTerms.Take(6)) : prompt;

            if (info.GenreIds.Contains(3))
            {
                info.SeedQueries.AddRange(new[] { "Initial D", "MF Ghost", "Wangan Midnight", "Capeta", "Redline" });
            }

            return info;
        }

        // Génère un lot de requêtes à tester à partir du prompt.
        private static IEnumerable<string> BuildQueries(string userPrompt, SearchInfo info)
        {
            var queries = new List<string> { userPrompt, info.KeywordQuery };
            queries.AddRange(info.SeedQueries);
            queries.AddRange(info.RequiredTerms.Take(4));

            return queries
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(15);
        }

        // Calcule un score de pertinence pour trier les résultats.
        private static int GetRelevanceScore(Datum anime, SearchInfo info, string normalizedQuery)
        {
            var text = $"{anime?.title} {anime?.title_english} {anime?.synopsis} {string.Join(' ', anime?.Genres ?? new List<string>())}".ToLowerInvariant();
            var score = 0;
            var query = normalizedQuery;

            if (!string.IsNullOrWhiteSpace(query))
            {
                if (IsExactTitleMatch(anime, query))
                {
                    score += 400;
                }
                else if (IsTitleContains(anime, query))
                {
                    score += 220;
                }

                var queryTokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var titleText = NormalizeText($"{anime?.title} {anime?.title_english} {anime?.Title}");
                score += queryTokens.Count(t => titleText.Contains(t, StringComparison.OrdinalIgnoreCase)) * 35;
            }

            foreach (var t in info.RequiredTerms)
            {
                if (text.Contains(t, StringComparison.OrdinalIgnoreCase))
                {
                    score += 12;
                }
            }

            if (!string.IsNullOrWhiteSpace(info.Type) && string.Equals(anime?.type, info.Type, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            if (!string.IsNullOrWhiteSpace(info.Status) && string.Equals(anime?.status, info.Status, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            if (info.GenreIds.Count > 0)
            {
                var animeGenreIds = (anime?.genres ?? new List<Genre>()).Select(g => g.mal_id).ToHashSet();
                score += info.GenreIds.Count(id => animeGenreIds.Contains(id)) * 14;
            }

            return score;
        }

        // Vérifie si la requête correspond exactement à un titre.
        private static bool IsExactTitleMatch(Datum anime, string query)
        {
            if (anime == null || string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            var normalizedQuery = NormalizeText(query);
            return string.Equals(NormalizeText(anime.title), normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeText(anime.title_english), normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeText(anime.Title), normalizedQuery, StringComparison.OrdinalIgnoreCase);
        }

        // Vérifie si la requête est contenue dans l'un des titres.
        private static bool IsTitleContains(Datum anime, string query)
        {
            if (anime == null || string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            var normalizedQuery = NormalizeText(query);
            var compactQuery = NormalizeCompactText(query);

            var normalizedTitle = NormalizeText(anime.title);
            var normalizedEnglishTitle = NormalizeText(anime.title_english);
            var normalizedDisplayTitle = NormalizeText(anime.Title);

            var compactTitle = NormalizeCompactText(anime.title);
            var compactEnglishTitle = NormalizeCompactText(anime.title_english);
            var compactDisplayTitle = NormalizeCompactText(anime.Title);

            return NormalizeText(anime.title).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || NormalizeText(anime.title_english).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || NormalizeText(anime.Title).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(compactQuery)
                    && (compactTitle.Contains(compactQuery, StringComparison.OrdinalIgnoreCase)
                        || compactEnglishTitle.Contains(compactQuery, StringComparison.OrdinalIgnoreCase)
                        || compactDisplayTitle.Contains(compactQuery, StringComparison.OrdinalIgnoreCase)));
        }

        // Vérifie si tous les mots de la requête existent dans le titre.
        private static bool HasAllQueryTokensInTitle(Datum anime, string[] tokens)
        {
            if (anime == null || tokens == null || tokens.Length == 0)
            {
                return false;
            }

            var titleHaystack = NormalizeText($"{anime.title} {anime.title_english} {anime.Title}");
            return tokens.All(t => titleHaystack.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        // Définit la priorité de correspondance sur les titres.
        private static int GetTitleMatchPriority(Datum anime, string normalizedQuery)
        {
            if (anime == null || string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return 3;
            }

            var titles = new[] { NormalizeText(anime.title), NormalizeText(anime.title_english), NormalizeText(anime.Title) };

            if (titles.Any(t => string.Equals(t, normalizedQuery, StringComparison.OrdinalIgnoreCase)))
            {
                return 0;
            }

            if (titles.Any(t => t.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
            {
                return 1;
            }

            if (titles.Any(t => t.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
            {
                return 2;
            }

            return 3;
        }

        // Normalise une chaîne pour comparaison textuelle.
        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().ToLowerInvariant();
            return Regex.Replace(normalized, "\\s+", " ");
        }

        // Normalise une chaîne en supprimant les séparateurs.
        private static string NormalizeCompactText(string value)
        {
            var normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            return Regex.Replace(normalized, "[^a-z0-9]+", string.Empty);
        }

        private sealed class SearchInfo
        {
            public string? Type { get; set; }
            public string? Status { get; set; }
            public string? KeywordQuery { get; set; }
            public List<int> GenreIds { get; } = new();
            public List<string> RequiredTerms { get; } = new();
            public List<string> SeedQueries { get; } = new();
        }

        // Ouvre la page de détail pour l'anime sélectionné.
        public void SelectAnime(Datum anime)
        {
            _mainController.ShowAnimeDetails(anime);
        }

        // Ajoute rapidement un anime à la liste utilisateur.
        public void AddToMyList(Datum anime)
        {
            if (anime == null)
            {
                return;
            }

            anime.IsWatched = true;
            _userDataService.SaveUserData(anime.mal_id, true, anime.UserScore, anime.EpisodesWatched);
        }

        // Applique les filtres genre/type à la collection affichée.
        public void ApplyFilters(string selectedGenre, string selectedType)
        {
            var filtered = _allAnimes.AsEnumerable();

            if (!string.IsNullOrEmpty(selectedGenre) && !IsAllGenreOption(selectedGenre))
            {
                filtered = filtered.Where(a => a.Genres != null && a.Genres.Contains(selectedGenre));
            }

            if (!string.IsNullOrEmpty(selectedType) && !IsAllTypeOption(selectedType))
            {
                filtered = filtered.Where(a => a.Type == selectedType);
            }

            Animes.Clear();
            foreach (var anime in filtered)
            {
                ApplyUserData(anime);
                Animes.Add(anime);
            }
        }

        // Charge des animes aléatoires correspondant aux filtres actifs.
        public async Task LoadRandomByFiltersAsync(string selectedGenre, string selectedType)
        {
            var hasGenreFilter = !string.IsNullOrWhiteSpace(selectedGenre) && !IsAllGenreOption(selectedGenre);
            var hasTypeFilter = !string.IsNullOrWhiteSpace(selectedType) && !IsAllTypeOption(selectedType);

            if (!hasGenreFilter && !hasTypeFilter)
            {
                ApplyFilters(selectedGenre, selectedType);
                return;
            }

            var collected = new List<Datum>();

            for (var i = 0; i < 3; i++)
            {
                var criteria = new AnimeApiCriteria
                {
                    order_by = "popularity",
                    sort = Random.Shared.Next(0, 2) == 0 ? "asc" : "desc",
                    page = Random.Shared.Next(1, 16),
                    limit = 25
                };

                if (hasTypeFilter)
                {
                    criteria.type = NormalizeTypeFilter(selectedType);
                }

                if (hasGenreFilter && TryMapGenreToId(selectedGenre, out var genreId))
                {
                    criteria.genre_ids.Add(genreId);
                }

                var results = await _jikanService.SearchAnimeByCriteriaAsync(criteria);
                foreach (var anime in results.Where(a => a != null))
                {
                    if (collected.All(x => x.mal_id != anime.mal_id))
                    {
                        collected.Add(anime);
                    }
                }

                if (collected.Count >= 60)
                {
                    break;
                }
            }

            _allAnimes = collected
                .OrderBy(_ => Random.Shared.Next())
                .Take(24)
                .ToList();

            ApplyFilters(GetAllGenresLabel(), GetAllTypesLabel());
        }

        // Retourne le libellé de l'option "tous les genres" dans la langue active.
        private static string GetAllGenresLabel() => UiPreferencesManager.GetText("Genre.All", "Tous les genres");

        // Retourne le libellé de l'option "tous les types" dans la langue active.
        private static string GetAllTypesLabel() => UiPreferencesManager.GetText("Type.All", "Tous les types");

        // Vérifie si la valeur de genre correspond à l'option globale (FR/EN).
        private static bool IsAllGenreOption(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                || string.Equals(value, "Tous les genres", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "All genres", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, GetAllGenresLabel(), StringComparison.OrdinalIgnoreCase);
        }

        // Vérifie si la valeur de type correspond à l'option globale (FR/EN).
        private static bool IsAllTypeOption(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                || string.Equals(value, "Tous les types", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "All types", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, GetAllTypesLabel(), StringComparison.OrdinalIgnoreCase);
        }

        // Ouvre la page "Ma liste".
        public void OpenMyList()
        {
            _mainController.ShowAnimeList();
        }

        // Réapplique les filtres courants sur la collection.
        private void ApplyFiltersAndUpdate()
        {
            ApplyFilters(null, null);
        }

        // Recharge les valeurs disponibles pour les filtres genre/type.
        private void LoadGenresAndTypes()
        {
            var genreSet = new HashSet<string> { "Tous les genres" };
            var typeSet = new HashSet<string> { "Tous les types" };

            foreach (var anime in _allAnimes)
            {
                if (anime.Genres != null)
                {
                    foreach (var genre in anime.Genres)
                    {
                        genreSet.Add(genre);
                    }
                }
                if (!string.IsNullOrEmpty(anime.Type))
                {
                    typeSet.Add(anime.Type);
                }
            }

            Genres.Clear();
            foreach (var genre in genreSet.OrderBy(g => g))
            {
                Genres.Add(genre);
            }

            Types.Clear();
            foreach (var type in typeSet.OrderBy(t => t))
            {
                Types.Add(type);
            }
        }

        // Initialise les valeurs par défaut des filtres.
        private void InitializeFilters()
        {
            Genres.Add("Tous les genres");
            Types.Add("Tous les types");
        }

        // Normalise la valeur de type choisie pour l'API.
        private static string NormalizeTypeFilter(string selectedType)
        {
            if (string.IsNullOrWhiteSpace(selectedType))
            {
                return null;
            }

            var normalized = selectedType.Trim().ToLowerInvariant();
            return normalized switch
            {
                "tv" => "tv",
                "movie" => "movie",
                "ova" => "ova",
                "ona" => "ona",
                "special" => "special",
                _ => normalized
            };
        }

        // Convertit un nom de genre affiché en identifiant API.
        private static bool TryMapGenreToId(string selectedGenre, out int genreId)
        {
            genreId = 0;
            if (string.IsNullOrWhiteSpace(selectedGenre))
            {
                return false;
            }

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Action"] = 1,
                ["Aventure"] = 2,
                ["Adventure"] = 2,
                ["Comédie"] = 4,
                ["Comedie"] = 4,
                ["Comedy"] = 4,
                ["Drame"] = 8,
                ["Drama"] = 8,
                ["Fantasy"] = 10,
                ["Horror"] = 14,
                ["Romance"] = 22,
                ["Sci-Fi"] = 24,
                ["Slice of Life"] = 36,
                ["Sports"] = 30,
                ["Surnaturel"] = 37,
                ["Supernatural"] = 37
            };

            return map.TryGetValue(selectedGenre.Trim(), out genreId);
        }

        // Injecte les données utilisateur sur un anime avant affichage.
        private void ApplyUserData(Datum anime)
        {
            var userData = _userDataService.GetUserData(anime.Id);
            anime.IsWatched = userData.IsWatched;
            anime.UserScore = userData.UserScore;
            anime.EpisodesWatched = userData.EpisodesWatched;
        }

    }
}
