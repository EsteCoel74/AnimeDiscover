using AnimeDiscover.Models;
using AnimeDiscover.Views;

// Coordonne la navigation principale entre les pages de l'application.
namespace AnimeDiscover.Services
{
    public class MainController
    {
        private readonly IJikanService _jikanService;
        private readonly UserDataService _userDataService;
        private AiConversationPage? _aiConversationPage;

        public HomeController HomeController { get; }
        public AnimeListController AnimeListController { get; }
        public AnimeController AnimeController { get; }
        public UserDataService UserDataService => _userDataService;

        public System.Action<object> NavigateAction { get; set; }

        public MainController()
        {
            _jikanService = new JikanService();
            _userDataService = new UserDataService();

            HomeController = new HomeController(_jikanService, this, _userDataService);
            AnimeListController = new AnimeListController(_userDataService, this, _jikanService);
            AnimeController = new AnimeController(_userDataService, this);
        }

        // Affiche la page d'accueil et charge les animes de saison.
        public void ShowHome()
        {
            _ = ShowHomeAsync();
        }

        // Affiche la page d'accueil et charge les animes de saison avec gestion d'erreurs.
        public async System.Threading.Tasks.Task ShowHomeAsync()
        {
            var homePage = new HomePage { DataContext = HomeController };
            try
            {
                await HomeController.LoadCurrentSeasonAsync();
            }
            catch
            {
                // La page d'accueil est tout de même affichée même si le chargement API échoue.
            }

            NavigateAction?.Invoke(homePage);
        }

        // Affiche la page d'accueil sans recharger les données (utile pour afficher une recherche).
        public void ShowHomeView()
        {
            var homePage = new HomePage { DataContext = HomeController };
            NavigateAction?.Invoke(homePage);
        }

        // Affiche la page de la liste personnelle d'animes.
        public void ShowAnimeList()
        {
            var listPage = new AnimeListPage { DataContext = AnimeListController };
            AnimeListController.LoadUserAnimesAsync();
            NavigateAction?.Invoke(listPage);
        }

        // Ouvre la page de détail pour l'anime sélectionné.
        public void ShowAnimeDetails(Datum anime)
        {
            AnimeController.SetCurrentAnime(anime);
            var detailPage = new AnimePage { DataContext = AnimeController };
            NavigateAction?.Invoke(detailPage);
        }

        // Affiche la page de conversation avec l'assistant IA.
        public void ShowAiConversation()
        {
            _aiConversationPage ??= new AiConversationPage(this, _jikanService);
            NavigateAction?.Invoke(_aiConversationPage);
        }

        // Affiche la page des paramètres.
        public void ShowSettingsPage()
        {
            var settingsPage = new SettingsPage(_userDataService, () =>
            {
                ShowHome();
            }, ShowHome);

            NavigateAction?.Invoke(settingsPage);
        }
    }
}
