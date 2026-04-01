using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AnimeDiscover.Services;

// Gère la vue détaillée d'un anime et les interactions utilisateur associées.
namespace AnimeDiscover.Views
{
    /// <summary>
    /// Interaction logic for AnimePage.xaml
    /// </summary>
    public partial class AnimePage : UserControl
    {
        private static readonly Brush FilledStarBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4B400"));
        private static readonly Brush EmptyStarBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9AA0A6"));
        private static readonly HttpClient TranslationHttpClient = new();
        private static readonly ConcurrentDictionary<string, string> LocalizedTextCache = new();
        private string? _trailerUrl;

        // Retourne une chaîne localisée avec fallback.
        private static string L(string key, string fallback)
        {
            return UiPreferencesManager.GetText(key, fallback);
        }

        public AnimePage()
        {
            InitializeComponent();
            this.Loaded += AnimePage_Loaded;
        }

        // Initialise les données de la page détail au chargement.
        private void AnimePage_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AnimeController controller && controller.CurrentAnime != null)
            {
                // Load genres
                var genresControl = FindName("GenresItemsControl") as ItemsControl;
                if (genresControl != null && controller.CurrentAnime.Genres != null)
                {
                    genresControl.ItemsSource = controller.CurrentAnime.Genres;
                }

                // Trailer link (external browser for reliability)
                var trailerUrl = controller.CurrentAnime.TrailerUrl;
                if (!string.IsNullOrWhiteSpace(trailerUrl) && Uri.IsWellFormedUriString(trailerUrl, UriKind.Absolute))
                {
                    _trailerUrl = trailerUrl;
                    TrailerSection.Visibility = Visibility.Visible;
                }
                else
                {
                    _trailerUrl = null;
                    TrailerSection.Visibility = Visibility.Collapsed;
                }

                _ = ApplyLocalizedContentAsync(controller);
            }
        }

        // Localise le synopsis et le background selon la langue UI active.
        private async Task ApplyLocalizedContentAsync(AnimeController controller)
        {
            var synopsisTextBlock = FindName("SynopsisTextBlock") as TextBlock;
            var backgroundTextBlock = FindName("BackgroundTextBlock") as TextBlock;
            var backgroundSection = FindName("BackgroundSection") as FrameworkElement;

            if (controller?.CurrentAnime == null || synopsisTextBlock == null || backgroundTextBlock == null || backgroundSection == null)
            {
                return;
            }

            var originalSynopsis = controller.CurrentAnime.Synopsis ?? string.Empty;
            var originalBackground = controller.CurrentAnime.Background ?? string.Empty;

            synopsisTextBlock.Text = originalSynopsis;
            backgroundTextBlock.Text = originalBackground;
            backgroundSection.Visibility = string.IsNullOrWhiteSpace(originalBackground) ? Visibility.Collapsed : Visibility.Visible;

            var selectedLanguage = controller.GetUiLanguage();
            var targetLanguage = string.Equals(selectedLanguage, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "fr-FR";

            if (!string.IsNullOrWhiteSpace(originalSynopsis))
            {
                var localizedSynopsis = await GetLocalizedTextAsync(controller.CurrentAnime.Id, "synopsis", originalSynopsis, targetLanguage);
                if (!string.IsNullOrWhiteSpace(localizedSynopsis))
                {
                    synopsisTextBlock.Text = localizedSynopsis;
                }
            }

            if (!string.IsNullOrWhiteSpace(originalBackground))
            {
                var localizedBackground = await GetLocalizedTextAsync(controller.CurrentAnime.Id, "background", originalBackground, targetLanguage);
                if (!string.IsNullOrWhiteSpace(localizedBackground))
                {
                    backgroundTextBlock.Text = localizedBackground;
                }
            }
        }

        // Retourne un texte localisé avec cache mémoire par anime/champ/langue.
        private static async Task<string> GetLocalizedTextAsync(int animeId, string fieldName, string text, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var trimmed = text.Trim();
            var keyPrefix = trimmed[..Math.Min(48, trimmed.Length)];
            var cacheKey = $"{animeId}:{fieldName}:{targetLanguage}:{trimmed.Length}:{keyPrefix}";

            if (LocalizedTextCache.TryGetValue(cacheKey, out var cachedText))
            {
                return cachedText;
            }

            var localizedText = await TranslateTextAsync(trimmed, targetLanguage);
            var finalText = string.IsNullOrWhiteSpace(localizedText) ? trimmed : localizedText;
            LocalizedTextCache[cacheKey] = finalText;
            return finalText;
        }

        // Traduit un texte arbitraire vers la langue cible via service externe.
        private static async Task<string?> TranslateTextAsync(string text, string targetLanguage)
        {
            try
            {
                var trimmed = text.Trim();
                if (trimmed.Length > 3000)
                {
                    trimmed = trimmed[..3000];
                }

                var languageLabel = string.Equals(targetLanguage, "fr-FR", StringComparison.OrdinalIgnoreCase)
                    ? "français"
                    : "anglais";

                var prompt = $"Traduis le texte suivant en {languageLabel}. "
                           + "Réponds uniquement avec la traduction, sans commentaire:\n"
                           + trimmed;

                var url = $"https://text.pollinations.ai/{Uri.EscapeDataString(prompt)}";
                var response = await TranslationHttpClient.GetStringAsync(url);
                return NormalizeTranslatedText(response);
            }
            catch
            {
                return null;
            }
        }

        // Nettoie la réponse de traduction brute pour l'affichage dans l'UI.
        private static string NormalizeTranslatedText(string? rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return string.Empty;
            }

            var normalized = rawText.Trim();
            if ((normalized.StartsWith('"') && normalized.EndsWith('"'))
                || (normalized.StartsWith('\'') && normalized.EndsWith('\'')))
            {
                normalized = normalized[1..^1].Trim();
            }

            return normalized;
        }

        // Ouvre la bande-annonce dans le navigateur par défaut.
        private void OpenTrailerButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_trailerUrl))
            {
                AppMessageBox.Show(
                    L("AnimePage.TrailerUnavailable", "Trailer unavailable for this anime."),
                    L("Ui.InfoTitle", "Information"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _trailerUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppMessageBox.Show(
                    string.Format(L("AnimePage.TrailerOpenError", "Unable to open trailer: {0}"), ex.Message),
                    L("Ui.ErrorTitle", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // Retourne à la vue précédente.
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AnimeController controller)
            {
                controller.GoBack();
            }
        }

        // Met à jour le statut "vu" de l'anime.
        private void WatchedCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is AnimeController controller && sender is CheckBox checkBox)
            {
                controller.UpdateIsWatched(checkBox.IsChecked ?? false);
            }
        }

        // Initialise l'affichage des étoiles selon la note existante.
        private void StarRatingPanel_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Panel panel && DataContext is AnimeController controller && controller.CurrentAnime != null)
            {
                UpdateStarButtons(panel, ConvertScoreToStars(controller.CurrentAnime.UserScore));
            }
        }

        // Enregistre la note utilisateur via les étoiles.
        private void StarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Parent is not Panel panel)
            {
                return;
            }

            if (DataContext is not AnimeController controller || controller.CurrentAnime == null)
            {
                return;
            }

            if (!int.TryParse(button.Tag?.ToString(), out var stars))
            {
                return;
            }

            controller.UpdateUserScore(stars * 2);
            UpdateStarButtons(panel, stars);
        }

        // Prévisualise les étoiles au survol de la souris.
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
            if (sender is not Panel panel)
            {
                return;
            }

            if (DataContext is not AnimeController controller || controller.CurrentAnime == null)
            {
                return;
            }

            UpdateStarButtons(panel, ConvertScoreToStars(controller.CurrentAnime.UserScore));
        }

        // Valide la saisie des épisodes vus quand Entrée est pressée.
        private void EpisodesWatchedTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            SaveEpisodesWatchedFromInput(sender);
            e.Handled = true;
        }

        // Valide la saisie des épisodes vus à la perte de focus.
        private void EpisodesWatchedTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveEpisodesWatchedFromInput(sender);
        }

        // Parse, borne et enregistre le nombre d'épisodes vus.
        private void SaveEpisodesWatchedFromInput(object sender)
        {
            if (sender is not TextBox textBox || DataContext is not AnimeController controller || controller.CurrentAnime == null)
            {
                return;
            }

            int? episodesWatched = null;
            var text = textBox.Text?.Trim();

            if (!string.IsNullOrWhiteSpace(text))
            {
                if (!int.TryParse(text, out var parsedValue))
                {
                    textBox.Text = controller.CurrentAnime.EpisodesWatched?.ToString() ?? string.Empty;
                    return;
                }

                var maxEpisodes = controller.CurrentAnime.Episodes > 0 ? controller.CurrentAnime.Episodes : int.MaxValue;
                episodesWatched = Math.Clamp(parsedValue, 0, maxEpisodes);
            }

            controller.UpdateEpisodesWatched(episodesWatched);
            textBox.Text = episodesWatched?.ToString() ?? string.Empty;
        }

        // Convertit une note sur 10 en nombre d'étoiles sur 5.
        private static int ConvertScoreToStars(int? score)
        {
            var safeScore = Math.Clamp(score ?? 0, 0, 10);
            return (int)Math.Round(safeScore / 2.0, MidpointRounding.AwayFromZero);
        }

        // Met à jour visuellement les boutons d'étoiles.
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

        // Gère les changements de texte du champ épisodes (réservé pour évolution).
        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}
