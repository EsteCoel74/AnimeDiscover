using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// Modèles de données utilisés pour sérialiser les réponses de l'API Jikan.
namespace AnimeDiscover.Models
{
    public class Aired
    {
        public DateTime? from { get; set; }
        public DateTime? to { get; set; }
        public Prop prop { get; set; }
        public string @string { get; set; }
    }

    public class Broadcast
    {
        public string day { get; set; }
        public string time { get; set; }
        public string timezone { get; set; }
        public string @string { get; set; }
    }

    public class Datum
    {
        public int mal_id { get; set; }
        public string url { get; set; }
        public Images images { get; set; }
        public Trailer trailer { get; set; }
        public bool approved { get; set; }
        public List<Title> titles { get; set; }
        public string title { get; set; }
        public string title_english { get; set; }
        public string title_japanese { get; set; }
        public List<string> title_synonyms { get; set; }
        public string type { get; set; }
        public string source { get; set; }
        public int? episodes { get; set; }
        public string status { get; set; }
        public bool airing { get; set; }
        public Aired aired { get; set; }
        public string duration { get; set; }
        public string rating { get; set; }
        public double? score { get; set; }
        public int? scored_by { get; set; }
        public int? rank { get; set; }
        public int? popularity { get; set; }
        public int? members { get; set; }
        public int? favorites { get; set; }
        public string synopsis { get; set; }
        public string background { get; set; }
        public string season { get; set; }
        public int? year { get; set; }
        public Broadcast broadcast { get; set; }
        public List<Producer> producers { get; set; }
        public List<Licensor> licensors { get; set; }
        public List<Studio> studios { get; set; }
        public List<Genre> genres { get; set; }
        public List<object> explicit_genres { get; set; }
        public List<Theme> themes { get; set; }
        public List<Demographic> demographics { get; set; }

        [JsonIgnore]
        public int Id { get => mal_id; set => mal_id = value; }
        [JsonIgnore]
        public string Title
        {
            get => !string.IsNullOrWhiteSpace(title) ? title : (title_english ?? "Sans titre");
            set => title = value;
        }
        [JsonIgnore]
        public string ImageUrl
        {
            get => images?.jpg?.large_image_url
                ?? images?.jpg?.image_url
                ?? images?.webp?.large_image_url
                ?? images?.webp?.image_url;
            set
            {
                images ??= new Images();
                images.jpg ??= new Jpg();
                images.jpg.image_url = value;
                images.jpg.large_image_url = value;
            }
        }
        [JsonIgnore]
        public string Synopsis { get => synopsis ?? "Aucun synopsis disponible"; set => synopsis = value; }
        [JsonIgnore]
        public string Background { get => background ?? string.Empty; set => background = value; }
        [JsonIgnore]
        public string Type { get => type ?? "Inconnu"; set => type = value; }
        [JsonIgnore]
        public List<string> Genres
        {
            get => genres?.Select(g => g.name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>();
            set => genres = value?.Select(n => new Genre { name = n, type = "Genre" }).ToList() ?? new List<Genre>();
        }
        [JsonIgnore]
        public List<string> Themes
        {
            get => themes?.Select(t => t.name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>();
            set => themes = value?.Select(n => new Theme { name = n, type = "Theme" }).ToList() ?? new List<Theme>();
        }
        [JsonIgnore]
        public List<string> Studios
        {
            get => studios?.Select(s => s.name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>();
            set => studios = value?.Select(n => new Studio { name = n, type = "Studio" }).ToList() ?? new List<Studio>();
        }
        [JsonIgnore]
        public List<string> Producers
        {
            get => producers?.Select(p => p.name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>();
            set => producers = value?.Select(n => new Producer { name = n, type = "Producer" }).ToList() ?? new List<Producer>();
        }
        [JsonIgnore]
        public List<string> Licensors
        {
            get => licensors?.Select(l => l.name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>();
            set => licensors = value?.Select(n => new Licensor { name = n, type = "Licensor" }).ToList() ?? new List<Licensor>();
        }
        [JsonIgnore]
        public string TrailerUrl
        {
            get
            {
                var directUrl = ExtractStringValue(trailer?.url);
                if (!string.IsNullOrWhiteSpace(directUrl))
                    return directUrl;

                var youtubeId = ExtractStringValue(trailer?.youtube_id);
                if (!string.IsNullOrWhiteSpace(youtubeId))
                    return $"https://www.youtube.com/watch?v={youtubeId}";

                var embedUrl = trailer?.embed_url;
                if (string.IsNullOrWhiteSpace(embedUrl))
                    return null;

                return ConvertEmbedToWatchUrl(embedUrl);
            }
            set
            {
                trailer ??= new Trailer();
                trailer.embed_url = value;
            }
        }
        [JsonIgnore]
        public int Year { get => year ?? 0; set => year = value; }
        [JsonIgnore]
        public double? Score { get => score; set => score = value; }
        [JsonIgnore]
        public int? ScoredBy { get => scored_by; set => scored_by = value; }
        [JsonIgnore]
        public int Episodes { get => episodes ?? 0; set => episodes = value; }
        [JsonIgnore]
        public string Status { get => status ?? "Inconnu"; set => status = value; }
        [JsonIgnore]
        public string Season { get => season ?? string.Empty; set => season = value; }
        [JsonIgnore]
        public string Source { get => source ?? string.Empty; set => source = value; }
        [JsonIgnore]
        public string Duration { get => duration ?? string.Empty; set => duration = value; }
        [JsonIgnore]
        public string Rating { get => rating ?? string.Empty; set => rating = value; }
        [JsonIgnore]
        public int Rank { get => rank ?? 0; set => rank = value; }
        [JsonIgnore]
        public int Popularity { get => popularity ?? 0; set => popularity = value; }
        [JsonIgnore]
        public int Members { get => members ?? 0; set => members = value; }
        [JsonIgnore]
        public int Favorites { get => favorites ?? 0; set => favorites = value; }
        [JsonIgnore]
        public string Broadcast
        {
            get => broadcast?.@string ?? string.Empty;
            set
            {
                broadcast ??= new Broadcast();
                broadcast.@string = value;
            }
        }

        [JsonIgnore]
        public bool IsWatched { get; set; }
        [JsonIgnore]
        public int? UserScore { get; set; }
        [JsonIgnore]
        public int? EpisodesWatched { get; set; }

        private static string ExtractStringValue(object value)
        {
            if (value is null)
                return null;

            if (value is string s)
                return s;

            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.String)
                    return element.GetString();

                if (element.ValueKind == JsonValueKind.Number || element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                    return element.ToString();
            }

            return value.ToString();
        }

        private static string ConvertEmbedToWatchUrl(string embedUrl)
        {
            if (!Uri.TryCreate(embedUrl, UriKind.Absolute, out var uri))
                return embedUrl;

            if (!uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
                return embedUrl;

            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && segments[0].Equals("embed", StringComparison.OrdinalIgnoreCase))
            {
                var videoId = segments[1];
                return $"https://www.youtube.com/watch?v={videoId}";
            }

            return embedUrl;
        }
    }

    public class Demographic
    {
        public int mal_id { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public string url { get; set; }
    }

    public class From
    {
        public int? day { get; set; }
        public int? month { get; set; }
        public int? year { get; set; }
    }

    public class Genre
    {
        public int mal_id { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public string url { get; set; }
    }

    public class Images
    {
        public Jpg jpg { get; set; }
        public Webp webp { get; set; }
        public object image_url { get; set; }
        public object small_image_url { get; set; }
        public object medium_image_url { get; set; }
        public object large_image_url { get; set; }
        public object maximum_image_url { get; set; }
    }

    public class Items
    {
        public int count { get; set; }
        public int total { get; set; }
        public int per_page { get; set; }
    }

    public class Jpg
    {
        public string image_url { get; set; }
        public string small_image_url { get; set; }
        public string large_image_url { get; set; }
    }

    public class Licensor
    {
        public int mal_id { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public string url { get; set; }
    }

    public class Pagination
    {
        public int last_visible_page { get; set; }
        public bool has_next_page { get; set; }
        public int current_page { get; set; }
        public Items items { get; set; }
    }

    public class Producer
    {
        public int mal_id { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public string url { get; set; }
    }

    public class Prop
    {
        public From from { get; set; }
        public To to { get; set; }
    }

    public class Root
    {
        public Pagination pagination { get; set; }
        public List<Datum> data { get; set; }
    }

    public class Studio
    {
        public int mal_id { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public string url { get; set; }
    }

    public class Theme
    {
        public int mal_id { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public string url { get; set; }
    }

    public class Title
    {
        public string type { get; set; }
        public string title { get; set; }
    }

    public class To
    {
        public int? day { get; set; }
        public int? month { get; set; }
        public int? year { get; set; }
    }

    public class Trailer
    {
        public object youtube_id { get; set; }
        public object url { get; set; }
        public string embed_url { get; set; }
        public Images images { get; set; }
    }

    public class Webp
    {
        public string image_url { get; set; }
        public string small_image_url { get; set; }
        public string large_image_url { get; set; }
    }

    public class UserAnimeData
    {
        public int AnimeId { get; set; }
        public bool IsWatched { get; set; }
        public int? UserScore { get; set; }
        public int? EpisodesWatched { get; set; }
    }

    public class AnimeApiCriteria
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
        public int? page { get; set; }
        public List<int> genre_ids { get; set; } = new();
        public int limit { get; set; } = 8;
    }
}
