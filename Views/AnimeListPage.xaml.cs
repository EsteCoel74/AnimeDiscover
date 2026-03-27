using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using AnimeDiscover.Models;
using AnimeDiscover.Services;

// Gère l'affichage et l'édition de la liste personnelle d'animes.
namespace AnimeDiscover.Views
{
    /// <summary>
    /// Interaction logic for AnimeListPage.xaml
    /// </summary>
    public partial class AnimeListPage : UserControl
    {
        private static readonly Brush FilledStarBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4B400"));
        private static readonly Brush EmptyStarBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9AA0A6"));
        private bool _isInternalChange;

        public AnimeListPage()
        {
            InitializeComponent();
            Loaded += AnimeListPage_Loaded;
        }

        // Applique le filtre pour n'afficher que les animes marqués comme vus.
        private void AnimeListPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Afficher uniquement les animes marqués comme "vu"
            var view = CollectionViewSource.GetDefaultView(AnimeListView.ItemsSource);
            if (view != null)
            {
                view.Filter = item => item is Datum anime && anime.IsWatched;
                view.Refresh();
            }
        }

        // Retourne à la page précédente.
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AnimeListController controller)
            {
                controller.GoBack();
            }
        }

        // Gère le changement de statut "vu" dans la liste.
        private void CheckBox_WatchedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInternalChange)
            {
                return;
            }

            if (sender is CheckBox checkBox && checkBox.DataContext is Datum anime)
            {
                if (DataContext is AnimeListController controller)
                {
                    var isWatched = checkBox.IsChecked ?? false;

                    if (!isWatched)
                    {
                        var confirm = AppMessageBox.Show(
                            $"Retirer '{anime.Title}' de votre liste ?",
                            "Confirmation",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (confirm != MessageBoxResult.Yes)
                        {
                            _isInternalChange = true;
                            checkBox.IsChecked = true;
                            _isInternalChange = false;
                            return;
                        }
                    }

                    controller.UpdateAnimeData(anime, isWatched, anime.UserScore, anime.EpisodesWatched);

                    var view = CollectionViewSource.GetDefaultView(AnimeListView.ItemsSource);
                    view?.Refresh();
                }
            }
        }

        // Initialise l'état des étoiles dans chaque ligne de la liste.
        private void StarRatingPanel_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Panel panel && panel.DataContext is Datum anime)
            {
                UpdateStarButtons(panel, ConvertScoreToStars(anime.UserScore));
            }
        }

        // Enregistre une nouvelle note utilisateur depuis les étoiles.
        private void StarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not Datum anime)
            {
                return;
            }

            if (button.Parent is not Panel panel)
            {
                return;
            }

            if (!int.TryParse(button.Tag?.ToString(), out var stars))
            {
                return;
            }

            var newScore = stars * 2;

            if (DataContext is AnimeListController controller)
            {
                controller.UpdateAnimeData(anime, anime.IsWatched, newScore, anime.EpisodesWatched);
            }

            UpdateStarButtons(panel, stars);
        }

        // Prévisualise les étoiles au survol.
        private void StarButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is not Button button || button.Parent is not Panel panel)
            {
                return;
            }

            if (!int.TryParse(button.Tag?.ToString(), out var stars))
            {
                return;
            }

            UpdateStarButtons(panel, stars);
        }

        // Restaure l'affichage des étoiles à la sortie de la zone.
        private void StarRatingPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is not Panel panel || panel.DataContext is not Datum anime)
            {
                return;
            }

            UpdateStarButtons(panel, ConvertScoreToStars(anime.UserScore));
        }

        // Valide la saisie d'épisodes vus sur Entrée.
        private void EpisodesWatchedTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            SaveEpisodesWatchedFromInput(sender);
            e.Handled = true;
        }

        // Valide la saisie d'épisodes vus à la perte de focus.
        private void EpisodesWatchedTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveEpisodesWatchedFromInput(sender);
        }

        // Parse, borne et enregistre la saisie des épisodes vus.
        private void SaveEpisodesWatchedFromInput(object sender)
        {
            if (sender is not TextBox textBox || textBox.DataContext is not Datum anime)
            {
                return;
            }

            int? episodesWatched = null;
            var text = textBox.Text?.Trim();

            if (!string.IsNullOrWhiteSpace(text))
            {
                if (!int.TryParse(text, out var parsedValue))
                {
                    textBox.Text = anime.EpisodesWatched?.ToString() ?? string.Empty;
                    return;
                }

                var maxEpisodes = anime.Episodes > 0 ? anime.Episodes : int.MaxValue;
                episodesWatched = Math.Clamp(parsedValue, 0, maxEpisodes);
            }

            if (DataContext is AnimeListController controller)
            {
                controller.UpdateAnimeData(anime, anime.IsWatched, anime.UserScore, episodesWatched);
            }

            textBox.Text = episodesWatched?.ToString() ?? string.Empty;
        }

        // Convertit une note utilisateur en étoiles affichables.
        private static int ConvertScoreToStars(int? score)
        {
            var safeScore = Math.Clamp(score ?? 0, 0, 10);
            return (int)Math.Round(safeScore / 2.0, MidpointRounding.AwayFromZero);
        }

        // Met à jour l'apparence des étoiles dans un panneau donné.
        private static void UpdateStarButtons(Panel panel, int stars)
        {
            var safeStars = Math.Clamp(stars, 0, 5);

            foreach (var starButton in panel.Children.OfType<Button>())
            {
                if (!int.TryParse(starButton.Tag?.ToString(), out var starIndex))
                {
                    continue;
                }

                starButton.Content = starIndex <= safeStars ? "★" : "☆";
                starButton.Foreground = starIndex <= safeStars ? FilledStarBrush : EmptyStarBrush;
            }
        }
    }
}
