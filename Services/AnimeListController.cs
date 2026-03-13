using System;
using System.Collections.ObjectModel;
using System.Linq;
using AnimeDiscover.Models;

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
                    UserAnimes.Add(anime);
                }
            }
        }

        public void UpdateAnimeData(Datum anime, bool isWatched, int? userScore)
        {
            _userDataService.SaveUserData(anime.Id, isWatched, userScore);
            anime.IsWatched = isWatched;
            anime.UserScore = userScore;
        }

        public void SelectAnime(Datum anime)
        {
            _mainController.ShowAnimeDetails(anime);
        }

        public void GoBack()
        {
            _mainController.ShowHome();
        }
    }
}
