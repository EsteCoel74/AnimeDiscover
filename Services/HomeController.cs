using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AnimeDiscover.Models;

namespace AnimeDiscover.Services
{
    public class HomeController
    {
        private readonly IJikanService _jikanService;
        private readonly MainController _mainController;
        private readonly UserDataService _userDataService;

        public ObservableCollection<Datum> Animes { get; } = new();
        public ObservableCollection<string> Genres { get; } = new();
        public ObservableCollection<string> Types { get; } = new();
        public ObservableCollection<Datum> SearchSuggestions { get; } = new();

        private List<Datum> _allAnimes = new();

        public HomeController(IJikanService jikanService, MainController mainController, UserDataService userDataService)
        {
            _jikanService = jikanService;
            _mainController = mainController;
            _userDataService = userDataService;

            InitializeFilters();
        }

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

        public async Task SearchAndApplyAsync(string query, string selectedGenre, string selectedType)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ApplyFilters(selectedGenre, selectedType);
                return;
            }

            var results = await SearchWithPromptAsync(query);
            _allAnimes = results ?? new List<Datum>();
            ApplyFilters(selectedGenre, selectedType);
        }

        public async Task SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                SearchSuggestions.Clear();
                return;
            }

            var results = await SearchWithPromptAsync(query);
            SearchSuggestions.Clear();
            foreach (var anime in results.Take(5))
            {
                SearchSuggestions.Add(anime);
            }
        }

        private async Task<List<Datum>> SearchWithPromptAsync(string query)
        {
            var info = ExtractSearchInfo(query);
            var collected = new List<Datum>();

            var criteriaResults = await _jikanService.SearchAnimeByCriteriaAsync(new AnimeApiCriteria
            {
                q = string.IsNullOrWhiteSpace(info.KeywordQuery) ? query : info.KeywordQuery,
                type = info.Type,
                status = info.Status,
                genre_ids = info.GenreIds,
                order_by = "score",
                sort = "desc",
                limit = 25
            });

            foreach (var anime in criteriaResults.Where(a => a != null))
            {
                if (collected.All(x => x.mal_id != anime.mal_id))
                {
                    collected.Add(anime);
                }
            }

            foreach (var q in BuildQueries(query, info))
            {
                var results = await _jikanService.SearchAnimeAsync(q);
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

            return collected
                .OrderByDescending(a => GetRelevanceScore(a, info))
                .ThenByDescending(a => a.score ?? 0)
                .Take(25)
                .ToList();
        }

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

        private static int GetRelevanceScore(Datum anime, SearchInfo info)
        {
            var text = $"{anime?.title} {anime?.title_english} {anime?.synopsis} {string.Join(' ', anime?.Genres ?? new List<string>())}".ToLowerInvariant();
            var score = 0;

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

        private sealed class SearchInfo
        {
            public string? Type { get; set; }
            public string? Status { get; set; }
            public string? KeywordQuery { get; set; }
            public List<int> GenreIds { get; } = new();
            public List<string> RequiredTerms { get; } = new();
            public List<string> SeedQueries { get; } = new();
        }

        public void SelectAnime(Datum anime)
        {
            _mainController.ShowAnimeDetails(anime);
        }

        public void ApplyFilters(string selectedGenre, string selectedType)
        {
            var filtered = _allAnimes.AsEnumerable();

            if (!string.IsNullOrEmpty(selectedGenre) && selectedGenre != "Tous les genres")
            {
                filtered = filtered.Where(a => a.Genres != null && a.Genres.Contains(selectedGenre));
            }

            if (!string.IsNullOrEmpty(selectedType) && selectedType != "Tous les types")
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

        public void OpenMyList()
        {
            _mainController.ShowAnimeList();
        }

        private void ApplyFiltersAndUpdate()
        {
            ApplyFilters(null, null);
        }

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

        private void InitializeFilters()
        {
            Genres.Add("Tous les genres");
            Types.Add("Tous les types");
        }

        private void ApplyUserData(Datum anime)
        {
            var userData = _userDataService.GetUserData(anime.Id);
            anime.IsWatched = userData.IsWatched;
            anime.UserScore = userData.UserScore;
        }

    }
}
