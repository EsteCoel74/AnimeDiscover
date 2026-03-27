using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnimeDiscover.Services;

// Contrôle la fenêtre principale, la navigation et les interactions globales.
namespace AnimeDiscover
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainController _mainController;

        // Retourne une chaîne localisée depuis les ressources UI.
        private static string L(string key, string fallback)
        {
            return UiPreferencesManager.GetText(key, fallback);
        }

        public MainWindow()
        {
            InitializeComponent();
            ApplyLogoTransparency();
            InitializeApp();
        }

        // Rend le fond clair du logo transparent pour une meilleure intégration visuelle.
        private void ApplyLogoTransparency()
        {
            try
            {
                var logoUri = new Uri("pack://application:,,,/Assets/logo.png", UriKind.Absolute);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = logoUri;
                bitmap.EndInit();
                bitmap.Freeze();

                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                int stride = converted.PixelWidth * 4;
                byte[] pixels = new byte[stride * converted.PixelHeight];
                converted.CopyPixels(pixels, stride, 0);

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];

                    if (r > 245 && g > 245 && b > 245)
                    {
                        pixels[i + 3] = 0;
                    }
                }

                var transparentLogo = BitmapSource.Create(
                    converted.PixelWidth,
                    converted.PixelHeight,
                    converted.DpiX,
                    converted.DpiY,
                    PixelFormats.Bgra32,
                    null,
                    pixels,
                    stride);

                transparentLogo.Freeze();
                LogoImage.Source = transparentLogo;
            }
            catch (IOException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        // Initialise le contrôleur principal, la navigation et les événements UI.
        private async void InitializeApp()
        {
            // Initialize the MainController
            _mainController = new MainController();
            ApplySavedUiPreferences();
            ApplySavedTheme();

            // Wire up navigation
            _mainController.NavigateAction = (view) =>
            {
                ContentArea.Content = view;
            };

            // Wire up event handlers
            SearchTextBox.TextChanged += SearchTextBox_TextChanged;
            SearchTextBox.KeyDown += SearchTextBox_KeyDown;
            HomeButton.Click += HomeButton_Click;
            MyListButton.Click += MyListButton_Click;
            AiAssistantButton.Click += AiAssistantButton_Click;
            SettingsButton.Click += SettingsButton_Click;
            GenreComboBox.SelectionChanged += FilterComboBox_SelectionChanged;
            TypeComboBox.SelectionChanged += FilterComboBox_SelectionChanged;
            SuggestionsListBox.MouseDoubleClick += SuggestionsListBox_MouseDoubleClick;

            // Load initial content with random anime data
            await LoadHomePageAsync();
        }

        // Applique au démarrage les préférences UI (langue + animations).
        private void ApplySavedUiPreferences()
        {
            var savedLanguage = _mainController?.UserDataService?.GetUiLanguage();
            var savedAnimationsEnabled = _mainController?.UserDataService?.GetUiAnimationsEnabled() ?? true;

            UiPreferencesManager.ApplyLanguage(savedLanguage);
            UiPreferencesManager.ApplyAnimations(savedAnimationsEnabled);
        }

        // Applique au démarrage le thème précédemment enregistré.
        private void ApplySavedTheme()
        {
            var savedTheme = _mainController?.UserDataService?.GetTheme();
            ThemeManager.ApplyTheme(savedTheme);
        }

        // Charge la page d'accueil au démarrage.
        private async System.Threading.Tasks.Task LoadHomePageAsync()
        {
            try
            {
                await _mainController.ShowHomeAsync();
            }
            catch (Exception ex)
            {
                var errorMessage = string.Format(L("Ui.LoadError", "Erreur lors du chargement: {0}"), ex.Message);
                AppMessageBox.Show(errorMessage, L("Ui.ErrorTitle", "Erreur"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Lance une recherche de suggestions avec délai après saisie utilisateur.
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var homeController = _mainController?.HomeController;
            if (homeController == null) return;

            // Debounce search with 500ms delay
            System.Threading.Timer searchTimer = null;
            searchTimer = new System.Threading.Timer(
                async (state) =>
                {
                    Dispatcher.Invoke(async () =>
                    {
                        var searchText = SearchTextBox.Text.Trim();
                        if (searchText.Length > 2)
                        {
                            await homeController.SearchAsync(searchText);

                            if (homeController.SearchSuggestions.Count > 0)
                            {
                                SuggestionsListBox.Visibility = Visibility.Visible;
                                SuggestionsListBox.ItemsSource = homeController.SearchSuggestions;
                            }
                            else
                            {
                                SuggestionsListBox.Visibility = Visibility.Collapsed;
                            }
                        }
                        else
                        {
                            SuggestionsListBox.Visibility = Visibility.Collapsed;
                        }
                    });
                    searchTimer?.Dispose();
                },
                null,
                500,
                System.Threading.Timeout.Infinite
            );
        }

        // Exécute une recherche quand le bouton de recherche est cliqué.
        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearchAsync();
        }

        // Exécute la recherche quand l'utilisateur appuie sur Entrée.
        private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await PerformSearchAsync();
            }
        }

        // Applique la recherche avec les filtres actuellement sélectionnés.
        private async System.Threading.Tasks.Task PerformSearchAsync()
        {
            SuggestionsListBox.Visibility = Visibility.Collapsed;
            EnsureHomePageVisibleForSearch();

            var homeController = _mainController?.HomeController;
            if (homeController == null) return;

            var selectedGenre = GenreComboBox.SelectedItem as ComboBoxItem;
            var selectedType = TypeComboBox.SelectedItem as ComboBoxItem;

            var genreText = selectedGenre?.Content?.ToString() ?? L("Genre.All", "Tous les genres");
            var typeText = selectedType?.Content?.ToString() ?? L("Type.All", "Tous les types");

            await homeController.SearchAndApplyAsync(SearchTextBox.Text?.Trim(), genreText, typeText);
        }

        // Force l'affichage de la page d'accueil avant d'appliquer une recherche.
        private void EnsureHomePageVisibleForSearch()
        {
            if (ContentArea.Content is not Views.HomePage)
            {
                _mainController?.ShowHomeView();
            }
        }

        // Ouvre la page "Ma liste".
        private void MyListButton_Click(object sender, RoutedEventArgs e)
        {
            _mainController?.HomeController?.OpenMyList();
        }

        // Ouvre la page de conversation IA.
        private void AiAssistantButton_Click(object sender, RoutedEventArgs e)
        {
            _mainController?.ShowAiConversation();
        }

        // Ouvre la page des paramètres.
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _mainController?.ShowSettingsPage();
        }

        // Réinitialise la recherche et revient à la page d'accueil.
        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            SuggestionsListBox.Visibility = Visibility.Collapsed;
            SearchTextBox.Clear();
            _mainController?.ShowHome();
        }

        // Retourne à l'accueil lors d'un clic sur le logo.
        private void LogoImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            SuggestionsListBox.Visibility = Visibility.Collapsed;
            _mainController?.ShowHome();
        }

        // Réapplique les filtres à chaque changement de sélection.
        private async void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await ApplyFiltersAsync();
        }

        // Ouvre le détail de l'anime sélectionné dans les suggestions.
        private void SuggestionsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SuggestionsListBox.SelectedItem is Models.Datum selectedAnime)
            {
                _mainController?.HomeController?.SelectAnime(selectedAnime);
                SuggestionsListBox.Visibility = Visibility.Collapsed;
                SearchTextBox.Clear();
            }
        }

        // Applique les filtres courants avec ou sans texte de recherche.
        private async System.Threading.Tasks.Task ApplyFiltersAsync()
        {
            var homeController = _mainController?.HomeController;
            if (homeController == null) return;

            var selectedGenre = GenreComboBox.SelectedItem as ComboBoxItem;
            var selectedType = TypeComboBox.SelectedItem as ComboBoxItem;

            var genreText = selectedGenre?.Content?.ToString() ?? L("Genre.All", "Tous les genres");
            var typeText = selectedType?.Content?.ToString() ?? L("Type.All", "Tous les types");

            if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
            {
                await homeController.LoadRandomByFiltersAsync(genreText, typeText);
                return;
            }

            homeController.ApplyFilters(genreText, typeText);
        }
    }
}
