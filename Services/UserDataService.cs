using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AnimeDiscover.Models;

namespace AnimeDiscover.Services
{
    public class UserDataService
    {
        private readonly string _dataPath;
        private List<UserAnimeData> _userData = new();

        public UserDataService()
        {
            _dataPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), 
                "AnimeDiscover", "userdata.json");
            LoadData();
        }

        public void SaveUserData(int animeId, bool isWatched, int? userScore)
        {
            var existing = _userData.Find(x => x.AnimeId == animeId);
            if (existing != null)
            {
                existing.IsWatched = isWatched;
                existing.UserScore = userScore;
            }
            else
            {
                _userData.Add(new UserAnimeData 
                { 
                    AnimeId = animeId, 
                    IsWatched = isWatched, 
                    UserScore = userScore 
                });
            }
            PersistData();
        }

        public UserAnimeData GetUserData(int animeId)
        {
            return _userData.Find(x => x.AnimeId == animeId) ?? 
                new UserAnimeData { AnimeId = animeId };
        }

        public List<UserAnimeData> GetAllUserData()
        {
            return new List<UserAnimeData>(_userData);
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    var json = File.ReadAllText(_dataPath);
                    _userData = JsonSerializer.Deserialize<List<UserAnimeData>>(json) ?? new();
                }
            }
            catch { /* Ignorer les erreurs de chargement */ }
        }

        private void PersistData()
        {
            try
            {
                var directory = Path.GetDirectoryName(_dataPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(_userData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_dataPath, json);
            }
            catch { /* Ignorer les erreurs de sauvegarde */ }
        }
    }
}
