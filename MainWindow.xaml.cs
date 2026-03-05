using System;
using System.Windows;
using System.Windows.Controls;
using AnimeDiscover.Services;

namespace AnimeDiscover
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainController _mainController;

        public MainWindow()
        {
            InitializeComponent();
            InitializeApp();
        }

        private async void InitializeApp()
        {
            // Initialize the MainController
            _mainController = new MainController();

            // Wire up navigation
            _mainController.NavigateAction = (view) =>
            {
                ContentArea.Content = view;
            };

            // Wire up event handlers
            SearchTextBox.TextChanged += SearchTextBox_TextChanged;
            SearchButton.Click += SearchButton_Click;
            MyListButton.Click += MyListButton_Click;
            GenreComboBox.SelectionChanged += FilterComboBox_SelectionChanged;
            TypeComboBox.SelectionChanged += FilterComboBox_SelectionChanged;
            SuggestionsListBox.MouseDoubleClick += SuggestionsListBox_MouseDoubleClick;

            // Load initial content with random anime data
            await LoadHomePageAsync();
        }

        private async System.Threading.Tasks.Task LoadHomePageAsync()
        {
            try
            {
                _mainController.ShowHome();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement: {ex.Message}", "Erreur", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            SuggestionsListBox.Visibility = Visibility.Collapsed;
            ApplyFilters();
        }

        private void MyListButton_Click(object sender, RoutedEventArgs e)
        {
            _mainController?.HomeController?.OpenMyList();
        }

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void SuggestionsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SuggestionsListBox.SelectedItem is Models.Anime selectedAnime)
            {
                _mainController?.HomeController?.SelectAnime(selectedAnime);
                SuggestionsListBox.Visibility = Visibility.Collapsed;
                SearchTextBox.Clear();
            }
        }

        private void ApplyFilters()
        {
            var homeController = _mainController?.HomeController;
            if (homeController == null) return;

            var selectedGenre = GenreComboBox.SelectedItem as ComboBoxItem;
            var selectedType = TypeComboBox.SelectedItem as ComboBoxItem;

            var genreText = selectedGenre?.Content?.ToString() ?? "Tous les genres";
            var typeText = selectedType?.Content?.ToString() ?? "Tous les types";

            homeController.ApplyFilters(genreText, typeText);
        }
    }
}
