using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
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
                var aiDecision = await BuildCandidateTitlesWithAiAsync(userPrompt);
                var recommendations = await GetVerifiedRecommendationsAsync(userPrompt, aiDecision.titles);

                if (recommendations.Count == 0)
                {
                    Messages.Add(new ChatMessage
                    {
                        Author = "Assistant IA",
                        Text = "Aucun anime n'a pu être validé strictement dans l'API pour ce critère.",
                        IsUser = false
                    });
                }
                else
                {
                    Messages.Add(new ChatMessage
                    {
                        Author = "Assistant IA",
                        Text = $"{aiDecision.summary}\n\nJ'ai validé {recommendations.Count} anime(s) strictement dans l'API.",
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

        private async Task<(string summary, List<string> titles)> BuildCandidateTitlesWithAiAsync(string userPrompt)
        {
            var aiPrompt = "Tu es un expert anime. Tu dois proposer des titres qui correspondent STRICTEMENT à la demande utilisateur. " +
                           "Réponds UNIQUEMENT en JSON valide, sans markdown, sans texte autour. " +
                           "Format exact: {\"summary\":\"...\",\"titles\":[\"title1\",\"title2\"]}. " +
                           "Maximum 12 titres. " +
                           $"Demande utilisateur: {userPrompt}";

            try
            {
                var url = $"https://text.pollinations.ai/{Uri.EscapeDataString(aiPrompt)}";
                var raw = await AiHttpClient.GetStringAsync(url);
                var json = ExtractJson(raw);
                var parsed = JsonSerializer.Deserialize<AiTitlesEnvelope>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var titles = (parsed?.titles ?? new List<string>())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .ToList();

                if (titles.Count == 0)
                {
                    titles = BuildDeterministicFallbackTitles(userPrompt);
                }

                return (string.IsNullOrWhiteSpace(parsed?.summary) ? "Analyse IA terminée." : parsed.summary, titles);
            }
            catch
            {
                return ("Analyse IA partielle (fallback).", BuildDeterministicFallbackTitles(userPrompt));
            }
        }

        private async Task<List<Datum>> GetVerifiedRecommendationsAsync(string userPrompt, List<string> candidateTitles)
        {
            var verified = new List<Datum>();

            foreach (var candidate in candidateTitles ?? new List<string>())
            {
                var results = await _jikanService.SearchAnimeAsync(candidate);
                var exact = FindExactTitleMatch(results, candidate);
                if (exact == null || !MatchesStrictCriteria(exact, userPrompt))
                {
                    continue;
                }

                if (verified.All(x => x.mal_id != exact.mal_id))
                {
                    verified.Add(exact);
                }

                if (verified.Count >= 8)
                {
                    break;
                }
            }

            return verified;
        }

        private static Datum FindExactTitleMatch(IEnumerable<Datum> results, string candidateTitle)
        {
            if (results == null)
            {
                return null;
            }

            var expected = NormalizeText(candidateTitle);
            foreach (var anime in results)
            {
                if (anime == null)
                {
                    continue;
                }

                var names = new List<string> { anime.title, anime.title_english, anime.title_japanese };
                if (anime.title_synonyms != null)
                {
                    names.AddRange(anime.title_synonyms);
                }

                if (names.Any(n => !string.IsNullOrWhiteSpace(n) && NormalizeText(n) == expected))
                {
                    return anime;
                }
            }

            return null;
        }

        private static bool MatchesStrictCriteria(Datum anime, string prompt)
        {
            var normalizedPrompt = NormalizeText(prompt);
            var haystack = NormalizeText($"{anime?.title} {anime?.title_english} {anime?.synopsis} {string.Join(' ', anime?.Genres ?? new List<string>())} {string.Join(' ', anime?.Themes ?? new List<string>())}");

            var automotiveIntent = normalizedPrompt.Contains("course automobile")
                                   || normalizedPrompt.Contains("automobile")
                                   || normalizedPrompt.Contains("voiture")
                                   || normalizedPrompt.Contains("racing")
                                   || normalizedPrompt.Contains("motorsport")
                                   || normalizedPrompt.Contains("drift");

            if (automotiveIntent)
            {
                var automotiveTokens = new[] { "car", "cars", "automobile", "racing", "race", "drift", "motorsport", "formula" };
                return automotiveTokens.Any(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
            }

            var promptKeywords = BuildKeywordQuery(prompt, prompt)
                ?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                ?.Distinct(StringComparer.OrdinalIgnoreCase)
                ?.ToList() ?? new List<string>();

            if (promptKeywords.Count == 0)
            {
                return true;
            }

            var matched = promptKeywords.Count(k => haystack.Contains(k, StringComparison.OrdinalIgnoreCase));
            var required = Math.Min(2, Math.Max(1, promptKeywords.Count));
            return matched >= required;
        }

        private static List<string> BuildDeterministicFallbackTitles(string userPrompt)
        {
            var prompt = NormalizeText(userPrompt);
            if (prompt.Contains("course automobile") || prompt.Contains("automobile") || prompt.Contains("voiture") || prompt.Contains("racing") || prompt.Contains("motorsport") || prompt.Contains("drift"))
            {
                return new List<string> { "Initial D", "MF Ghost", "Wangan Midnight", "Capeta", "Redline" };
            }

            var extracted = BuildKeywordQuery(userPrompt, userPrompt);
            return string.IsNullOrWhiteSpace(extracted)
                ? new List<string>()
                : extracted.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5).ToList();
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
                .OrderByDescending(a => GetRelevanceScore(a, userPrompt))
                .ThenByDescending(a => a.score ?? 0)
                .Take(8)
                .ToList();
        }

        private static int GetRelevanceScore(Datum anime, string prompt)
        {
            var intentKeywords = GetIntentKeywords(prompt);
            if (intentKeywords.Count == 0)
            {
                return 0;
            }

            var haystack = NormalizeText($"{anime?.title} {anime?.title_english} {anime?.synopsis}");
            if (string.IsNullOrWhiteSpace(haystack))
            {
                return 0;
            }

            var score = 0;
            foreach (var keyword in intentKeywords)
            {
                if (haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }
            }

            return score;
        }

        private static List<string> GetIntentKeywords(string prompt)
        {
            var normalizedPrompt = NormalizeText(prompt ?? string.Empty);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (normalizedPrompt.Contains("course automobile")
                || normalizedPrompt.Contains("automobile")
                || normalizedPrompt.Contains("voiture")
                || normalizedPrompt.Contains("racing")
                || normalizedPrompt.Contains("motorsport")
                || normalizedPrompt.Contains("drift"))
            {
                foreach (var token in new[]
                {
                    "racing", "race", "car", "cars", "automobile", "auto", "drift", "street race", "motorsport", "formula"
                })
                {
                    result.Add(token);
                }
            }

            // tokens génériques extraits du prompt (hors stopwords)
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "je", "veux", "un", "une", "des", "de", "du", "la", "le", "les", "et", "ou", "avec", "pour",
                "qui", "que", "dans", "sur", "anime", "animes", "manga", "genre", "types", "type", "donne", "cherche"
            };

            var extracted = Regex.Split(normalizedPrompt, "[^a-zA-Z0-9+\\-]+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => x.Length >= 3)
                .Where(x => !stopWords.Contains(x));

            foreach (var token in extracted)
            {
                result.Add(token);
            }

            return result.ToList();
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
                normalized = EnrichCriteriaFromPrompt(normalized, userPrompt);
                var summary = string.IsNullOrWhiteSpace(parsed?.summary)
                    ? "Analyse IA effectuée sur tes critères."
                    : parsed.summary;

                return (summary, normalized);
            }
            catch
            {
                var fallback = EnrichCriteriaFromPrompt(new AnimeApiCriteria
                {
                    q = userPrompt,
                    order_by = "score",
                    sort = "desc",
                    limit = 8
                }, userPrompt);

                return ("Analyse IA partielle (fallback).", fallback);
            }
        }

        private static AnimeApiCriteria EnrichCriteriaFromPrompt(AnimeApiCriteria criteria, string userPrompt)
        {
            criteria ??= new AnimeApiCriteria();
            var prompt = userPrompt ?? string.Empty;
            var automotiveIntent = Regex.IsMatch(prompt, "\\b(course automobile|automobile|voiture|cars|car|racing|motorsport|drift|f1|formula)\\b", RegexOptions.IgnoreCase);

            if (string.IsNullOrWhiteSpace(criteria.type))
            {
                if (Regex.IsMatch(prompt, "\\b(film|movie)\\b", RegexOptions.IgnoreCase)) criteria.type = "movie";
                else if (Regex.IsMatch(prompt, "\\b(ova)\\b", RegexOptions.IgnoreCase)) criteria.type = "ova";
                else if (Regex.IsMatch(prompt, "\\b(ona)\\b", RegexOptions.IgnoreCase)) criteria.type = "ona";
                else if (Regex.IsMatch(prompt, "\\b(tv|série|serie)\\b", RegexOptions.IgnoreCase)) criteria.type = "tv";
            }

            if (string.IsNullOrWhiteSpace(criteria.status))
            {
                if (Regex.IsMatch(prompt, "\\b(en cours|airing|actuel)\\b", RegexOptions.IgnoreCase)) criteria.status = "airing";
                else if (Regex.IsMatch(prompt, "\\b(terminé|complete|fini)\\b", RegexOptions.IgnoreCase)) criteria.status = "complete";
                else if (Regex.IsMatch(prompt, "\\b(à venir|upcoming|prochain)\\b", RegexOptions.IgnoreCase)) criteria.status = "upcoming";
            }

            if (criteria.genre_ids == null)
            {
                criteria.genre_ids = new List<int>();
            }

            if (automotiveIntent)
            {
                if (!criteria.genre_ids.Contains(3)) criteria.genre_ids.Add(3);   // Cars
                if (!criteria.genre_ids.Contains(30)) criteria.genre_ids.Add(30); // Sports
                criteria.q = "car racing drift motorsport";
            }

            foreach (var token in new[] { "action", "aventure", "adventure", "comédie", "comedy", "drame", "fantasy", "horreur", "horror", "romance", "sci-fi", "science fiction", "slice of life", "sports", "surnaturel", "supernatural", "mystery", "thriller" })
            {
                if (Regex.IsMatch(prompt, $"\\b{Regex.Escape(token)}\\b", RegexOptions.IgnoreCase) && TryMapGenreNameToId(token, out var id) && !criteria.genre_ids.Contains(id))
                {
                    criteria.genre_ids.Add(id);
                }
            }

            var smartQuery = BuildKeywordQuery(criteria.q, prompt);
            criteria.q = string.IsNullOrWhiteSpace(smartQuery) ? criteria.q : smartQuery;
            criteria.order_by ??= "score";
            criteria.sort ??= "desc";
            if (criteria.limit <= 0) criteria.limit = 8;

            return criteria;
        }

        private static string BuildKeywordQuery(string aiQuery, string prompt)
        {
            var source = string.IsNullOrWhiteSpace(aiQuery) ? prompt : aiQuery;
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "je", "veux", "un", "une", "des", "de", "du", "la", "le", "les", "et", "ou", "avec", "pour",
                "qui", "que", "dans", "sur", "anime", "animes", "manga", "genre", "types", "type", "donne", "cherche"
            };

            var keywords = Regex.Split(source ?? string.Empty, "[^a-zA-Z0-9+\\-]+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => x.Length >= 3)
                .Where(x => !stopWords.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            return keywords.Count == 0 ? null : string.Join(" ", keywords);
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
                min_score = dto.min_score is >= 0 and <= 10 ? dto.min_score : null,
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
                ["aventure"] = 2,
                ["cars"] = 3,
                ["car"] = 3,
                ["voiture"] = 3,
                ["automobile"] = 3,
                ["racing"] = 3,
                ["motorsport"] = 3,
                ["drift"] = 3,
                ["comedy"] = 4,
                ["comédie"] = 4,
                ["drama"] = 8,
                ["drame"] = 8,
                ["fantasy"] = 10,
                ["horror"] = 14,
                ["horreur"] = 14,
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

        private class AiTitlesEnvelope
        {
            public string summary { get; set; }
            public List<string> titles { get; set; } = new();
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
