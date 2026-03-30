using System;
using System.Collections.ObjectModel;
using System.Linq;
using AnimeDiscover.Models;

// Gère les opérations de la liste d'animes utilisateur.
namespace AnimeDiscover.Services
{
    public class AnimeListController
    {
        private readonly UserDataService _userDataService;
        private readonly MainController _mainController;
        private readonly IJikanService _jikanService;

        public ObservableCollection<Datum> UserAnimes { get; } = new();

        public AnimeListController(UserDataService userDataService, MainController mainController, IJikanService jikanService)
        {
            _userDataService = userDataService;
            _mainController = mainController;
            _jikanService = jikanService;
        }

        // Charge la liste d'animes enregistrés par l'utilisateur.
        public async void LoadUserAnimesAsync()
        {
            UserAnimes.Clear();

            var allUserData = _userDataService.GetAllUserData();

            foreach (var userData in allUserData)
            {
                var anime = await _jikanService.GetAnimeByIdAsync(userData.AnimeId);
                if (anime != null)
                {
                    anime.IsWatched = userData.IsWatched;
                    anime.UserScore = userData.UserScore;
                    anime.EpisodesWatched = userData.EpisodesWatched;
                    UserAnimes.Add(anime);
                }
            }
        }

        // Met à jour les données utilisateur d'un anime de la liste.
        public void UpdateAnimeData(Datum anime, bool isWatched, int? userScore, int? episodesWatched)
        {
            _userDataService.SaveUserData(anime.Id, isWatched, userScore, episodesWatched);
            anime.IsWatched = isWatched;
            anime.UserScore = userScore;
            anime.EpisodesWatched = episodesWatched;
        }

        // Ouvre la page détail pour un anime de la liste.
        public void SelectAnime(Datum anime)
        {
            _mainController.ShowAnimeDetails(anime);
        }

        // Synchronise la liste après une modification dans la page détail.
        public void SyncFromAnimeDetails(Datum anime)
        {
            if (anime == null)
            {
                return;
            }

            var existing = UserAnimes.FirstOrDefault(x => x.Id == anime.Id);

            if (anime.IsWatched)
            {
                if (existing == null)
                {
                    UserAnimes.Add(anime);
                }
                else
                {
                    existing.IsWatched = true;
                    existing.UserScore = anime.UserScore;
                    existing.EpisodesWatched = anime.EpisodesWatched;
                }

                return;
            }

            if (existing != null)
            {
                UserAnimes.Remove(existing);
            }
        }

        // Retourne à la page d'accueil.
        public void GoBack()
        {
            _mainController.ShowHome();
        }
    }
}
