using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Helpers;
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
                .Include(m => m.Trajets)
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

        // ======== TRAJETS ========

        // GET: Transport/Trajets
        public async Task<IActionResult> Trajets()
        {
            var trajets = await _context.Trajets
                .Include(t => t.MoyenTransport)
                .Include(t => t.Commandes)
                .OrderBy(t => t.VilleDepart).ThenBy(t => t.VilleArrivee)
                .ToListAsync();
            return View(trajets);
        }

        // GET: Transport/CreateTrajet
        public async Task<IActionResult> CreateTrajet()
        {
            await PreparerListeVehiculesAsync(null);
            return View();
        }

        // POST: Transport/CreateTrajet
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTrajet([Bind("VilleDepart,VilleArrivee,DistanceKm,DureeEstimeeHeures,MoyenTransportId")] Trajet trajet)
        {
            await ValiderTrajetAsync(trajet);

            if (ModelState.IsValid)
            {
                _context.Add(trajet);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Trajet créé avec succès.";
                return RedirectToAction(nameof(Trajets));
            }
            await PreparerListeVehiculesAsync(trajet.MoyenTransportId);
            return View(trajet);
        }

        // GET: Transport/EditTrajet/5
        public async Task<IActionResult> EditTrajet(int? id)
        {
            if (id == null) return NotFound();
            var trajet = await _context.Trajets.FindAsync(id);
            if (trajet == null) return NotFound();
            await PreparerListeVehiculesAsync(trajet.MoyenTransportId);
            return View(trajet);
        }

        // POST: Transport/EditTrajet/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTrajet(int id, [Bind("Id,VilleDepart,VilleArrivee,DistanceKm,DureeEstimeeHeures,MoyenTransportId")] Trajet trajet)
        {
            if (id != trajet.Id) return NotFound();
            await ValiderTrajetAsync(trajet);

            if (ModelState.IsValid)
            {
                var trajetExistant = await _context.Trajets.FindAsync(id);
                if (trajetExistant == null) return NotFound();

                trajetExistant.VilleDepart = trajet.VilleDepart;
                trajetExistant.VilleArrivee = trajet.VilleArrivee;
                trajetExistant.DistanceKm = trajet.DistanceKm;
                trajetExistant.DureeEstimeeHeures = trajet.DureeEstimeeHeures;
                trajetExistant.MoyenTransportId = trajet.MoyenTransportId;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Trajet modifié avec succès.";
                return RedirectToAction(nameof(Trajets));
            }
            await PreparerListeVehiculesAsync(trajet.MoyenTransportId);
            return View(trajet);
        }

        private async Task ValiderTrajetAsync(Trajet trajet)
        {
            trajet.VilleDepart = trajet.VilleDepart?.Trim() ?? string.Empty;
            trajet.VilleArrivee = trajet.VilleArrivee?.Trim() ?? string.Empty;

            if (!string.IsNullOrEmpty(trajet.VilleDepart) && StatutHelper.MemeVille(trajet.VilleDepart, trajet.VilleArrivee))
                ModelState.AddModelError(nameof(Trajet.VilleArrivee), "La ville d'arrivée doit être différente de la ville de départ.");

            var existeDeja = (await _context.Trajets
                    .Where(t => t.Id != trajet.Id)
                    .Select(t => new { t.VilleDepart, t.VilleArrivee })
                    .ToListAsync())
                .Any(t => StatutHelper.MemeVille(t.VilleDepart, trajet.VilleDepart)
                       && StatutHelper.MemeVille(t.VilleArrivee, trajet.VilleArrivee));
            if (existeDeja)
                ModelState.AddModelError(string.Empty, $"Le trajet {trajet.VilleDepart} → {trajet.VilleArrivee} existe déjà.");

            if (trajet.MoyenTransportId.HasValue && !await _context.MoyensTransport.AnyAsync(m => m.Id == trajet.MoyenTransportId))
                ModelState.AddModelError(nameof(Trajet.MoyenTransportId), "Le véhicule sélectionné n'existe pas.");
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

        /// <summary>Véhicules disponibles, plus le véhicule déjà associé au trajet même s'il est indisponible.</summary>
        private async Task PreparerListeVehiculesAsync(int? moyenTransportId)
        {
            var vehicules = await _context.MoyensTransport
                .Where(m => m.EstDisponible || m.Id == moyenTransportId)
                .OrderBy(m => m.Type)
                .ToListAsync();

            ViewBag.MoyensTransport = new SelectList(
                vehicules.Select(m => new
                {
                    m.Id,
                    Display = $"{m.Type} · {m.Immatriculation ?? "sans immatriculation"} · {m.Capacite:N0} kg"
                              + (m.EstDisponible ? "" : " (indisponible)")
                }),
                "Id", "Display", moyenTransportId);
        }
    }
}
