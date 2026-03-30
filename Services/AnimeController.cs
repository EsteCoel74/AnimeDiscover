using AnimeDiscover.Models;

// Orchestre les actions de la page détail d'un anime.
namespace AnimeDiscover.Services
{
    public class AnimeController
    {
        private readonly UserDataService _userDataService;
        private readonly MainController _mainController;

        public Datum CurrentAnime { get; set; }

        public AnimeController(UserDataService userDataService, MainController mainController)
        {
            _userDataService = userDataService;
            _mainController = mainController;
        }

        // Retourne la langue UI sélectionnée dans les préférences utilisateur.
        public string GetUiLanguage()
        {
            return _userDataService.GetUiLanguage();
        }

        // Définit l'anime actuellement affiché dans la page détail.
        public void SetCurrentAnime(Datum anime)
        {
            CurrentAnime = anime;
        }

        // Met à jour le statut "vu" de l'anime courant.
        public void UpdateIsWatched(bool isWatched)
        {
            if (CurrentAnime != null)
            {
                _userDataService.SaveUserData(CurrentAnime.Id, isWatched, CurrentAnime.UserScore, CurrentAnime.EpisodesWatched);
                CurrentAnime.IsWatched = isWatched;
                _mainController.AnimeListController.SyncFromAnimeDetails(CurrentAnime);
            }
        }

        // Met à jour la note utilisateur de l'anime courant.
        public void UpdateUserScore(int? score)
        {
            if (CurrentAnime != null)
            {
                _userDataService.SaveUserData(CurrentAnime.Id, CurrentAnime.IsWatched, score, CurrentAnime.EpisodesWatched);
                CurrentAnime.UserScore = score;
                _mainController.AnimeListController.SyncFromAnimeDetails(CurrentAnime);
            }
        }

        // Met à jour le nombre d'épisodes vus pour l'anime courant.
        public void UpdateEpisodesWatched(int? episodesWatched)
        {
            if (CurrentAnime != null)
            {
                _userDataService.SaveUserData(CurrentAnime.Id, CurrentAnime.IsWatched, CurrentAnime.UserScore, episodesWatched);
                CurrentAnime.EpisodesWatched = episodesWatched;
                _mainController.AnimeListController.SyncFromAnimeDetails(CurrentAnime);
            }
        }

        // Retourne à la page d'accueil.
        public void GoBack()
        {
            _mainController.ShowHome();
        }
    }
}
