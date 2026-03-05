using System;
using System.Collections.Generic;

namespace AnimeDiscover.Models
{
    public class Anime
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string ImageUrl { get; set; }
        public string Synopsis { get; set; }
        public string Background { get; set; }
        public string Type { get; set; }
        public List<string> Genres { get; set; } = new();
        public List<string> Themes { get; set; } = new();
        public string TrailerUrl { get; set; }
        public int Year { get; set; }
        public double? Score { get; set; }
        public int? ScoredBy { get; set; }
        public int Episodes { get; set; }
        public string Status { get; set; }
        public string Season { get; set; }
        public string Source { get; set; }
        public string Duration { get; set; }
        public string Rating { get; set; }
        public int Rank { get; set; }
        public int Popularity { get; set; }
        public int Members { get; set; }
        public int Favorites { get; set; }
        public string Broadcast { get; set; }
        public List<string> Studios { get; set; } = new();
        public List<string> Producers { get; set; } = new();
        public List<string> Licensors { get; set; } = new();

        // Données utilisateur
        public bool IsWatched { get; set; }
        public int? UserScore { get; set; }
    }

    public class UserAnimeData
    {
        public int AnimeId { get; set; }
        public bool IsWatched { get; set; }
        public int? UserScore { get; set; }
    }
}
