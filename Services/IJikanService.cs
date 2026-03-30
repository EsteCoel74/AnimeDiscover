using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeDiscover.Models;

// Contrat d'accès aux données anime depuis l'API Jikan.
namespace AnimeDiscover.Services
{
    public interface IJikanService
    {
        // Retourne les animes de la saison en cours.
        Task<List<Datum>> GetCurrentSeasonAsync();
        // Recherche des animes par texte avec pagination.
        Task<List<Datum>> SearchAnimeAsync(string query, int page = 1, int limit = 25);
        // Recherche des animes avec critères avancés.
        Task<List<Datum>> SearchAnimeByCriteriaAsync(AnimeApiCriteria criteria);
        // Récupère un anime par identifiant.
        Task<Datum> GetAnimeByIdAsync(int id);
    }
}
