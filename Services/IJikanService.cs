using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeDiscover.Models;

namespace AnimeDiscover.Services
{
    public interface IJikanService
    {
        Task<List<Datum>> GetCurrentSeasonAsync();
        Task<List<Datum>> SearchAnimeAsync(string query);
        Task<List<Datum>> SearchAnimeByCriteriaAsync(AnimeApiCriteria criteria);
        Task<Datum> GetAnimeByIdAsync(int id);
    }
}
