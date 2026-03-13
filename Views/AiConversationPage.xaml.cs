using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
                Text = "Bonjour ! Donne-moi tes critères et je te proposerai des animes issus de l'API Jikan. Tu pourras cliquer dessus pour ouvrir leur fiche.",
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
                var aiDecision = await BuildCriteriaWithAiAsync(userPrompt);
                var recommendations = await GetAnimeRecommendationsAsync(aiDecision.criteria, userPrompt);

                if (recommendations.Count == 0)
                {
                    Messages.Add(new ChatMessage
                    {
                        Author = "Assistant IA",
                        Text = "Je n'ai trouvé aucun anime correspondant dans l'API. Essaie d'affiner ou de simplifier tes critères.",
                        IsUser = false
                    });
                }
                else
                {
                    Messages.Add(new ChatMessage
                    {
                        Author = "Assistant IA",
                        Text = $"{aiDecision.summary}\n\nJ'ai trouvé {recommendations.Count} recommandation(s) via les critères API. Clique sur un anime pour voir sa page détaillée.",
                        IsUser = false,
                        Recommendations = new ObservableCollection<Datum>(recommendations)
                    });
                }
            }
            catch (Exception)
            {
                Messages.Add(new ChatMessage
                {
                    Author = "Assistant IA",
                    Text = "Impossible de contacter l'API pour le moment. Réessaie dans quelques instants.",
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

        private async Task<List<Datum>> GetAnimeRecommendationsAsync(AnimeApiCriteria criteria, string userPrompt)
        {
            var collected = await _jikanService.SearchAnimeByCriteriaAsync(criteria);

            if (collected.Count == 0)
            {
                foreach (var query in BuildFallbackQueries(criteria?.q, userPrompt))
                {
                    var fallback = await _jikanService.SearchAnimeAsync(query);
                    foreach (var anime in fallback.Where(x => x != null))
                    {
                        if (collected.All(x => x.mal_id != anime.mal_id))
                        {
                            collected.Add(anime);
                        }
                    }

                    if (collected.Count >= 8)
                    {
                        break;
                    }
                }
            }

            if (collected.Count == 0)
            {
                var seasonFallback = await _jikanService.GetCurrentSeasonAsync();
                collected.AddRange(seasonFallback.Where(x => x != null));
            }

            return collected
                .Where(a => a != null)
                .GroupBy(a => a.mal_id)
                .Select(g => g.First())
                .OrderByDescending(a => a.score ?? 0)
                .Take(8)
                .ToList();
        }

        private static List<string> BuildFallbackQueries(string criteriaQuery, string userPrompt)
        {
            var queries = new List<string>();
            if (!string.IsNullOrWhiteSpace(criteriaQuery))
            {
                queries.Add(criteriaQuery.Trim());
            }

            if (!string.IsNullOrWhiteSpace(userPrompt))
            {
                queries.Add(userPrompt.Trim());
            }

            var source = string.Join(" ", queries);
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "je", "veux", "un", "une", "des", "de", "du", "la", "le", "les", "et", "ou", "avec",
                "pour", "qui", "que", "dans", "sur", "anime", "animes", "manga", "genre", "types", "type"
            };

            var keywords = Regex.Split(source, "[^a-zA-Z0-9+\\-]+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => x.Length >= 3)
                .Where(x => !stopWords.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            queries.AddRange(keywords);

            return queries
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
        }

        private async Task<(string summary, AnimeApiCriteria criteria)> BuildCriteriaWithAiAsync(string userPrompt)
        {
            var aiPrompt = "Tu es un assistant qui transforme une demande utilisateur en critères Jikan API. " +
                           "Réponds UNIQUEMENT en JSON valide, sans markdown, sans texte autour. " +
                           "Format exact: {\"summary\":\"...\",\"criteria\":{\"q\":\"\",\"type\":\"\",\"status\":\"\",\"rating\":\"\",\"order_by\":\"\",\"sort\":\"\",\"min_score\":null,\"start_date\":\"\",\"end_date\":\"\",\"genre_names\":[\"\"],\"limit\":8}}. " +
                           "Valeurs autorisées pour type: tv,movie,ova,ona,special,music,cm,pv,tv_special. " +
                           "Valeurs autorisées pour status: airing,complete,upcoming. " +
                           "Valeurs autorisées pour sort: desc,asc. " +
                           "Valeurs autorisées pour order_by: mal_id,title,start_date,end_date,episodes,score,scored_by,rank,popularity,members,favorites. " +
                           "rating peut être null ou g,pg,pg13,r17,rx. " +
                           "start_date/end_date doivent être YYYY-MM-DD ou chaîne vide. " +
                           "Si un champ est inconnu, mets chaîne vide ou null. " +
                           $"Demande utilisateur: {userPrompt}";

            try
            {
                var url = $"https://text.pollinations.ai/{Uri.EscapeDataString(aiPrompt)}";
                var raw = await AiHttpClient.GetStringAsync(url);
                var json = ExtractJson(raw);
                var parsed = JsonSerializer.Deserialize<AiCriteriaEnvelope>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var criteria = parsed?.criteria ?? new AiCriteriaDto();
                var normalized = NormalizeCriteria(criteria, userPrompt);
                var summary = string.IsNullOrWhiteSpace(parsed?.summary)
                    ? "Analyse IA effectuée sur tes critères."
                    : parsed.summary;

                return (summary, normalized);
            }
            catch
            {
                return ("Analyse IA partielle (fallback).", new AnimeApiCriteria
                {
                    q = userPrompt,
                    order_by = "score",
                    sort = "desc",
                    limit = 8
                });
            }
        }

        private static string ExtractJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "{}";

            var first = raw.IndexOf('{');
            var last = raw.LastIndexOf('}');
            if (first >= 0 && last > first)
                return raw.Substring(first, last - first + 1);

            return raw;
        }

        private static AnimeApiCriteria NormalizeCriteria(AiCriteriaDto dto, string fallbackPrompt)
        {
            var mappedGenreIds = new List<int>();
            if (dto.genre_names != null)
            {
                foreach (var g in dto.genre_names)
                {
                    if (TryMapGenreNameToId(g, out var id) && !mappedGenreIds.Contains(id))
                        mappedGenreIds.Add(id);
                }
            }

            return new AnimeApiCriteria
            {
                q = string.IsNullOrWhiteSpace(dto.q) ? fallbackPrompt : dto.q.Trim(),
                type = NormalizeSet(dto.type, new[] { "tv", "movie", "ova", "ona", "special", "music", "cm", "pv", "tv_special" }),
                status = NormalizeSet(dto.status, new[] { "airing", "complete", "upcoming" }),
                rating = NormalizeSet(dto.rating, new[] { "g", "pg", "pg13", "r17", "rx" }),
                order_by = NormalizeSet(dto.order_by, new[] { "mal_id", "title", "start_date", "end_date", "episodes", "score", "scored_by", "rank", "popularity", "members", "favorites" }),
                sort = NormalizeSet(dto.sort, new[] { "desc", "asc" }) ?? "desc",
                min_score = dto.min_score,
                start_date = NormalizeDate(dto.start_date),
                end_date = NormalizeDate(dto.end_date),
                genre_ids = mappedGenreIds,
                limit = dto.limit is > 0 and <= 25 ? dto.limit.Value : 8
            };
        }

        private static string NormalizeSet(string value, IEnumerable<string> allowed)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var lower = value.Trim().ToLowerInvariant();
            return allowed.Contains(lower) ? lower : null;
        }

        private static string NormalizeDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return Regex.IsMatch(value, "^\\d{4}-\\d{2}-\\d{2}$") ? value : null;
        }

        private static bool TryMapGenreNameToId(string genreName, out int id)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = 1,
                ["adventure"] = 2,
                ["comedy"] = 4,
                ["drama"] = 8,
                ["fantasy"] = 10,
                ["horror"] = 14,
                ["mystery"] = 7,
                ["romance"] = 22,
                ["sci-fi"] = 24,
                ["science fiction"] = 24,
                ["slice of life"] = 36,
                ["sports"] = 30,
                ["supernatural"] = 37,
                ["surnaturel"] = 37,
                ["thriller"] = 41
            };

            id = 0;
            if (string.IsNullOrWhiteSpace(genreName))
                return false;

            return map.TryGetValue(genreName.Trim(), out id);
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

        private class AiCriteriaEnvelope
        {
            public string summary { get; set; }
            public AiCriteriaDto criteria { get; set; }
        }

        private class AiCriteriaDto
        {
            public string q { get; set; }
            public string type { get; set; }
            public string status { get; set; }
            public string rating { get; set; }
            public string order_by { get; set; }
            public string sort { get; set; }
            public double? min_score { get; set; }
            public string start_date { get; set; }
            public string end_date { get; set; }
            public List<string> genre_names { get; set; } = new();
            public int? limit { get; set; }
        }
    }
}
