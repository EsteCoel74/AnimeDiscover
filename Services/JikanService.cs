using System;
using System.Collections.Generic;
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
        }

        public async Task<List<Anime>> GetCurrentSeasonAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/seasons/now?sfw");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<AnimeDataResponse>(json, GetJsonOptions());

                return MapToAnimeList(root?.data ?? new());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement de la saison: {ex.Message}");
                return new List<Anime>();
            }
        }

        public async Task<List<Anime>> SearchAnimeAsync(string query)
        {
            try
            {
                var encodedQuery = Uri.EscapeDataString(query);
                var response = await _httpClient.GetAsync($"{BaseUrl}/anime?q={encodedQuery}&limit=5&sfw");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<AnimeDataResponse>(json, GetJsonOptions());

                return MapToAnimeList(root?.data ?? new());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de la recherche: {ex.Message}");
                return new List<Anime>();
            }
        }

        public async Task<Anime> GetAnimeByIdAsync(int id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/anime/{id}");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                var dataElement = doc.RootElement.GetProperty("data");
                var datum = JsonSerializer.Deserialize<Datum>(dataElement.GetRawText(), GetJsonOptions());

                return datum != null ? MapToAnime(datum) : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement de l'anime: {ex.Message}");
                return null;
            }
        }

        private List<Anime> MapToAnimeList(List<Datum> data)
        {
            var result = new List<Anime>();
            foreach (var datum in data)
            {
                result.Add(MapToAnime(datum));
            }
            return result;
        }

        private Anime MapToAnime(Datum datum)
        {
            var genres = new List<string>();
            if (datum.genres != null)
            {
                foreach (var genre in datum.genres)
                {
                    genres.Add(genre.name);
                }
            }

            var themes = new List<string>();
            if (datum.themes != null)
            {
                foreach (var theme in datum.themes)
                {
                    themes.Add(theme.name);
                }
            }

            var studios = new List<string>();
            if (datum.studios != null)
            {
                foreach (var studio in datum.studios)
                {
                    studios.Add(studio.name);
                }
            }

            var producers = new List<string>();
            if (datum.producers != null)
            {
                foreach (var producer in datum.producers)
                {
                    producers.Add(producer.name);
                }
            }

            var licensors = new List<string>();
            if (datum.licensors != null)
            {
                foreach (var licensor in datum.licensors)
                {
                    licensors.Add(licensor.name);
                }
            }

            var broadcastString = datum.broadcast?.@string ?? "";

            return new Anime
            {
                Id = datum.mal_id,
                Title = datum.title ?? datum.title_english ?? "Sans titre",
                ImageUrl = datum.images?.jpg?.large_image_url ?? datum.images?.jpg?.image_url,
                Synopsis = datum.synopsis ?? "Aucun synopsis disponible",
                Background = datum.background ?? "",
                Type = datum.type ?? "Inconnu",
                Genres = genres,
                Themes = themes,
                Studios = studios,
                Producers = producers,
                Licensors = licensors,
                TrailerUrl = datum.trailer?.embed_url,
                Year = datum.year,
                Score = datum.score,
                ScoredBy = datum.scored_by,
                Episodes = datum.episodes ?? 0,
                Status = datum.status ?? "Inconnu",
                Season = datum.season ?? "",
                Source = datum.source ?? "",
                Duration = datum.duration ?? "",
                Rating = datum.rating ?? "",
                Rank = datum.rank,
                Popularity = datum.popularity,
                Members = datum.members,
                Favorites = datum.favorites,
                Broadcast = broadcastString
            };
        }

        private JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }
    }
}
