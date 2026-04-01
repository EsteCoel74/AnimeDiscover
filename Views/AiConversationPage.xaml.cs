using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AnimeDiscover.Models;
using AnimeDiscover.Services;

// Gère la conversation IA avec une logique 100% basée sur l'API Jikan.
namespace AnimeDiscover.Views
{
    public partial class AiConversationPage : UserControl
    {
        private const int ApiPageSize = 25;
        private const int MaxSearchPages = 10;
        private const int MaxCandidates = 350;
        private const int MaxResultsPerAnswer = 20;
        private const int MaxApiRequestsPerPrompt = 24;
        private const int ConversationSaveDebounceMs = 350;

        private readonly MainController _mainController;
        private readonly IJikanService _jikanService;
        private readonly HashSet<int> _recommendedAnimeIds = new();

        private bool _isSending;
        private bool _isCriteriaLocked;
        private PromptCriteria? _lastCriteria;
        private string? _lastResolvedPrompt;
        private CancellationTokenSource? _requestCts;
        private CancellationTokenSource? _saveDebounceCts;
        private bool _scrollPending;

        private static readonly JsonSerializerOptions StorageJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public ObservableCollection<ChatMessage> Messages { get; } = new();

        // Retourne une chaîne localisée depuis les ressources UI.
        private static string L(string key, string fallback)
        {
            return UiPreferencesManager.GetText(key, fallback);
        }

        // Retourne le chemin du fichier de persistance de conversation IA.
        private static string GetConversationFilePath()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnimeDiscover");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "ai-conversation-history.json");
        }

        // Initialise la page IA avec restauration locale de l'historique.
        public AiConversationPage(MainController mainController, IJikanService jikanService)
        {
            InitializeComponent();
            DataContext = this;

            _mainController = mainController;
            _jikanService = jikanService;

            LoadConversationHistory();
            EnsureWelcomeMessageIfEmpty();
            UpdateLockFiltersButtonContent();
        }

        // Charge plus de résultats en réutilisant les derniers critères.
        private async void MoreResultsButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_isSending)
            {
                _requestCts?.Cancel();
                return;
            }

            if (string.IsNullOrWhiteSpace(_lastResolvedPrompt) && _lastCriteria == null)
            {
                AddAssistantText(L("AiPage.NoMoreResults", "Aucune recherche précédente à continuer."));
                return;
            }

            await SendPromptAsync("encore", forceFollowUp: true);
        }

        // Réinitialise la conversation et nettoie la persistance locale.
        private void ResetConversationButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _requestCts?.Cancel();
            _isSending = false;
            _recommendedAnimeIds.Clear();
            _lastCriteria = null;
            _lastResolvedPrompt = null;

            Messages.Clear();
            EnsureWelcomeMessageIfEmpty();
            SaveConversationHistoryImmediately();
            ScrollToBottom();
        }

        // Active/désactive le verrouillage des filtres de conversation.
        private void LockFiltersButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _isCriteriaLocked = !_isCriteriaLocked;
            UpdateLockFiltersButtonContent();
        }

        // Met à jour le texte du bouton de verrouillage des filtres.
        private void UpdateLockFiltersButtonContent()
        {
            if (LockFiltersButton == null)
            {
                return;
            }

            var lockText = _isCriteriaLocked
                ? L("AiPage.UnlockFilters", "Déverrouiller filtres")
                : L("AiPage.LockFilters", "Verrouiller filtres");

            if (LockFiltersButton.Content is TextBlock lockTextBlock)
            {
                lockTextBlock.Text = lockText;
                lockTextBlock.Foreground = System.Windows.Media.Brushes.White;
                return;
            }

            LockFiltersButton.Content = new TextBlock
            {
                Text = lockText,
                Foreground = System.Windows.Media.Brushes.White
            };
        }

        // Envoie le prompt avec Entrée (sans Shift+Entrée).
        private async void PromptTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                e.Handled = true;
                await SendPromptAsync();
            }
        }

        // Gère le cycle complet d'un envoi de message utilisateur.
        private async Task SendPromptAsync(string? forcedPrompt = null, bool forceFollowUp = false)
        {
            if (_isSending)
            {
                _requestCts?.Cancel();
                return;
            }

            var prompt = (forcedPrompt ?? PromptTextBox.Text)?.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            var followUp = forceFollowUp || IsFollowUpRequest(prompt);
            if (!followUp)
            {
                _recommendedAnimeIds.Clear();
            }

            _isSending = true;
            _requestCts?.Cancel();
            _requestCts = new CancellationTokenSource();
            var cancellationToken = _requestCts.Token;

            AddMessage(new ChatMessage
            {
                Author = L("AiPage.AuthorUser", "Toi"),
                Text = prompt,
                IsUser = true
            });

            var loadingMessage = new ChatMessage
            {
                Author = L("AiPage.AuthorAssistant", "Assistant IA"),
                Text = L("AiPage.Loading", "⏳ Je cherche dans l'API..."),
                IsUser = false
            };
            AddMessage(loadingMessage);

            if (forcedPrompt == null)
            {
                PromptTextBox.Clear();
            }

            ScrollToBottom();

            try
            {
                var recommendationResponse = await GetRecommendationsFromApiAsync(prompt, followUp, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                RemoveMessage(loadingMessage);

                if (recommendationResponse.Recommendations.Count == 0)
                {
                    AddAssistantText(followUp
                        ? L("AiPage.NoMoreResults", "Je n'ai plus de nouveaux résultats API pour cette demande. Ajoute des critères pour élargir.")
                        : L("AiPage.NoStrictResults", "Aucun anime ne correspond à tes critères dans l'API Jikan."));
                }
                else
                {
                    var messageText = string.Format(
                        L("AiPage.FoundResults", "J'ai trouvé {0} anime(s) via l'API Jikan."),
                        recommendationResponse.Recommendations.Count);

                    if (!string.IsNullOrWhiteSpace(recommendationResponse.AppliedFiltersSummary))
                    {
                        messageText = $"{messageText}\n{recommendationResponse.AppliedFiltersSummary}";
                    }

                    AddMessage(new ChatMessage
                    {
                        Author = L("AiPage.AuthorAssistant", "Assistant IA"),
                        Text = messageText,
                        IsUser = false,
                        Recommendations = new ObservableCollection<Datum>(recommendationResponse.Recommendations)
                    });
                }
            }
            catch (OperationCanceledException)
            {
                RemoveMessage(loadingMessage);
                AddAssistantText(L("AiPage.RequestCancelled", "Recherche annulée."));
            }
            catch
            {
                RemoveMessage(loadingMessage);
                AddAssistantText(L("AiPage.ApiUnavailable", "Impossible de contacter l'API pour le moment."));
            }
            finally
            {
                _isSending = false;
                ScrollToBottom();
            }
        }

        // Ajoute un message assistant simple dans la conversation.
        private void AddAssistantText(string text)
        {
            AddMessage(new ChatMessage
            {
                Author = L("AiPage.AuthorAssistant", "Assistant IA"),
                Text = text,
                IsUser = false
            });
        }

        // Ajoute un message et persiste immédiatement la conversation.
        private void AddMessage(ChatMessage message)
        {
            Messages.Add(message);
            RequestConversationHistorySave();
        }

        // Retire un message et persiste immédiatement la conversation.
        private void RemoveMessage(ChatMessage message)
        {
            Messages.Remove(message);
            RequestConversationHistorySave();
        }

        // Restaure la conversation depuis le stockage local si disponible.
        private void LoadConversationHistory()
        {
            try
            {
                var filePath = GetConversationFilePath();
                if (!File.Exists(filePath))
                {
                    return;
                }

                var json = File.ReadAllText(filePath);
                var storedMessages = JsonSerializer.Deserialize<List<StoredChatMessage>>(json, StorageJsonOptions) ?? new List<StoredChatMessage>();

                Messages.Clear();
                _recommendedAnimeIds.Clear();

                foreach (var stored in storedMessages)
                {
                    var restoredRecommendations = (stored.Recommendations ?? new List<Datum>())
                        .Where(a => a != null)
                        .ToList();

                    foreach (var anime in restoredRecommendations)
                    {
                        _recommendedAnimeIds.Add(anime.mal_id);
                    }

                    Messages.Add(new ChatMessage
                    {
                        Author = string.IsNullOrWhiteSpace(stored.Author) ? L("AiPage.AuthorAssistant", "Assistant IA") : stored.Author,
                        Text = stored.Text ?? string.Empty,
                        IsUser = stored.IsUser,
                        Recommendations = new ObservableCollection<Datum>(restoredRecommendations)
                    });
                }
            }
            catch
            {
                Messages.Clear();
            }
        }

        // Planifie une sauvegarde de conversation avec debounce pour réduire les écritures disque.
        private void RequestConversationHistorySave()
        {
            _saveDebounceCts?.Cancel();
            _saveDebounceCts = new CancellationTokenSource();
            var token = _saveDebounceCts.Token;

            _ = SaveConversationHistoryDebouncedAsync(token);
        }

        // Sauvegarde la conversation après un délai court pour grouper les mises à jour.
        private async Task SaveConversationHistoryDebouncedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(ConversationSaveDebounceMs, cancellationToken);
                SaveConversationHistoryImmediately();
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Sauvegarde immédiatement la conversation dans un fichier local JSON.
        private void SaveConversationHistoryImmediately()
        {
            try
            {
                var serialized = Messages.Select(m => new StoredChatMessage
                {
                    Author = m.Author,
                    Text = m.Text,
                    IsUser = m.IsUser,
                    Recommendations = m.Recommendations?.ToList() ?? new List<Datum>()
                }).ToList();

                var json = JsonSerializer.Serialize(serialized, StorageJsonOptions);
                File.WriteAllText(GetConversationFilePath(), json);
            }
            catch
            {
            }
        }

        // Ajoute le message d'accueil uniquement si la conversation est vide.
        private void EnsureWelcomeMessageIfEmpty()
        {
            if (Messages.Count > 0)
            {
                return;
            }

            AddMessage(new ChatMessage
            {
                Author = L("AiPage.AuthorAssistant", "Assistant IA"),
                Text = L("AiPage.Welcome", "Décris tes critères. Je réponds uniquement avec les données récupérées depuis l'API Jikan."),
                IsUser = false
            });
        }

        // Construit une réponse en agrégeant et filtrant les résultats Jikan.
        private async Task<RecommendationResponse> GetRecommendationsFromApiAsync(string userPrompt, bool preferNewResults, CancellationToken cancellationToken)
        {
            var resolvedPrompt = NormalizePromptForIntent(userPrompt);
            var criteria = ExtractPromptCriteria(resolvedPrompt);

            if (_isCriteriaLocked && _lastCriteria != null)
            {
                var lockedCriteria = CloneCriteria(_lastCriteria);
                if (!string.IsNullOrWhiteSpace(criteria.QueryText))
                {
                    lockedCriteria.QueryText = criteria.QueryText;
                }

                foreach (var term in criteria.MustTerms)
                {
                    if (!lockedCriteria.MustTerms.Contains(term, StringComparer.OrdinalIgnoreCase))
                    {
                        lockedCriteria.MustTerms.Add(term);
                    }
                }

                criteria = lockedCriteria;
            }
            else if (ShouldReusePreviousCriteria(resolvedPrompt, criteria))
            {
                criteria = CloneCriteria(_lastCriteria!);
                if (!string.IsNullOrWhiteSpace(_lastResolvedPrompt))
                {
                    resolvedPrompt = _lastResolvedPrompt;
                }
            }

            _lastCriteria = CloneCriteria(criteria);
            _lastResolvedPrompt = resolvedPrompt;

            var collected = new List<Datum>();
            var requestBudget = new ApiRequestBudget(MaxApiRequestsPerPrompt);

            if (HasExplicitApiFilters(criteria))
            {
                var criteriaResults = await FetchCriteriaPagesAsync(criteria, MaxSearchPages, MaxCandidates, cancellationToken, requestBudget);
                MergeUnique(collected, criteriaResults);
            }

            foreach (var query in BuildQueryCandidates(resolvedPrompt, criteria))
            {
                if (!requestBudget.HasRemaining)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var queryResults = await FetchTextSearchPagesAsync(query, MaxSearchPages, MaxCandidates, cancellationToken, requestBudget);
                MergeUnique(collected, queryResults);

                if (collected.Count >= MaxCandidates)
                {
                    break;
                }
            }

            if (collected.Count == 0)
            {
                var topFallback = await FetchTextSearchPagesAsync(string.Empty, MaxSearchPages, MaxCandidates, cancellationToken, requestBudget);
                MergeUnique(collected, topFallback);
            }

            var strict = collected
                .Where(a => a != null)
                .Where(a => MatchesCriteria(a, criteria, strictTextMatch: true))
                .OrderByDescending(a => ComputeRelevanceScore(a, criteria))
                .ThenByDescending(a => a.score ?? 0)
                .ToList();

            var selected = strict.Count > 0
                ? SelectWithNovelty(strict, MaxResultsPerAnswer, preferNewResults)
                : SelectWithNovelty(
                    collected.Where(a => a != null)
                        .Where(a => MatchesCriteria(a, criteria, strictTextMatch: false))
                        .OrderByDescending(a => ComputeRelevanceScore(a, criteria))
                        .ThenByDescending(a => a.score ?? 0)
                        .ToList(),
                    MaxResultsPerAnswer,
                    preferNewResults);

            return new RecommendationResponse
            {
                Recommendations = selected,
                AppliedFiltersSummary = BuildAppliedFiltersSummary(criteria)
            };
        }

        // Recherche par texte sur plusieurs pages API.
        private async Task<List<Datum>> FetchTextSearchPagesAsync(string query, int maxPages, int maxResults, CancellationToken cancellationToken, ApiRequestBudget requestBudget)
        {
            var collected = new List<Datum>();
            var safeMaxPages = Math.Max(1, maxPages);
            var safeMaxResults = Math.Max(1, maxResults);

            for (var page = 1; page <= safeMaxPages; page++)
            {
                if (!requestBudget.TryConsume())
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();

                var pageResults = await _jikanService.SearchAnimeAsync(query, page, ApiPageSize);
                if (pageResults == null || pageResults.Count == 0)
                {
                    break;
                }

                MergeUnique(collected, pageResults);
                if (collected.Count >= safeMaxResults)
                {
                    return collected;
                }

                if (pageResults.Count < ApiPageSize)
                {
                    break;
                }

                await Task.Delay(120, cancellationToken);
            }

            return collected;
        }

        // Recherche par critères API sur plusieurs pages.
        private async Task<List<Datum>> FetchCriteriaPagesAsync(PromptCriteria criteria, int maxPages, int maxResults, CancellationToken cancellationToken, ApiRequestBudget requestBudget)
        {
            var collected = new List<Datum>();
            var safeMaxPages = Math.Max(1, maxPages);
            var safeMaxResults = Math.Max(1, maxResults);

            for (var page = 1; page <= safeMaxPages; page++)
            {
                if (!requestBudget.TryConsume())
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();

                var apiCriteria = BuildApiCriteria(criteria, page);
                var pageResults = await _jikanService.SearchAnimeByCriteriaAsync(apiCriteria);

                if (pageResults == null || pageResults.Count == 0)
                {
                    break;
                }

                MergeUnique(collected, pageResults);
                if (collected.Count >= safeMaxResults)
                {
                    return collected;
                }

                if (pageResults.Count < ApiPageSize)
                {
                    break;
                }

                await Task.Delay(120, cancellationToken);
            }

            return collected;
        }

        // Construit les critères Jikan à partir des critères extraits du prompt.
        private static AnimeApiCriteria BuildApiCriteria(PromptCriteria criteria, int page)
        {
            var hasGenreOnlyConstraint = criteria.GenreIds.Count > 0
                && string.IsNullOrWhiteSpace(criteria.Type)
                && string.IsNullOrWhiteSpace(criteria.Status)
                && !criteria.MinScore.HasValue
                && !criteria.FromYear.HasValue
                && !criteria.ToYear.HasValue
                && string.IsNullOrWhiteSpace(criteria.Rating);

            var safeQuery = hasGenreOnlyConstraint ? null : (string.IsNullOrWhiteSpace(criteria.QueryText) ? null : criteria.QueryText);

            return new AnimeApiCriteria
            {
                q = safeQuery,
                type = criteria.Type,
                status = criteria.Status,
                genre_ids = new List<int>(criteria.GenreIds),
                min_score = criteria.MinScore,
                rating = criteria.Rating,
                start_date = criteria.FromYear.HasValue ? $"{criteria.FromYear.Value:0000}-01-01" : null,
                end_date = criteria.ToYear.HasValue ? $"{criteria.ToYear.Value:0000}-12-31" : null,
                order_by = "score",
                sort = "desc",
                page = page,
                limit = ApiPageSize
            };
        }

        // Fusionne des animes sans doublons dans la collection cible.
        private static void MergeUnique(List<Datum> target, List<Datum> source)
        {
            foreach (var anime in source.Where(a => a != null))
            {
                if (target.All(x => x.mal_id != anime.mal_id))
                {
                    target.Add(anime);
                }
            }
        }

        // Génère les requêtes texte à tenter côté API.
        private static List<string> BuildQueryCandidates(string userPrompt, PromptCriteria criteria)
        {
            var queries = new List<string>();

            if (criteria.ThemeTerms.Count > 0)
            {
                queries.Add(string.Join(" ", criteria.ThemeTerms.Take(4)));
            }

            if (criteria.GenreTerms.Count > 0)
            {
                queries.Add(string.Join(" ", criteria.GenreTerms.Take(4)));
            }

            queries.AddRange(criteria.AnimeNameHints);

            if (!string.IsNullOrWhiteSpace(userPrompt))
            {
                queries.Add(userPrompt.Trim());
            }

            if (!string.IsNullOrWhiteSpace(criteria.QueryText))
            {
                queries.Add(criteria.QueryText);
            }

            if (criteria.MustTerms.Count > 1)
            {
                queries.Add(string.Join(" ", criteria.MustTerms.Take(4)));
            }

            queries.AddRange(criteria.SeedQueries);

            return queries
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(14)
                .ToList();
        }

        // Extrait des critères structurés depuis le prompt utilisateur.
        private static PromptCriteria ExtractPromptCriteria(string prompt)
        {
            var normalizedPrompt = NormalizePromptForIntent(prompt);
            var normalized = NormalizeText(normalizedPrompt);
            var criteria = new PromptCriteria();

            foreach (var themeAlias in ThemeAliasMap)
            {
                if (!normalized.Contains(themeAlias.Key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!criteria.ThemeTerms.Contains(themeAlias.Value, StringComparer.OrdinalIgnoreCase))
                {
                    criteria.ThemeTerms.Add(themeAlias.Value);
                }

                if (!criteria.MustTerms.Contains(themeAlias.Value, StringComparer.OrdinalIgnoreCase))
                {
                    criteria.MustTerms.Add(themeAlias.Value);
                }
            }

            if (Regex.IsMatch(normalized, "\\b(film|movie|long metrage|long-metrage|cinema)\\b")) criteria.Type = "movie";
            else if (Regex.IsMatch(normalized, "\\b(ova)\\b")) criteria.Type = "ova";
            else if (Regex.IsMatch(normalized, "\\b(ona)\\b")) criteria.Type = "ona";
            else if (Regex.IsMatch(normalized, "\\b(tv|serie|série|anime tv)\\b")) criteria.Type = "tv";

            if (Regex.IsMatch(normalized, "\\b(en cours|en diffusion|airing|actuel|ongoing)\\b")) criteria.Status = "airing";
            else if (Regex.IsMatch(normalized, "\\b(termin[eé]|fini|complete|completed)\\b")) criteria.Status = "complete";
            else if (Regex.IsMatch(normalized, "\\b(a venir|à venir|prochain|upcoming)\\b")) criteria.Status = "upcoming";

            foreach (var alias in GenreAliasMap)
            {
                if (normalized.Contains(alias.Key, StringComparison.OrdinalIgnoreCase))
                {
                    if (!criteria.GenreIds.Contains(alias.Value.Id))
                    {
                        criteria.GenreIds.Add(alias.Value.Id);
                    }

                    if (!criteria.MustTerms.Contains(alias.Value.Canonical, StringComparer.OrdinalIgnoreCase))
                    {
                        criteria.MustTerms.Add(alias.Value.Canonical);
                    }

                    if (!criteria.GenreTerms.Contains(alias.Value.Canonical, StringComparer.OrdinalIgnoreCase))
                    {
                        criteria.GenreTerms.Add(alias.Value.Canonical);
                    }
                }
            }

            foreach (var animeNameHint in ExtractAnimeNameHints(prompt))
            {
                if (!criteria.AnimeNameHints.Contains(animeNameHint, StringComparer.OrdinalIgnoreCase))
                {
                    criteria.AnimeNameHints.Add(animeNameHint);
                }
            }

            var minScoreMatch = Regex.Match(normalized, "(?:score|note|rating)\\s*(?:>=|>|minimum|au dessus de|au moins)?\\s*(\\d+(?:[.,]\\d+)?)");
            if (minScoreMatch.Success && double.TryParse(minScoreMatch.Groups[1].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var score))
            {
                criteria.MinScore = Math.Clamp(score, 0, 10);
            }

            var minEpisodesMatch = Regex.Match(normalized, "(?:episodes?|épisodes?|eps?)\\s*(?:>=|>|minimum|au moins)\\s*(\\d+)");
            if (minEpisodesMatch.Success && int.TryParse(minEpisodesMatch.Groups[1].Value, out var minEpisodes))
            {
                criteria.MinEpisodes = Math.Max(0, minEpisodes);
            }

            var maxEpisodesMatch = Regex.Match(normalized, "(?:episodes?|épisodes?|eps?)\\s*(?:<=|<|maximum|au plus)\\s*(\\d+)");
            if (maxEpisodesMatch.Success && int.TryParse(maxEpisodesMatch.Groups[1].Value, out var maxEpisodes))
            {
                criteria.MaxEpisodes = Math.Max(0, maxEpisodes);
            }

            var betweenYearsMatch = Regex.Match(normalized, "(?:entre|between)\\s*(19\\d{2}|20\\d{2})\\s*(?:et|and|-)\\s*(19\\d{2}|20\\d{2})");
            if (betweenYearsMatch.Success)
            {
                criteria.FromYear = int.Parse(betweenYearsMatch.Groups[1].Value);
                criteria.ToYear = int.Parse(betweenYearsMatch.Groups[2].Value);
            }
            else
            {
                var fromYearMatch = Regex.Match(normalized, "(?:apres|après|after|depuis)\\s*(19\\d{2}|20\\d{2})");
                if (fromYearMatch.Success)
                {
                    criteria.FromYear = int.Parse(fromYearMatch.Groups[1].Value);
                }

                var toYearMatch = Regex.Match(normalized, "(?:avant|before|jusqu'?a|jusqu'?à)\\s*(19\\d{2}|20\\d{2})");
                if (toYearMatch.Success)
                {
                    criteria.ToYear = int.Parse(toYearMatch.Groups[1].Value);
                }
            }

            var exactYearMatch = Regex.Match(normalized, "\\b(19\\d{2}|20\\d{2})\\b");
            if (exactYearMatch.Success && !criteria.FromYear.HasValue && !criteria.ToYear.HasValue)
            {
                var year = int.Parse(exactYearMatch.Groups[1].Value);
                criteria.FromYear = year;
                criteria.ToYear = year;
            }

            if (normalized.Contains("tout public", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("familial", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("pg", StringComparison.OrdinalIgnoreCase))
            {
                criteria.Rating = "pg13";
            }
            else if (normalized.Contains("+17", StringComparison.OrdinalIgnoreCase) || normalized.Contains("r17", StringComparison.OrdinalIgnoreCase))
            {
                criteria.Rating = "r17";
            }
            else if (normalized.Contains("adulte", StringComparison.OrdinalIgnoreCase) || normalized.Contains("r+", StringComparison.OrdinalIgnoreCase))
            {
                criteria.Rating = "r";
            }

            if (normalized.Contains("manga", StringComparison.OrdinalIgnoreCase)) criteria.Source = "manga";
            else if (normalized.Contains("light novel", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("novel", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("roman", StringComparison.OrdinalIgnoreCase)) criteria.Source = "light novel";
            else if (normalized.Contains("jeu", StringComparison.OrdinalIgnoreCase) || normalized.Contains("game", StringComparison.OrdinalIgnoreCase)) criteria.Source = "game";
            else if (normalized.Contains("original", StringComparison.OrdinalIgnoreCase)) criteria.Source = "original";

            var weakWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "anime", "animes", "manga", "cherche", "trouve", "donne", "avec", "pour", "dans", "qui", "quoi", "juste", "stp", "svp", "bonjour", "salut", "merci"
            };

            foreach (var keyword in TokenizeKeywords(normalizedPrompt).Where(k => !weakWords.Contains(k)).Take(6))
            {
                if (!criteria.MustTerms.Contains(keyword, StringComparer.OrdinalIgnoreCase))
                {
                    criteria.MustTerms.Add(keyword);
                }
            }

            foreach (var genreId in criteria.GenreIds)
            {
                foreach (var seed in GetGenreSeedQueries(genreId))
                {
                    if (!criteria.SeedQueries.Contains(seed, StringComparer.OrdinalIgnoreCase))
                    {
                        criteria.SeedQueries.Add(seed);
                    }
                }
            }

            criteria.QueryText = criteria.MustTerms.Count == 0
                ? string.Join(" ", TokenizeKeywords(normalizedPrompt).Take(4))
                : string.Join(" ", criteria.MustTerms.Take(6));

            if (string.IsNullOrWhiteSpace(criteria.QueryText) && criteria.AnimeNameHints.Count > 0)
            {
                criteria.QueryText = criteria.AnimeNameHints[0];
            }

            return criteria;
        }

        // Extrait les noms d'animes mentionnés explicitement (entre guillemets ou après "comme/style").
        private static List<string> ExtractAnimeNameHints(string prompt)
        {
            var hints = new List<string>();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return hints;
            }

            foreach (Match quotedMatch in Regex.Matches(prompt, "[\"“”'](?<title>[^\"“”']{2,80})[\"“”']"))
            {
                var quotedTitle = quotedMatch.Groups["title"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(quotedTitle))
                {
                    hints.Add(quotedTitle);
                }
            }

            var normalized = NormalizeText(prompt);
            var contextualPattern = "(?:comme|style|dans le style de|similaire a|similaire a|like)\\s+([a-z0-9][a-z0-9'!:\\- ]{1,60})";
            foreach (Match contextualMatch in Regex.Matches(normalized, contextualPattern))
            {
                var titleHint = contextualMatch.Groups[1].Value.Trim(' ', '.', ',', ';', ':');
                if (!string.IsNullOrWhiteSpace(titleHint))
                {
                    hints.Add(titleHint);
                }
            }

            return hints
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
        }

        // Retourne une synthèse lisible des filtres réellement appliqués.
        private static string BuildAppliedFiltersSummary(PromptCriteria criteria)
        {
            var parts = new List<string>();

            if (criteria.ThemeTerms.Count > 0) parts.Add($"themes: {string.Join(",", criteria.ThemeTerms)}");
            if (!string.IsNullOrWhiteSpace(criteria.Type)) parts.Add($"type: {criteria.Type}");
            if (!string.IsNullOrWhiteSpace(criteria.Status)) parts.Add($"status: {criteria.Status}");
            if (criteria.GenreIds.Count > 0) parts.Add($"genres: {string.Join(",", criteria.GenreIds)}");
            if (criteria.MinScore.HasValue) parts.Add($"score ≥ {criteria.MinScore.Value:0.0}");
            if (criteria.MinEpisodes.HasValue) parts.Add($"episodes ≥ {criteria.MinEpisodes.Value}");
            if (criteria.MaxEpisodes.HasValue) parts.Add($"episodes ≤ {criteria.MaxEpisodes.Value}");
            if (criteria.FromYear.HasValue || criteria.ToYear.HasValue)
            {
                var from = criteria.FromYear?.ToString() ?? "...";
                var to = criteria.ToYear?.ToString() ?? "...";
                parts.Add($"années: {from}-{to}");
            }

            if (!string.IsNullOrWhiteSpace(criteria.Rating)) parts.Add($"rating: {criteria.Rating}");
            if (!string.IsNullOrWhiteSpace(criteria.Source)) parts.Add($"source: {criteria.Source}");

            if (parts.Count == 0)
            {
                return string.Empty;
            }

            return $"Filtres appliqués → {string.Join(" | ", parts)}";
        }

        // Vérifie si l'anime respecte les critères extraits du prompt.
        private static bool MatchesCriteria(Datum anime, PromptCriteria criteria, bool strictTextMatch)
        {
            if (!string.IsNullOrWhiteSpace(criteria.Type) && !string.Equals(anime?.type, criteria.Type, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(criteria.Status) && !string.Equals(anime?.status, criteria.Status, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (criteria.MinScore.HasValue && (anime?.score ?? 0) < criteria.MinScore.Value)
            {
                return false;
            }

            if (criteria.MinEpisodes.HasValue && (anime?.episodes ?? 0) < criteria.MinEpisodes.Value)
            {
                return false;
            }

            if (criteria.MaxEpisodes.HasValue && (anime?.episodes ?? 0) > criteria.MaxEpisodes.Value)
            {
                return false;
            }

            if (criteria.FromYear.HasValue && (anime?.year ?? 0) > 0 && anime.year.Value < criteria.FromYear.Value)
            {
                return false;
            }

            if (criteria.ToYear.HasValue && (anime?.year ?? 0) > 0 && anime.year.Value > criteria.ToYear.Value)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(criteria.Source) && !NormalizeText(anime?.source).Contains(criteria.Source, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(criteria.Rating) && !NormalizeText(anime?.rating).Contains(criteria.Rating, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!HasThemeMatch(anime, criteria))
            {
                return false;
            }

            if (!HasGenreMatch(anime, criteria))
            {
                return false;
            }

            if (criteria.MustTerms.Count == 0)
            {
                return true;
            }

            var haystack = NormalizeText($"{anime?.title} {anime?.title_english} {anime?.title_japanese} {anime?.synopsis} {string.Join(' ', anime?.Genres ?? new List<string>())} {string.Join(' ', anime?.Themes ?? new List<string>())}");
            var matchedTerms = criteria.MustTerms.Count(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));

            if (!strictTextMatch)
            {
                return matchedTerms >= 1;
            }

            var requiredMatches = Math.Max(1, (int)Math.Ceiling(criteria.MustTerms.Count * 0.45));
            return matchedTerms >= requiredMatches;
        }

        // Vérifie le match de genre via IDs Jikan et fallback texte.
        private static bool HasGenreMatch(Datum anime, PromptCriteria criteria)
        {
            if (criteria.GenreIds.Count == 0)
            {
                return true;
            }

            var animeGenreIds = (anime?.genres ?? new List<Genre>()).Select(g => g.mal_id).ToHashSet();
            if (criteria.GenreIds.Any(animeGenreIds.Contains))
            {
                return true;
            }

            var genreFallbackText = NormalizeText($"{anime?.title} {anime?.title_english} {anime?.title_japanese} {anime?.synopsis} {string.Join(' ', anime?.Genres ?? new List<string>())} {string.Join(' ', anime?.Themes ?? new List<string>())}");
            foreach (var genreId in criteria.GenreIds)
            {
                if (!GenreFallbackKeywordsMap.TryGetValue(genreId, out var fallbackKeywords))
                {
                    continue;
                }

                if (fallbackKeywords.Any(keyword => genreFallbackText.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            var haystack = NormalizeText($"{string.Join(' ', anime?.Genres ?? new List<string>())} {string.Join(' ', anime?.Themes ?? new List<string>())} {anime?.synopsis}");
            return criteria.MustTerms.Any(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // Vérifie le match des thèmes officiels fournis par l'API Jikan.
        private static bool HasThemeMatch(Datum anime, PromptCriteria criteria)
        {
            if (criteria.ThemeTerms.Count == 0)
            {
                return true;
            }

            var apiThemesText = NormalizeText(string.Join(' ', anime?.Themes ?? new List<string>()));
            if (criteria.ThemeTerms.Any(theme => apiThemesText.Contains(NormalizeText(theme), StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var fallbackText = NormalizeText($"{anime?.title} {anime?.title_english} {anime?.title_japanese} {anime?.synopsis}");
            return criteria.ThemeTerms.Any(theme => fallbackText.Contains(NormalizeText(theme), StringComparison.OrdinalIgnoreCase));
        }

        // Calcule un score de pertinence pour trier les résultats.
        private static int ComputeRelevanceScore(Datum anime, PromptCriteria criteria)
        {
            var score = 0;

            if (!string.IsNullOrWhiteSpace(criteria.Type) && string.Equals(anime?.type, criteria.Type, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            if (!string.IsNullOrWhiteSpace(criteria.Status) && string.Equals(anime?.status, criteria.Status, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            if (!string.IsNullOrWhiteSpace(criteria.Source) && NormalizeText(anime?.source).Contains(criteria.Source, StringComparison.OrdinalIgnoreCase))
            {
                score += 8;
            }

            if (!string.IsNullOrWhiteSpace(criteria.Rating) && NormalizeText(anime?.rating).Contains(criteria.Rating, StringComparison.OrdinalIgnoreCase))
            {
                score += 6;
            }

            if (criteria.FromYear.HasValue && (anime?.year ?? 0) > 0)
            {
                score += 4;
            }

            if (criteria.GenreIds.Count > 0)
            {
                var animeGenreIds = (anime?.genres ?? new List<Genre>()).Select(g => g.mal_id).ToHashSet();
                score += criteria.GenreIds.Count(id => animeGenreIds.Contains(id)) * 12;
            }

            var titleText = NormalizeText($"{anime?.title} {anime?.title_english} {anime?.title_japanese}");
            var descText = NormalizeText($"{anime?.synopsis} {string.Join(' ', anime?.Genres ?? new List<string>())} {string.Join(' ', anime?.Themes ?? new List<string>())}");

            foreach (var term in criteria.MustTerms)
            {
                if (titleText.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    score += 6;
                }

                if (descText.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    score += 14;
                }
            }

            score += (int)Math.Round((anime?.score ?? 0) * 2);
            return score;
        }

        // Sélectionne des résultats en privilégiant les nouveautés en suivi.
        private List<Datum> SelectWithNovelty(List<Datum> candidates, int maxResults, bool preferNewResults)
        {
            if (candidates.Count == 0)
            {
                return new List<Datum>();
            }

            List<Datum> selected;
            if (preferNewResults)
            {
                selected = candidates
                    .Where(a => !_recommendedAnimeIds.Contains(a.mal_id))
                    .Take(maxResults)
                    .ToList();

                if (selected.Count == 0)
                {
                    return new List<Datum>();
                }
            }
            else
            {
                selected = candidates.Take(maxResults).ToList();
            }

            foreach (var anime in selected)
            {
                _recommendedAnimeIds.Add(anime.mal_id);
            }

            return selected;
        }

        // Détecte une demande de continuation (ex: "encore", "plus").
        private static bool IsFollowUpRequest(string prompt)
        {
            var normalized = NormalizePromptForIntent(prompt);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            var patterns = new[]
            {
                "encore", "plus", "d'autres", "dautres", "autres", "continue", "en plus", "ajoute", "more", "again", "next"
            };

            return patterns.Any(p => normalized.Contains(p, StringComparison.OrdinalIgnoreCase));
        }

        // Indique si le prompt contient de vrais filtres exploitables côté API.
        private static bool HasExplicitApiFilters(PromptCriteria criteria)
        {
            return !string.IsNullOrWhiteSpace(criteria.Type)
                || !string.IsNullOrWhiteSpace(criteria.Status)
                || criteria.GenreIds.Count > 0
                || criteria.MinScore.HasValue
                || criteria.MinEpisodes.HasValue
                || criteria.MaxEpisodes.HasValue
                || criteria.FromYear.HasValue
                || criteria.ToYear.HasValue
                || !string.IsNullOrWhiteSpace(criteria.Rating)
                || !string.IsNullOrWhiteSpace(criteria.Source);
        }

        // Indique s'il faut réutiliser les critères précédents pour un follow-up.
        private bool ShouldReusePreviousCriteria(string prompt, PromptCriteria currentCriteria)
        {
            if (_lastCriteria == null)
            {
                return false;
            }

            if (!IsFollowUpRequest(prompt))
            {
                return false;
            }

            return !HasExplicitApiFilters(currentCriteria) && currentCriteria.MustTerms.Count == 0;
        }

        // Réalise une copie défensive d'un objet de critères.
        private static PromptCriteria CloneCriteria(PromptCriteria source)
        {
            var clone = new PromptCriteria
            {
                QueryText = source.QueryText,
                Type = source.Type,
                Status = source.Status,
                MinScore = source.MinScore,
                MinEpisodes = source.MinEpisodes,
                MaxEpisodes = source.MaxEpisodes,
                FromYear = source.FromYear,
                ToYear = source.ToYear,
                Rating = source.Rating,
                Source = source.Source
            };

            clone.GenreIds.AddRange(source.GenreIds);
            clone.ThemeTerms.AddRange(source.ThemeTerms);
            clone.GenreTerms.AddRange(source.GenreTerms);
            clone.AnimeNameHints.AddRange(source.AnimeNameHints);
            clone.MustTerms.AddRange(source.MustTerms);
            clone.SeedQueries.AddRange(source.SeedQueries);

            return clone;
        }

        // Normalise un texte pour simplifier les comparaisons (accents/casse).
        private static string NormalizeText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var formD = input.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(formD.Length);
            foreach (var c in formD)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        // Simplifie le prompt utilisateur pour mieux extraire l'intention de recherche.
        private static string NormalizePromptForIntent(string prompt)
        {
            var normalized = NormalizeText(prompt ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            foreach (var replacement in PromptIntentReplacements)
            {
                normalized = normalized.Replace(replacement.Key, replacement.Value, StringComparison.OrdinalIgnoreCase);
            }

            return Regex.Replace(normalized, "\\s+", " ").Trim();
        }

        // Extrait des mots-clés utiles depuis un texte libre.
        private static List<string> TokenizeKeywords(string text)
        {
            return Regex.Split(NormalizeText(text ?? string.Empty), "[^a-z0-9+\\-]+")
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Where(t => t.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Retourne les seeds de recherche associées à un genre pour élargir la couverture API.
        private static IEnumerable<string> GetGenreSeedQueries(int genreId)
        {
            return GenreSeedQueriesMap.TryGetValue(genreId, out var seeds)
                ? seeds
                : Array.Empty<string>();
        }

        // Ouvre la page de détail quand l'utilisateur clique une recommandation.
        private void RecommendationButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button { Tag: Datum anime })
            {
                _mainController.ShowAnimeDetails(anime);
            }
        }

        // Force le défilement de la conversation en bas.
        private void ScrollToBottom()
        {
            if (_scrollPending)
            {
                return;
            }

            _scrollPending = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ChatScrollViewer.ScrollToEnd();
                _scrollPending = false;
            }), DispatcherPriority.Background);
        }

        // Représente un message de conversation affiché dans l'UI.
        public class ChatMessage
        {
            public required string Author { get; init; }
            public required string Text { get; init; }
            public bool IsUser { get; init; }
            public ObservableCollection<Datum> Recommendations { get; init; } = new();
        }

        // Représente un message de conversation persisté localement.
        private sealed class StoredChatMessage
        {
            public string? Author { get; set; }
            public string? Text { get; set; }
            public bool IsUser { get; set; }
            public List<Datum>? Recommendations { get; set; }
        }

        // Contient la réponse recommandation + résumé des filtres appliqués.
        private sealed class RecommendationResponse
        {
            public List<Datum> Recommendations { get; set; } = new();
            public string AppliedFiltersSummary { get; set; } = string.Empty;
        }

        // Représente un budget global de requêtes API pour un prompt utilisateur.
        private sealed class ApiRequestBudget
        {
            public int RemainingRequests { get; private set; }
            public bool HasRemaining => RemainingRequests > 0;

            public ApiRequestBudget(int initialBudget)
            {
                RemainingRequests = Math.Max(0, initialBudget);
            }

            public bool TryConsume()
            {
                if (RemainingRequests <= 0)
                {
                    return false;
                }

                RemainingRequests--;
                return true;
            }
        }

        // Contient les critères dérivés du prompt utilisateur pour interroger l'API.
        private sealed class PromptCriteria
        {
            public string? QueryText { get; set; }
            public string? Type { get; set; }
            public string? Status { get; set; }
            public double? MinScore { get; set; }
            public int? MinEpisodes { get; set; }
            public int? MaxEpisodes { get; set; }
            public int? FromYear { get; set; }
            public int? ToYear { get; set; }
            public string? Rating { get; set; }
            public string? Source { get; set; }
            public List<int> GenreIds { get; } = new();
            public List<string> ThemeTerms { get; } = new();
            public List<string> GenreTerms { get; } = new();
            public List<string> AnimeNameHints { get; } = new();
            public List<string> MustTerms { get; } = new();
            public List<string> SeedQueries { get; } = new();
        }

        // Dictionnaire des alias de genres vers les IDs Jikan.
        private static readonly Dictionary<string, (int Id, string Canonical)> GenreAliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["action"] = (1, "action"),
            ["aventure"] = (2, "adventure"),
            ["adventure"] = (2, "adventure"),
            ["automobile"] = (3, "cars"),
            ["voiture"] = (3, "cars"),
            ["course"] = (3, "racing"),
            ["courses"] = (3, "racing"),
            ["racing"] = (3, "racing"),
            ["comédie"] = (4, "comedy"),
            ["comedie"] = (4, "comedy"),
            ["comedy"] = (4, "comedy"),
            ["drame"] = (8, "drama"),
            ["drama"] = (8, "drama"),
            ["fantasy"] = (10, "fantasy"),
            ["fantastique"] = (10, "fantasy"),
            ["horreur"] = (14, "horror"),
            ["horror"] = (14, "horror"),
            ["romance"] = (22, "romance"),
            ["science fiction"] = (24, "sci-fi"),
            ["science-fiction"] = (24, "sci-fi"),
            ["sf"] = (24, "sci-fi"),
            ["sci-fi"] = (24, "sci-fi"),
            ["sport"] = (30, "sports"),
            ["sports"] = (30, "sports"),
            ["slice of life"] = (36, "slice of life"),
            ["tranche de vie"] = (36, "slice of life"),
            ["surnaturel"] = (37, "supernatural"),
            ["supernatural"] = (37, "supernatural")
        };

        // Alias de thèmes officiels Jikan pour prioriser l'intention utilisateur.
        private static readonly Dictionary<string, string> ThemeAliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["psychologique"] = "psychological",
            ["psychological"] = "psychological",
            ["ecchi"] = "harem",
            ["harem"] = "harem",
            ["reverse harem"] = "reverse harem",
            ["isekai"] = "isekai",
            ["reincarnation"] = "reincarnation",
            ["school"] = "school",
            ["ecole"] = "school",
            ["école"] = "school",
            ["mecha"] = "mecha",
            ["military"] = "military",
            ["militaire"] = "military",
            ["music"] = "music",
            ["musique"] = "music",
            ["mythology"] = "mythology",
            ["mythologie"] = "mythology",
            ["samurai"] = "samurai",
            ["historical"] = "historical",
            ["historique"] = "historical",
            ["space"] = "space",
            ["espace"] = "space",
            ["time travel"] = "time travel",
            ["voyage temporel"] = "time travel",
            ["super power"] = "super power",
            ["super-pouvoir"] = "super power",
            ["video game"] = "video game",
            ["jeu video"] = "video game",
            ["jeu vidéo"] = "video game",
            ["parody"] = "parody",
            ["parodie"] = "parody",
            ["gore"] = "gore",
            ["martial arts"] = "martial arts",
            ["arts martiaux"] = "martial arts",
            ["strategy game"] = "strategy game",
            ["survival"] = "survival",
            ["workplace"] = "workplace",
            ["medical"] = "medical",
            ["detective"] = "detective",
            ["otaku culture"] = "otaku culture",
            ["racing"] = "racing",
            ["team sports"] = "team sports",
            ["romantic subtext"] = "romantic subtext",
            ["love polygon"] = "love polygon"
        };

        // Remplacements pour nettoyer les formulations conversationnelles du prompt.
        private static readonly Dictionary<string, string> PromptIntentReplacements = new(StringComparer.OrdinalIgnoreCase)
        {
            ["jveux"] = "je veux",
            ["j'aimerais"] = "je veux",
            ["j aimerais"] = "je veux",
            ["donne moi"] = "",
            ["trouve moi"] = "",
            ["fais moi"] = "",
            ["s'il te plait"] = "",
            ["s il te plait"] = "",
            ["please"] = "",
            ["stp"] = "",
            ["svp"] = ""
        };

        // Mots-clés de fallback par genre pour récupérer plus de résultats pertinents.
        private static readonly Dictionary<int, string[]> GenreFallbackKeywordsMap = new()
        {
            [1] = new[] { "action", "combat", "battle", "fight", "war" },
            [2] = new[] { "adventure", "quest", "journey", "expedition" },
            [3] = new[] { "racing", "race", "course", "cars", "car", "drift", "grand prix", "formula", "f1" },
            [4] = new[] { "comedy", "funny", "humor", "comique", "humour" },
            [8] = new[] { "drama", "drame", "tragic", "emotional" },
            [10] = new[] { "fantasy", "magic", "magie", "kingdom", "sword" },
            [14] = new[] { "horror", "horreur", "fear", "ghost", "terror" },
            [22] = new[] { "romance", "love", "couple", "relation" },
            [24] = new[] { "sci-fi", "science fiction", "space", "cyber", "mecha", "future" },
            [30] = new[] { "sports", "sport", "tournament", "match", "competition" },
            [36] = new[] { "slice of life", "daily life", "school life", "quotidien" },
            [37] = new[] { "supernatural", "surnaturel", "spirit", "demon", "ghost", "youkai" }
        };

        // Requêtes seed par genre pour diversifier les recherches paginées.
        private static readonly Dictionary<int, string[]> GenreSeedQueriesMap = new()
        {
            [1] = new[] { "Attack on Titan", "Jujutsu Kaisen" },
            [2] = new[] { "Made in Abyss", "Hunter x Hunter" },
            [3] = new[] { "Initial D", "MF Ghost", "Wangan Midnight", "Redline", "Capeta", "Overtake!" },
            [4] = new[] { "Gintama", "Kaguya-sama" },
            [8] = new[] { "Your Lie in April", "Nana" },
            [10] = new[] { "Re:Zero", "Frieren" },
            [14] = new[] { "Another", "Higurashi" },
            [22] = new[] { "Toradora", "Clannad" },
            [24] = new[] { "Steins;Gate", "Psycho-Pass" },
            [30] = new[] { "Haikyuu!!", "Blue Lock", "Kuroko no Basket" },
            [36] = new[] { "Barakamon", "Non Non Biyori" },
            [37] = new[] { "Natsume Yuujinchou", "Blue Exorcist" }
        };
    }
}
