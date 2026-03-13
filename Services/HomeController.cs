using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

            var results = await _jikanService.SearchAnimeAsync(query);
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

            var results = await _jikanService.SearchAnimeAsync(query);
            SearchSuggestions.Clear();
            foreach (var anime in results.Take(5))
            {
                SearchSuggestions.Add(anime);
            }
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
