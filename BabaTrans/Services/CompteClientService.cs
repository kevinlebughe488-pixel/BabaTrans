using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Models;

namespace BabaTrans.Services
{
    /// <summary>
    /// Retrouve le supermarché lié au compte Client connecté.
    /// Le lien se fait par l'adresse email : celle du compte doit être celle du supermarché.
    /// </summary>
    public class CompteClientService
    {
        private readonly BabaTransContext _context;
        private Client? _client;
        private bool _dejaCharge;

        public CompteClientService(BabaTransContext context)
        {
            _context = context;
        }

        public async Task<Client?> GetClientConnecteAsync(ClaimsPrincipal user)
        {
            if (_dejaCharge)
                return _client;

            _dejaCharge = true;
            if (!user.IsInRole("Client"))
                return null;

            var email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name;
            if (string.IsNullOrWhiteSpace(email))
                return null;

            _client = await _context.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Email == email);
            return _client;
        }
    }
}
