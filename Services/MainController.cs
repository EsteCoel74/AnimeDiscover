using AnimeDiscover.Models;
using AnimeDiscover.Views;

namespace AnimeDiscover.Services
{
    public class MainController
    {
        private readonly IJikanService _jikanService;
        private readonly UserDataService _userDataService;

        public HomeController HomeController { get; }
        public AnimeListController AnimeListController { get; }
        public AnimeController AnimeController { get; }

        public System.Action<object> NavigateAction { get; set; }

        public MainController()
        {
            _jikanService = new JikanService();
            _userDataService = new UserDataService();

            HomeController = new HomeController(_jikanService, this, _userDataService);
            AnimeListController = new AnimeListController(_userDataService, this, _jikanService);
            AnimeController = new AnimeController(_userDataService, this);
        }

        public async void ShowHome()
        {
            var homePage = new HomePage { DataContext = HomeController };
            await HomeController.LoadCurrentSeasonAsync();
            NavigateAction?.Invoke(homePage);
        }

        public void ShowAnimeList()
        {
            var listPage = new AnimeListPage { DataContext = AnimeListController };
            AnimeListController.LoadUserAnimesAsync();
            NavigateAction?.Invoke(listPage);
        }

        public void ShowAnimeDetails(Anime anime)
        {
            AnimeController.SetCurrentAnime(anime);
            var detailPage = new AnimePage { DataContext = AnimeController };
            NavigateAction?.Invoke(detailPage);
        }
    }
}
