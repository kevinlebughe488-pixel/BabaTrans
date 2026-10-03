using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Models;

namespace BabaTrans.Controllers
{
    [Authorize(Roles = "Administrateur,Agent")]
    public class TransportController : Controller
    {
        private readonly BabaTransContext _context;

        public TransportController(BabaTransContext context)
        {
            _context = context;
        }

        // ======== MOYENS DE TRANSPORT ========

        // GET: Transport/Moyens
        public async Task<IActionResult> Moyens()
        {
            var moyens = await _context.MoyensTransport
                .OrderByDescending(m => m.EstDisponible)
                .ThenBy(m => m.Type)
                .ToListAsync();

            // Véhicules actuellement engagés sur une livraison (affectée ou en route).
            ViewBag.VehiculesEngages = (await _context.Livraisons
                .Where(l => l.MoyenTransportId != null
                    && (l.Statut == StatutLivraison.EnAttente || l.Statut == StatutLivraison.EnCours))
                .Select(l => l.MoyenTransportId!.Value)
                .ToListAsync()).ToHashSet();

            return View(moyens);
        }

        // GET: Transport/CreateMoyen
        public IActionResult CreateMoyen()
        {
            // Pas de modèle : le champ capacité reste vide au lieu d'afficher « 0 ».
            return View();
        }

        // POST: Transport/CreateMoyen
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateMoyen([Bind("Type,Immatriculation,Capacite,EstDisponible,Description")] MoyenTransport moyen)
        {
            moyen.Immatriculation = NormaliserImmatriculation(moyen.Immatriculation);
            await ValiderImmatriculationUniqueAsync(moyen.Immatriculation, idMoyen: null);

            if (ModelState.IsValid)
            {
                _context.Add(moyen);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Moyen de transport créé avec succès.";
                return RedirectToAction(nameof(Moyens));
            }
            return View(moyen);
        }

        // GET: Transport/EditMoyen/5
        public async Task<IActionResult> EditMoyen(int? id)
        {
            if (id == null) return NotFound();
            var moyen = await _context.MoyensTransport.FindAsync(id);
            if (moyen == null) return NotFound();
            return View(moyen);
        }

        // POST: Transport/EditMoyen/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMoyen(int id, [Bind("Id,Type,Immatriculation,Capacite,EstDisponible,Description")] MoyenTransport moyen)
        {
            if (id != moyen.Id) return NotFound();

            moyen.Immatriculation = NormaliserImmatriculation(moyen.Immatriculation);
            await ValiderImmatriculationUniqueAsync(moyen.Immatriculation, idMoyen: id);

            if (ModelState.IsValid)
            {
                var moyenExistant = await _context.MoyensTransport.FindAsync(id);
                if (moyenExistant == null) return NotFound();

                moyenExistant.Type = moyen.Type;
                moyenExistant.Immatriculation = moyen.Immatriculation;
                moyenExistant.Capacite = moyen.Capacite;
                moyenExistant.EstDisponible = moyen.EstDisponible;
                moyenExistant.Description = moyen.Description;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Moyen de transport modifié avec succès.";
                return RedirectToAction(nameof(Moyens));
            }
            return View(moyen);
        }

        private async Task ValiderImmatriculationUniqueAsync(string? immatriculation, int? idMoyen)
        {
            if (string.IsNullOrEmpty(immatriculation))
                return;

            if (await _context.MoyensTransport.AnyAsync(m => m.Immatriculation == immatriculation && m.Id != idMoyen))
                ModelState.AddModelError(nameof(MoyenTransport.Immatriculation), "Cette immatriculation est déjà enregistrée.");
        }

        private static string? NormaliserImmatriculation(string? immatriculation)
            => string.IsNullOrWhiteSpace(immatriculation) ? null : immatriculation.Trim().ToUpperInvariant();
    }
}
