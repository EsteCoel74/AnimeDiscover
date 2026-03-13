using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AnimeDiscover.Models;

namespace AnimeDiscover.Services
{
    public class JikanService : IJikanService
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://api.jikan.moe/v4";

        public JikanService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AnimeDiscover/1.0");
        }

        public async Task<List<Datum>> SearchAnimeByCriteriaAsync(AnimeApiCriteria criteria)
        {
            try
            {
                criteria ??= new AnimeApiCriteria();

                var queryParts = new List<string>
                {
                    "sfw=true",
                    $"limit={Math.Clamp(criteria.limit <= 0 ? 8 : criteria.limit, 1, 25)}"
                };

                if (!string.IsNullOrWhiteSpace(criteria.q))
                    queryParts.Add($"q={Uri.EscapeDataString(criteria.q)}");
                if (!string.IsNullOrWhiteSpace(criteria.type))
                    queryParts.Add($"type={Uri.EscapeDataString(criteria.type)}");
                if (!string.IsNullOrWhiteSpace(criteria.status))
                    queryParts.Add($"status={Uri.EscapeDataString(criteria.status)}");
                if (!string.IsNullOrWhiteSpace(criteria.rating))
                    queryParts.Add($"rating={Uri.EscapeDataString(criteria.rating)}");
                if (!string.IsNullOrWhiteSpace(criteria.order_by))
                    queryParts.Add($"order_by={Uri.EscapeDataString(criteria.order_by)}");
                if (!string.IsNullOrWhiteSpace(criteria.sort))
                    queryParts.Add($"sort={Uri.EscapeDataString(criteria.sort)}");
                if (criteria.min_score.HasValue)
                    queryParts.Add($"min_score={criteria.min_score.Value.ToString(CultureInfo.InvariantCulture)}");
                if (!string.IsNullOrWhiteSpace(criteria.start_date))
                    queryParts.Add($"start_date={Uri.EscapeDataString(criteria.start_date)}");
                if (!string.IsNullOrWhiteSpace(criteria.end_date))
                    queryParts.Add($"end_date={Uri.EscapeDataString(criteria.end_date)}");
                if (criteria.genre_ids != null && criteria.genre_ids.Count > 0)
                    queryParts.Add($"genres={string.Join(",", criteria.genre_ids)}");

                var url = $"{BaseUrl}/anime?{string.Join("&", queryParts)}";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<Root>(json, GetJsonOptions());

                return root?.data ?? new List<Datum>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur recherche par critères API: {ex.Message}");
                return new List<Datum>();
            }
        }

        public async Task<List<Datum>> GetCurrentSeasonAsync()
        {
            try
            {
                var response = await GetAsyncWithRetry($"{BaseUrl}/seasons/now?sfw");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<Root>(json, GetJsonOptions());

                return root?.data ?? new List<Datum>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement de la saison: {ex.Message}");
                return new List<Datum>();
            }
        }

        public async Task<List<Datum>> SearchAnimeAsync(string query)
        {
            try
            {
                var encodedQuery = Uri.EscapeDataString(query);
                var response = await GetAsyncWithRetry($"{BaseUrl}/anime?q={encodedQuery}&limit=8&sfw");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<Root>(json, GetJsonOptions());

                return root?.data ?? new List<Datum>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de la recherche: {ex.Message}");
                return new List<Datum>();
            }
        }

        public async Task<Datum> GetAnimeByIdAsync(int id)
        {
            try
            {
                var response = await GetAsyncWithRetry($"{BaseUrl}/anime/{id}");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                var dataElement = doc.RootElement.GetProperty("data");
                var datum = JsonSerializer.Deserialize<Datum>(dataElement.GetRawText(), GetJsonOptions());

                return datum;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement de l'anime: {ex.Message}");
                return null;
            }
        }

        private async Task<HttpResponseMessage> GetAsyncWithRetry(string url)
        {
            var response = await _httpClient.GetAsync(url);
            if ((int)response.StatusCode == 429)
            {
                await Task.Delay(1200);
                response = await _httpClient.GetAsync(url);
            }

            return response;
        }

        private JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };
        }
    }
}
