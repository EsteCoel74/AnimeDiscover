using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AnimeDiscover.Models;

// Implémentation HTTP du service Jikan avec cache léger.
namespace AnimeDiscover.Services
{
    public class JikanService : IJikanService
    {
        private sealed class CacheEntry
        {
            public required List<Datum> Results { get; init; }
            public required DateTimeOffset SavedAtUtc { get; init; }
        }

        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, CacheEntry> _searchCache = new(StringComparer.OrdinalIgnoreCase);
        private List<Datum> _currentSeasonCache = new();
        private DateTimeOffset _currentSeasonCacheSavedAtUtc = DateTimeOffset.MinValue;
        private DateTimeOffset _lastRateLimitHitUtc = DateTimeOffset.MinValue;
        private const string BaseUrl = "https://api.jikan.moe/v4";
        private const int MaxRetryAttempts = 4;
        private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan CurrentSeasonCacheTtl = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan RateLimitNoticeTtl = TimeSpan.FromSeconds(25);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        // Initialise le client HTTP utilisé pour appeler Jikan.
        public JikanService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AnimeDiscover/1.0");
        }

        // Recherche des animes en combinant plusieurs niveaux de critères.
        public async Task<List<Datum>> SearchAnimeByCriteriaAsync(AnimeApiCriteria criteria)
        {
            try
            {
                criteria ??= new AnimeApiCriteria();

                var result = await ExecuteCriteriaSearchAsync(criteria, includeGenres: true, includeTypeAndStatus: true, includeDatesAndRating: true);
                if (result.Count == 0)
                {
                    result = await ExecuteCriteriaSearchAsync(criteria, includeGenres: false, includeTypeAndStatus: true, includeDatesAndRating: true);
                }
                if (result.Count == 0)
                {
                    result = await ExecuteCriteriaSearchAsync(criteria, includeGenres: false, includeTypeAndStatus: false, includeDatesAndRating: false);
                }

                if (result.Count == 0 && !string.IsNullOrWhiteSpace(criteria.q))
                {
                    using var fallbackResponse = await GetAsyncWithRetry($"{BaseUrl}/anime?q={Uri.EscapeDataString(criteria.q)}&limit=8&sfw=true");
                    fallbackResponse.EnsureSuccessStatusCode();
                    var fallbackRoot = await DeserializeRootAsync(fallbackResponse.Content);
                    result = fallbackRoot?.data ?? new List<Datum>();
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur recherche par critères API: {ex.Message}");
                if (IsRateLimitRecentlyHit())
                {
                    System.Diagnostics.Debug.WriteLine("Rate limit Jikan détecté récemment (429).");
                }

                if (!string.IsNullOrWhiteSpace(criteria?.q))
                {
                    return await SearchAnimeAsync(criteria.q);
                }

                return await GetCurrentSeasonAsync();
            }
        }

        // Exécute un appel de recherche par critères avec options de relâchement.
        private async Task<List<Datum>> ExecuteCriteriaSearchAsync(AnimeApiCriteria criteria, bool includeGenres, bool includeTypeAndStatus, bool includeDatesAndRating)
        {
            var queryParts = new List<string>
            {
                "sfw=true",
                $"limit={Math.Clamp(criteria.limit <= 0 ? 8 : criteria.limit, 1, 25)}"
            };

            if (!string.IsNullOrWhiteSpace(criteria.q))
                queryParts.Add($"q={Uri.EscapeDataString(criteria.q)}");

            if (includeTypeAndStatus)
            {
                if (!string.IsNullOrWhiteSpace(criteria.type))
                    queryParts.Add($"type={Uri.EscapeDataString(criteria.type)}");
                if (!string.IsNullOrWhiteSpace(criteria.status))
                    queryParts.Add($"status={Uri.EscapeDataString(criteria.status)}");
            }

            if (includeDatesAndRating)
            {
                if (!string.IsNullOrWhiteSpace(criteria.rating))
                    queryParts.Add($"rating={Uri.EscapeDataString(criteria.rating)}");
                if (criteria.min_score.HasValue)
                    queryParts.Add($"min_score={criteria.min_score.Value.ToString(CultureInfo.InvariantCulture)}");
                if (!string.IsNullOrWhiteSpace(criteria.start_date))
                    queryParts.Add($"start_date={Uri.EscapeDataString(criteria.start_date)}");
                if (!string.IsNullOrWhiteSpace(criteria.end_date))
                    queryParts.Add($"end_date={Uri.EscapeDataString(criteria.end_date)}");
            }

            if (!string.IsNullOrWhiteSpace(criteria.order_by))
                queryParts.Add($"order_by={Uri.EscapeDataString(criteria.order_by)}");
            if (!string.IsNullOrWhiteSpace(criteria.sort))
                queryParts.Add($"sort={Uri.EscapeDataString(criteria.sort)}");
            if (criteria.page.HasValue && criteria.page.Value > 0)
                queryParts.Add($"page={criteria.page.Value}");

            if (includeGenres && criteria.genre_ids != null && criteria.genre_ids.Count > 0)
            {
                queryParts.Add($"genres={string.Join(",", criteria.genre_ids)}");
            }

            var url = $"{BaseUrl}/anime?{string.Join("&", queryParts)}";
            using var response = await GetAsyncWithRetry(url);
            response.EnsureSuccessStatusCode();

            var root = await DeserializeRootAsync(response.Content);
            return root?.data ?? new List<Datum>();
        }

        // Charge les animes de la saison actuelle depuis Jikan.
        public async Task<List<Datum>> GetCurrentSeasonAsync()
        {
            if (_currentSeasonCache.Count > 0 && DateTimeOffset.UtcNow - _currentSeasonCacheSavedAtUtc <= CurrentSeasonCacheTtl)
            {
                return new List<Datum>(_currentSeasonCache);
            }

            try
            {
                using var response = await GetAsyncWithRetry($"{BaseUrl}/seasons/now?sfw=true");
                response.EnsureSuccessStatusCode();

                var root = await DeserializeRootAsync(response.Content);
                var results = root?.data ?? new List<Datum>();
                if (results.Count > 0)
                {
                    _currentSeasonCache = new List<Datum>(results);
                    _currentSeasonCacheSavedAtUtc = DateTimeOffset.UtcNow;
                }

                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement de la saison: {ex.Message}");
                if (IsRateLimitRecentlyHit())
                {
                    System.Diagnostics.Debug.WriteLine("Rate limit Jikan détecté récemment (429).");
                }

                if (_currentSeasonCache.Count > 0 && DateTimeOffset.UtcNow - _currentSeasonCacheSavedAtUtc <= CurrentSeasonCacheTtl)
                {
                    return new List<Datum>(_currentSeasonCache);
                }

                return new List<Datum>();
            }
        }

        // Recherche des animes par texte avec pagination.
        public async Task<List<Datum>> SearchAnimeAsync(string query, int page = 1, int limit = 25)
        {
            try
            {
                var normalizedQuery = query?.Trim() ?? string.Empty;
                var safeLimit = Math.Clamp(limit, 1, 25);
                var safePage = Math.Max(page, 1);
                var isEmptyQuery = string.IsNullOrWhiteSpace(normalizedQuery);
                var endpoint = isEmptyQuery
                    ? $"{BaseUrl}/top/anime?page={safePage}&limit={safeLimit}&sfw=true"
                    : $"{BaseUrl}/anime?q={Uri.EscapeDataString(normalizedQuery)}&page={safePage}&limit={safeLimit}&sfw=true";

                using var response = await GetAsyncWithRetry(endpoint);
                response.EnsureSuccessStatusCode();

                var root = await DeserializeRootAsync(response.Content);
                var results = root?.data ?? new List<Datum>();

                if (safePage == 1 && results.Count > 0)
                {
                    var cacheKey = isEmptyQuery ? "__top_anime__" : normalizedQuery;
                    _searchCache[cacheKey] = new CacheEntry
                    {
                        Results = new List<Datum>(results),
                        SavedAtUtc = DateTimeOffset.UtcNow
                    };
                }

                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de la recherche: {ex.Message}");
                if (IsRateLimitRecentlyHit())
                {
                    System.Diagnostics.Debug.WriteLine("Rate limit Jikan détecté récemment (429).");
                }

                if (page == 1)
                {
                    var cacheKey = string.IsNullOrWhiteSpace(query) ? "__top_anime__" : query.Trim();
                    if (_searchCache.TryGetValue(cacheKey, out var cachedEntry)
                        && DateTimeOffset.UtcNow - cachedEntry.SavedAtUtc <= SearchCacheTtl
                        && cachedEntry.Results.Count > 0)
                    {
                        return new List<Datum>(cachedEntry.Results);
                    }
                }

                return new List<Datum>();
            }
        }

        // Récupère le détail complet d'un anime par identifiant MAL.
        public async Task<Datum> GetAnimeByIdAsync(int id)
        {
            try
            {
                using var response = await GetAsyncWithRetry($"{BaseUrl}/anime/{id}");
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                var root = await JsonSerializer.DeserializeAsync<SingleItemRoot>(stream, JsonOptions);
                return root?.data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement de l'anime: {ex.Message}");
                return null;
            }
        }

        // Exécute un GET HTTP avec retry sur erreurs temporaires.
        private async Task<HttpResponseMessage> GetAsyncWithRetry(string url)
        {
            HttpResponseMessage response = null;

            for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                var statusCode = (int)response.StatusCode;
                if (statusCode == 429)
                {
                    _lastRateLimitHitUtc = DateTimeOffset.UtcNow;
                }

                if (statusCode != 429 && statusCode < 500)
                {
                    return response;
                }

                if (attempt == MaxRetryAttempts)
                {
                    return response;
                }

                var retryDelayMs = (int)Math.Min(5000, 700 * Math.Pow(2, attempt - 1));
                if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfter)
                {
                    retryDelayMs = (int)Math.Max(1000, retryAfter.TotalMilliseconds);
                }

                response.Dispose();
                await Task.Delay(retryDelayMs);
            }

            return response;
        }

        // Désérialise la réponse Jikan depuis un flux pour limiter les allocations intermédiaires.
        private static async Task<Root?> DeserializeRootAsync(HttpContent content)
        {
            await using var stream = await content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<Root>(stream, JsonOptions);
        }

        // Indique si un rate limit a été détecté récemment.
        private bool IsRateLimitRecentlyHit()
        {
            return DateTimeOffset.UtcNow - _lastRateLimitHitUtc <= RateLimitNoticeTtl;
        }

        // Encapsule une réponse contenant un seul anime.
        private sealed class SingleItemRoot
        {
            public Datum? data { get; set; }
        }
    }
}
