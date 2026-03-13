using AnimeDiscover.Models;

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

        public void SetCurrentAnime(Datum anime)
        {
            CurrentAnime = anime;
        }

        public void UpdateIsWatched(bool isWatched)
        {
            if (CurrentAnime != null)
            {
                _userDataService.SaveUserData(CurrentAnime.Id, isWatched, CurrentAnime.UserScore);
                CurrentAnime.IsWatched = isWatched;
            }
        }

        public void UpdateUserScore(int? score)
        {
            if (CurrentAnime != null)
            {
                _userDataService.SaveUserData(CurrentAnime.Id, CurrentAnime.IsWatched, score);
                CurrentAnime.UserScore = score;
            }
        }

        public void GoBack()
        {
            _mainController.ShowHome();
        }
    }
}
