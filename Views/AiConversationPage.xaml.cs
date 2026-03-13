using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AnimeDiscover.Models;
using AnimeDiscover.Services;

namespace AnimeDiscover.Views
{
    /// <summary>
    /// Interaction logic for AiConversationPage.xaml
    /// </summary>
    public partial class AiConversationPage : UserControl
    {
        private readonly MainController _mainController;
        private readonly IJikanService _jikanService;
        private bool _isSending;

        public ObservableCollection<ChatMessage> Messages { get; } = new();

        public AiConversationPage(MainController mainController, IJikanService jikanService)
        {
            InitializeComponent();
            DataContext = this;
            _mainController = mainController;
            _jikanService = jikanService;

            Messages.Add(new ChatMessage
            {
                Author = "Assistant IA",
                Text = "Décris précisément tes critères. Je filtre strictement les résultats trouvés dans l'API Jikan.",
                IsUser = false
            });
        }

        private async void SendButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await SendPromptAsync();
        }

        private async void PromptTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                e.Handled = true;
                await SendPromptAsync();
            }
        }

        private async Task SendPromptAsync()
        {
            if (_isSending)
            {
                return;
            }

            var userPrompt = PromptTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                return;
            }

            _isSending = true;
            SendButton.IsEnabled = false;

            Messages.Add(new ChatMessage
            {
                Author = "Toi",
                Text = userPrompt,
                IsUser = true
            });

            PromptTextBox.Clear();
            ScrollToBottom();

            try
            {
                var recommendations = await GetFilteredRecommendationsAsync(userPrompt);
                if (recommendations.Count == 0)
                {
                    Messages.Add(new ChatMessage
                    {
                        Author = "Assistant IA",
                        Text = "Aucun anime ne correspond strictement à ton critère dans l'API.",
                        IsUser = false
                    });
                }
                else
                {
                    Messages.Add(new ChatMessage
                    {
                        Author = "Assistant IA",
                        Text = $"J'ai trouvé {recommendations.Count} anime(s) strictement filtré(s) dans l'API.",
                        IsUser = false,
                        Recommendations = new ObservableCollection<Datum>(recommendations)
                    });
                }
            }
            catch
            {
                Messages.Add(new ChatMessage
                {
                    Author = "Assistant IA",
                    Text = "Impossible de contacter l'API pour le moment.",
                    IsUser = false
                });
            }
            finally
            {
                _isSending = false;
                SendButton.IsEnabled = true;
                ScrollToBottom();
            }
        }

        private async Task<List<Datum>> GetFilteredRecommendationsAsync(string userPrompt)
        {
            const int maxResults = 20;
            var intent = DetectIntent(userPrompt);
            var queries = BuildQueries(userPrompt, intent);

            var collected = new List<Datum>();

            // 0) Recherche API orientée critères (plus précise que la recherche texte brute)
            var criteriaResults = await _jikanService.SearchAnimeByCriteriaAsync(BuildIntentCriteria(userPrompt, intent));
            foreach (var anime in criteriaResults.Where(x => x != null))
            {
                if (collected.All(x => x.mal_id != anime.mal_id))
                {
                    collected.Add(anime);
                }
            }

            foreach (var query in queries)
            {
                var results = await _jikanService.SearchAnimeAsync(query);
                foreach (var anime in results.Where(x => x != null))
                {
                    if (collected.All(x => x.mal_id != anime.mal_id))
                    {
                        collected.Add(anime);
                    }
                }

                if (collected.Count >= 60)
                {
                    break;
                }
            }

            if (collected.Count == 0)
            {
                return new List<Datum>();
            }

            var strict = collected
                .Where(a => MatchesStrictCriteria(a, userPrompt, intent))
                .OrderByDescending(a => GetRelevanceScore(a, userPrompt, intent))
                .ThenByDescending(a => a.score ?? 0)
                .Take(maxResults)
                .ToList();

            if (strict.Count > 0)
            {
                return strict;
            }

            // Fallback intelligent: garder un filtrage minimum au lieu de renvoyer vide.
            var relaxed = collected
                .Where(a => GetRelevanceScore(a, userPrompt, intent) >= (intent == Intent.Automotive ? 8 : 5))
                .OrderByDescending(a => GetRelevanceScore(a, userPrompt, intent))
                .ThenByDescending(a => a.score ?? 0)
                .Take(maxResults)
                .ToList();

            return relaxed;
        }

        private static AnimeApiCriteria BuildIntentCriteria(string userPrompt, Intent intent)
        {
            if (intent == Intent.Automotive)
            {
                return new AnimeApiCriteria
                {
                    q = "racing",
                    genre_ids = new List<int> { 3, 30 }, // Cars + Sports
                    order_by = "score",
                    sort = "desc",
                    limit = 25
                };
            }

            var keywords = BuildKeywordQuery(userPrompt);
            return new AnimeApiCriteria
            {
                q = string.IsNullOrWhiteSpace(keywords) ? userPrompt : keywords,
                order_by = "score",
                sort = "desc",
                limit = 25
            };
        }

        private static List<string> BuildQueries(string userPrompt, Intent intent)
        {
            var queries = new List<string>();

            if (!string.IsNullOrWhiteSpace(userPrompt))
            {
                queries.Add(userPrompt.Trim());
            }

            var keywords = BuildKeywordQuery(userPrompt);
            if (!string.IsNullOrWhiteSpace(keywords))
            {
                queries.Add(keywords);
            }

            if (intent == Intent.Automotive)
            {
                queries.AddRange(new[]
                {
                    "Initial D",
                    "Initial D First Stage",
                    "Initial D Second Stage",
                    "Initial D Third Stage",
                    "Initial D Fourth Stage",
                    "Initial D Fifth Stage",
                    "MF Ghost",
                    "Wangan Midnight",
                    "Capeta",
                    "Redline"
                });
            }

            return queries
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool MatchesStrictCriteria(Datum anime, string prompt, Intent intent)
        {
            var haystack = NormalizeText($"{anime?.title} {anime?.title_english} {anime?.title_japanese} {anime?.synopsis} {string.Join(' ', anime?.Genres ?? new List<string>())} {string.Join(' ', anime?.Themes ?? new List<string>())}");
            if (string.IsNullOrWhiteSpace(haystack))
            {
                return false;
            }

            if (intent == Intent.Automotive)
            {
                var names = new[]
                {
                    NormalizeText(anime?.title),
                    NormalizeText(anime?.title_english),
                    NormalizeText(anime?.title_japanese)
                };

                var knownFranchise = names.Any(n => !string.IsNullOrWhiteSpace(n) &&
                    (n.Contains("initial d") || n.Contains("mf ghost") || n.Contains("wangan") || n.Contains("capeta") || n.Contains("redline")));

                var hasCarsGenre = (anime?.Genres ?? new List<string>())
                    .Any(g => string.Equals(g, "Cars", StringComparison.OrdinalIgnoreCase));

                var strongVehicleTokens = new[]
                {
                    "car", "cars", "automobile", "drift", "motorsport", "street racing", "rally", "touge", "formula 1", "f1"
                };

                var hasStrongVehicleSignal = strongVehicleTokens
                    .Any(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));

                // "racing"/"race" seul n'est pas suffisant (trop de faux positifs)
                return knownFranchise || hasCarsGenre || hasStrongVehicleSignal;
            }

            var promptKeywords = BuildPromptKeywords(prompt);
            if (promptKeywords.Count == 0)
            {
                return true;
            }

            var matched = promptKeywords.Count(k => haystack.Contains(k, StringComparison.OrdinalIgnoreCase));
            var required = Math.Min(2, Math.Max(1, promptKeywords.Count));
            return matched >= required;
        }

        private static int GetRelevanceScore(Datum anime, string prompt, Intent intent)
        {
            var haystack = NormalizeText($"{anime?.title} {anime?.title_english} {anime?.synopsis}");
            if (string.IsNullOrWhiteSpace(haystack))
            {
                return 0;
            }

            var score = 0;
            var promptKeywords = BuildPromptKeywords(prompt);
            foreach (var keyword in promptKeywords)
            {
                if (haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }
            }

            if (intent == Intent.Automotive)
            {
                foreach (var token in new[] { "initial d", "mf ghost", "wangan", "capeta", "redline", "drift", "automobile", "car", "cars", "motorsport", "f1" })
                {
                    if (haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 12;
                    }
                }
            }

            return score;
        }

        private static string BuildKeywordQuery(string input)
        {
            var keywords = BuildPromptKeywords(input);
            return keywords.Count == 0 ? null : string.Join(" ", keywords.Take(6));
        }

        private static List<string> BuildPromptKeywords(string input)
        {
            var normalized = NormalizeText(input ?? string.Empty);
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "je", "veux", "un", "une", "des", "de", "du", "la", "le", "les", "et", "ou", "avec", "pour",
                "qui", "que", "dans", "sur", "anime", "animes", "manga", "genre", "types", "type", "donne", "cherche"
            };

            return Regex.Split(normalized, "[^a-zA-Z0-9+\\-]+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => x.Length >= 3)
                .Where(x => !stopWords.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static Intent DetectIntent(string prompt)
        {
            var normalized = NormalizeText(prompt);
            var automotive = normalized.Contains("course automobile")
                             || normalized.Contains("automobile")
                             || normalized.Contains("voiture")
                             || normalized.Contains("racing")
                             || normalized.Contains("motorsport")
                             || normalized.Contains("drift")
                             || normalized.Contains("f1")
                             || normalized.Contains("formula");

            return automotive ? Intent.Automotive : Intent.Generic;
        }

        private static string NormalizeText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var formD = input.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (var c in formD)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        private void RecommendationButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button { Tag: Datum animeData })
            {
                _mainController.ShowAnimeDetails(animeData);
            }
        }

        private void ScrollToBottom()
        {
            Dispatcher.BeginInvoke(new Action(() => ChatScrollViewer.ScrollToEnd()), DispatcherPriority.Background);
        }

        public class ChatMessage
        {
            public required string Author { get; init; }
            public required string Text { get; init; }
            public bool IsUser { get; init; }
            public ObservableCollection<Datum> Recommendations { get; init; } = new();
        }

        private enum Intent
        {
            Generic,
            Automotive
        }
    }
}
