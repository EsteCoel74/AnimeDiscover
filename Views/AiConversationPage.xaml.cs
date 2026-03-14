using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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
        private static readonly HttpClient AiHttpClient = new();
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
            var previousButtonContent = SendButton.Content;
            SendButton.Content = "Recherche...";

            Messages.Add(new ChatMessage
            {
                Author = "Toi",
                Text = userPrompt,
                IsUser = true
            });

            var loadingMessage = new ChatMessage
            {
                Author = "Assistant IA",
                Text = "⏳ Je recherche à travers le monde...",
                IsUser = false
            };
            Messages.Add(loadingMessage);

            PromptTextBox.Clear();
            ScrollToBottom();

            try
            {
                var recommendations = await GetFilteredRecommendationsAsync(userPrompt);
                Messages.Remove(loadingMessage);
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
                Messages.Remove(loadingMessage);
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
                SendButton.Content = previousButtonContent;
                ScrollToBottom();
            }
        }

        private async Task<List<Datum>> GetFilteredRecommendationsAsync(string userPrompt)
        {
            const int maxResults = 20;
            var info = ExtractPromptInfo(userPrompt);
            await EnrichPromptInfoWithAiAsync(userPrompt, info);

            var collected = new List<Datum>();

            var criteriaResults = await _jikanService.SearchAnimeByCriteriaAsync(BuildSearchCriteria(userPrompt, info));
            foreach (var anime in criteriaResults.Where(x => x != null))
            {
                if (collected.All(x => x.mal_id != anime.mal_id))
                {
                    collected.Add(anime);
                }
            }

            foreach (var query in BuildQueries(userPrompt, info))
            {
                var results = await _jikanService.SearchAnimeAsync(query);
                foreach (var anime in results.Where(x => x != null))
                {
                    if (collected.All(x => x.mal_id != anime.mal_id))
                    {
                        collected.Add(anime);
                    }
                }

                if (collected.Count >= 80)
                {
                    break;
                }
            }

            if (collected.Count == 0)
            {
                return new List<Datum>();
            }

            var strict = collected
                .Where(a => MatchesStrictCriteria(a, info))
                .OrderByDescending(a => GetRelevanceScore(a, info))
                .ThenByDescending(a => a.score ?? 0)
                .Take(maxResults)
                .ToList();

            if (strict.Count > 0)
            {
                return strict;
            }

            var relaxed = collected
                .Where(a => GetRelevanceScore(a, info) >= 12)
                .OrderByDescending(a => GetRelevanceScore(a, info))
                .ThenByDescending(a => a.score ?? 0)
                .Take(maxResults)
                .ToList();

            return relaxed;
        }

        private static AnimeApiCriteria BuildSearchCriteria(string userPrompt, PromptInfo info)
        {
            var keywordQuery = BuildKeywordQuery(userPrompt, info);
            return new AnimeApiCriteria
            {
                q = string.IsNullOrWhiteSpace(keywordQuery) ? userPrompt : keywordQuery,
                type = info.Type,
                status = info.Status,
                genre_ids = info.GenreIds,
                order_by = "score",
                sort = "desc",
                limit = 25
            };
        }

        private static List<string> BuildQueries(string userPrompt, PromptInfo info)
        {
            var queries = new List<string>();

            if (!string.IsNullOrWhiteSpace(userPrompt))
            {
                queries.Add(userPrompt.Trim());
            }

            var keywordQuery = BuildKeywordQuery(userPrompt, info);
            if (!string.IsNullOrWhiteSpace(keywordQuery))
            {
                queries.Add(keywordQuery);
            }

            queries.AddRange(info.SeedQueries);
            queries.AddRange(info.RequiredTerms.Take(4));

            return queries
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(15)
                .ToList();
        }

        private static bool MatchesStrictCriteria(Datum anime, PromptInfo info)
        {
            var descriptionHaystack = NormalizeText($"{anime?.synopsis} {string.Join(' ', anime?.Genres ?? new List<string>())} {string.Join(' ', anime?.Themes ?? new List<string>())} {anime?.source} {anime?.rating}");
            if (string.IsNullOrWhiteSpace(descriptionHaystack))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(info.Type) && !string.Equals(anime?.type, info.Type, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(info.Status) && !string.Equals(anime?.status, info.Status, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (info.GenreIds.Count > 0)
            {
                var animeGenreIds = (anime?.genres ?? new List<Genre>()).Select(g => g.mal_id).ToHashSet();
                if (!info.GenreIds.Any(animeGenreIds.Contains))
                {
                    return false;
                }
            }

            if (info.RequiredTerms.Count == 0)
            {
                return true;
            }

            var textMatches = info.RequiredTerms.Count(k => descriptionHaystack.Contains(k, StringComparison.OrdinalIgnoreCase));

            var requiredTextMatches = Math.Max(1, (int)Math.Ceiling(info.RequiredTerms.Count * 0.4));
            return textMatches >= requiredTextMatches;
        }

        private static int GetRelevanceScore(Datum anime, PromptInfo info)
        {
            var titleHaystack = NormalizeText($"{anime?.title} {anime?.title_english} {anime?.title_japanese}");
            var descriptionHaystack = NormalizeText($"{anime?.synopsis} {string.Join(' ', anime?.Genres ?? new List<string>())} {string.Join(' ', anime?.Themes ?? new List<string>())} {anime?.source} {anime?.rating}");
            if (string.IsNullOrWhiteSpace(descriptionHaystack))
            {
                return 0;
            }

            var score = 0;
            foreach (var keyword in info.RequiredTerms)
            {
                if (titleHaystack.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    score += 4;
                }

                if (descriptionHaystack.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    score += 18;
                }
            }

            if (!string.IsNullOrWhiteSpace(info.Type) && string.Equals(anime?.type, info.Type, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            if (!string.IsNullOrWhiteSpace(info.Status) && string.Equals(anime?.status, info.Status, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            if (info.GenreIds.Count > 0)
            {
                var animeGenreIds = (anime?.genres ?? new List<Genre>()).Select(g => g.mal_id).ToHashSet();
                foreach (var genreId in info.GenreIds)
                {
                    if (animeGenreIds.Contains(genreId))
                    {
                        score += 14;
                    }
                }
            }

            return score;
        }

        private static string BuildKeywordQuery(string input, PromptInfo info)
        {
            var keywords = info.RequiredTerms.Count > 0 ? info.RequiredTerms : BuildPromptKeywords(input);
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

        private static PromptInfo ExtractPromptInfo(string prompt)
        {
            var normalized = NormalizeText(prompt);
            var info = new PromptInfo();

            if (Regex.IsMatch(normalized, "\\b(film|movie)\\b")) info.Type = "movie";
            else if (Regex.IsMatch(normalized, "\\b(ova)\\b")) info.Type = "ova";
            else if (Regex.IsMatch(normalized, "\\b(ona)\\b")) info.Type = "ona";
            else if (Regex.IsMatch(normalized, "\\b(tv|serie|série)\\b")) info.Type = "tv";

            if (Regex.IsMatch(normalized, "\\b(en cours|airing|actuel)\\b")) info.Status = "airing";
            else if (Regex.IsMatch(normalized, "\\b(termin[eé]|fini|complete)\\b")) info.Status = "complete";
            else if (Regex.IsMatch(normalized, "\\b(a venir|à venir|prochain|upcoming)\\b")) info.Status = "upcoming";

            var genreAlias = new Dictionary<string, (int id, string canonical)>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = (1, "action"),
                ["aventure"] = (2, "adventure"),
                ["adventure"] = (2, "adventure"),
                ["automobile"] = (3, "cars"),
                ["voiture"] = (3, "cars"),
                ["cars"] = (3, "cars"),
                ["racing"] = (3, "cars"),
                ["comedie"] = (4, "comedy"),
                ["comédie"] = (4, "comedy"),
                ["drame"] = (8, "drama"),
                ["drama"] = (8, "drama"),
                ["fantasy"] = (10, "fantasy"),
                ["horreur"] = (14, "horror"),
                ["horror"] = (14, "horror"),
                ["romance"] = (22, "romance"),
                ["science fiction"] = (24, "sci-fi"),
                ["sci-fi"] = (24, "sci-fi"),
                ["sports"] = (30, "sports"),
                ["sport"] = (30, "sports"),
                ["slice of life"] = (36, "slice of life"),
                ["surnaturel"] = (37, "supernatural"),
                ["supernatural"] = (37, "supernatural")
            };

            foreach (var kvp in genreAlias)
            {
                if (normalized.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    if (!info.GenreIds.Contains(kvp.Value.id)) info.GenreIds.Add(kvp.Value.id);
                    if (!info.RequiredTerms.Contains(kvp.Value.canonical)) info.RequiredTerms.Add(kvp.Value.canonical);
                }
            }

            var promptTerms = BuildPromptKeywords(prompt);
            var weakTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "precis", "precise", "meilleur", "mieux", "vraiment", "tres", "très", "good", "best", "top"
            };

            foreach (var term in promptTerms.Where(t => !weakTerms.Contains(t)).Take(4))
            {
                if (!IsNoiseTerm(term) && !info.RequiredTerms.Contains(term))
                {
                    info.RequiredTerms.Add(term);
                }
            }

            if (info.GenreIds.Contains(3))
            {
                info.SeedQueries.AddRange(new[] { "Initial D", "MF Ghost", "Wangan Midnight", "Capeta", "Redline" });
            }

            return info;
        }

        private async Task EnrichPromptInfoWithAiAsync(string prompt, PromptInfo info)
        {
            try
            {
                var aiPrompt = "Convertis la demande utilisateur en JSON STRICT (sans texte autour): " +
                               "{\"type\":\"\",\"status\":\"\",\"genre_names\":[\"\"],\"must_terms\":[\"\"],\"seed_queries\":[\"\"]}. " +
                               "type autorisé: tv,movie,ova,ona,special,music,cm,pv,tv_special ou vide. " +
                               "status autorisé: airing,complete,upcoming ou vide. " +
                               "must_terms = mots clés indispensables au sujet (max 6). " +
                               "seed_queries = titres/ancrages de recherche pertinents (max 8). " +
                               $"Demande: {prompt}";

                var url = $"https://text.pollinations.ai/{Uri.EscapeDataString(aiPrompt)}";
                var raw = await AiHttpClient.GetStringAsync(url);
                var json = ExtractFirstJson(raw);

                var parsed = System.Text.Json.JsonSerializer.Deserialize<AiPromptInfo>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed == null)
                {
                    return;
                }

                var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "tv","movie","ova","ona","special","music","cm","pv","tv_special"
                };
                var allowedStatus = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "airing","complete","upcoming"
                };

                if (string.IsNullOrWhiteSpace(info.Type) && !string.IsNullOrWhiteSpace(parsed.type) && allowedTypes.Contains(parsed.type))
                {
                    info.Type = parsed.type.ToLowerInvariant();
                }

                if (string.IsNullOrWhiteSpace(info.Status) && !string.IsNullOrWhiteSpace(parsed.status) && allowedStatus.Contains(parsed.status))
                {
                    info.Status = parsed.status.ToLowerInvariant();
                }

                foreach (var g in parsed.genre_names ?? new List<string>())
                {
                    if (TryMapGenre(g, out var id, out var canonical))
                    {
                        if (!info.GenreIds.Contains(id)) info.GenreIds.Add(id);
                        if (!info.RequiredTerms.Contains(canonical)) info.RequiredTerms.Add(canonical);
                    }
                }

                foreach (var term in (parsed.must_terms ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(NormalizeText).Take(6))
                {
                    if (!IsNoiseTerm(term) && !info.RequiredTerms.Contains(term))
                    {
                        info.RequiredTerms.Add(term);
                    }
                }

                foreach (var seed in (parsed.seed_queries ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Take(8))
                {
                    if (!info.SeedQueries.Contains(seed, StringComparer.OrdinalIgnoreCase))
                    {
                        info.SeedQueries.Add(seed);
                    }
                }
            }
            catch
            {
                // fallback silencieux sur l'extraction locale
            }
        }

        private static string ExtractFirstJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "{}";
            var first = raw.IndexOf('{');
            var last = raw.LastIndexOf('}');
            if (first >= 0 && last > first)
            {
                return raw.Substring(first, last - first + 1);
            }
            return "{}";
        }

        private static bool TryMapGenre(string value, out int id, out string? canonical)
        {
            id = 0;
            canonical = null;
            var v = NormalizeText(value);
            if (v == "action") { id = 1; canonical = "action"; return true; }
            if (v is "aventure" or "adventure") { id = 2; canonical = "adventure"; return true; }
            if (v is "automobile" or "voiture" or "cars" or "racing") { id = 3; canonical = "cars"; return true; }
            if (v is "comedie" or "comedy") { id = 4; canonical = "comedy"; return true; }
            if (v is "drame" or "drama") { id = 8; canonical = "drama"; return true; }
            if (v == "fantasy") { id = 10; canonical = "fantasy"; return true; }
            if (v is "horreur" or "horror") { id = 14; canonical = "horror"; return true; }
            if (v == "romance") { id = 22; canonical = "romance"; return true; }
            if (v is "science fiction" or "sci-fi") { id = 24; canonical = "sci-fi"; return true; }
            if (v is "sports" or "sport") { id = 30; canonical = "sports"; return true; }
            if (v == "slice of life") { id = 36; canonical = "slice of life"; return true; }
            if (v is "surnaturel" or "supernatural") { id = 37; canonical = "supernatural"; return true; }

            return false;
        }

        private static bool IsNoiseTerm(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return true;
            }

            var noise = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "anime", "animes", "animation", "animated", "serie", "series", "show", "tv", "manga"
            };

            return noise.Contains(term);
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

        private sealed class PromptInfo
        {
            public string? Type { get; set; }
            public string? Status { get; set; }
            public List<int> GenreIds { get; } = new();
            public List<string> RequiredTerms { get; } = new();
            public List<string> SeedQueries { get; } = new();
        }

        private sealed class AiPromptInfo
        {
            public string? type { get; set; }
            public string? status { get; set; }
            public List<string> genre_names { get; set; } = new();
            public List<string> must_terms { get; set; } = new();
            public List<string> seed_queries { get; set; } = new();
        }
    }
}
