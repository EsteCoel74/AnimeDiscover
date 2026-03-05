using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeDiscover.Models;

namespace AnimeDiscover.Services
{
    public interface IJikanService
    {
        Task<List<Anime>> GetCurrentSeasonAsync();
        Task<List<Anime>> SearchAnimeAsync(string query);
        Task<Anime> GetAnimeByIdAsync(int id);
    }
}
