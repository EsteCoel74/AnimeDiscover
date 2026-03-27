using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AnimeDiscover.Models;

// Persiste les données utilisateur (liste anime + préférences d'affichage).
namespace AnimeDiscover.Services
{
    public class UserDataService
    {
        private readonly string _dataPath;
        private readonly string _preferencesPath;
        private List<UserAnimeData> _userData = new();
        private AppPreferencesData _preferences = new();

        public UserDataService()
        {
            _dataPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), 
                "AnimeDiscover", "userdata.json");
            _preferencesPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "AnimeDiscover", "preferences.json");
            LoadData();
            LoadPreferences();
        }

        // Retourne le thème utilisateur enregistré.
        public string GetTheme()
        {
            return _preferences?.Theme ?? ThemeManager.LightTheme;
        }

        // Enregistre le thème utilisateur sélectionné.
        public void SaveTheme(string theme)
        {
            _preferences.Theme = ThemeManager.NormalizeTheme(theme);
            PersistPreferences();
        }

        // Retourne la langue UI enregistrée.
        public string GetUiLanguage()
        {
            return UiPreferencesManager.NormalizeLanguage(_preferences?.UiLanguage);
        }

        // Enregistre la langue UI sélectionnée.
        public void SaveUiLanguage(string languageCode)
        {
            _preferences.UiLanguage = UiPreferencesManager.NormalizeLanguage(languageCode);
            PersistPreferences();
        }

        // Retourne l'état enregistré des animations UI.
        public bool GetUiAnimationsEnabled()
        {
            return _preferences?.UiAnimationsEnabled ?? true;
        }

        // Enregistre l'état des animations UI.
        public void SaveUiAnimationsEnabled(bool isEnabled)
        {
            _preferences.UiAnimationsEnabled = isEnabled;
            PersistPreferences();
        }

        // Sauvegarde ou met à jour l'état utilisateur pour un anime.
        public void SaveUserData(int animeId, bool isWatched, int? userScore, int? episodesWatched = null)
        {
            var existing = _userData.Find(x => x.AnimeId == animeId);
            if (existing != null)
            {
                existing.IsWatched = isWatched;
                existing.UserScore = userScore;
                if (episodesWatched.HasValue)
                {
                    existing.EpisodesWatched = episodesWatched;
                }
            }
            else
            {
                _userData.Add(new UserAnimeData 
                { 
                    AnimeId = animeId, 
                    IsWatched = isWatched, 
                    UserScore = userScore,
                    EpisodesWatched = episodesWatched
                });
            }
            PersistData();
        }

        // Retourne les données utilisateur associées à un anime.
        public UserAnimeData GetUserData(int animeId)
        {
            return _userData.Find(x => x.AnimeId == animeId) ?? 
                new UserAnimeData { AnimeId = animeId };
        }

        // Retourne une copie de toutes les données utilisateur stockées.
        public List<UserAnimeData> GetAllUserData()
        {
            return new List<UserAnimeData>(_userData);
        }

        // Charge les données utilisateur anime depuis le disque.
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

        // Persiste les données utilisateur anime dans un fichier JSON.
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

        // Charge les préférences utilisateur depuis le disque.
        private void LoadPreferences()
        {
            try
            {
                if (File.Exists(_preferencesPath))
                {
                    var json = File.ReadAllText(_preferencesPath);
                    _preferences = JsonSerializer.Deserialize<AppPreferencesData>(json) ?? new AppPreferencesData();
                }
            }
            catch { /* Ignorer les erreurs de chargement */ }
        }

        // Persiste les préférences utilisateur dans un fichier JSON.
        private void PersistPreferences()
        {
            try
            {
                var directory = Path.GetDirectoryName(_preferencesPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_preferences, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_preferencesPath, json);
            }
            catch { /* Ignorer les erreurs de sauvegarde */ }
        }

        private class AppPreferencesData
        {
            public string Theme { get; set; } = ThemeManager.LightTheme;
            public string UiLanguage { get; set; } = UiPreferencesManager.DefaultLanguage;
            public bool UiAnimationsEnabled { get; set; } = true;
        }
    }
}
