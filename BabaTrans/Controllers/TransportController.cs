using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
                .Include(m => m.Trajets)
                .ToListAsync();
            return View(moyens);
        }

        // GET: Transport/CreateMoyen
        public IActionResult CreateMoyen()
        {
            return View();
        }

        // POST: Transport/CreateMoyen
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateMoyen([Bind("Type,Immatriculation,Capacite,EstDisponible,Description")] MoyenTransport moyen)
        {
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
                .ToListAsync();
            return View(trajets);
        }

        // GET: Transport/CreateTrajet
        public IActionResult CreateTrajet()
        {
            ViewBag.MoyensTransport = new SelectList(
                _context.MoyensTransport.Where(m => m.EstDisponible),
                "Id",
                "Type");
            return View();
        }

        // POST: Transport/CreateTrajet
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTrajet([Bind("VilleDepart,VilleArrivee,DistanceKm,DureeEstimeeHeures,MoyenTransportId")] Trajet trajet)
        {
            if (ModelState.IsValid)
            {
                _context.Add(trajet);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Trajet créé avec succès.";
                return RedirectToAction(nameof(Trajets));
            }
            ViewBag.MoyensTransport = new SelectList(
                _context.MoyensTransport.Where(m => m.EstDisponible),
                "Id", "Type", trajet.MoyenTransportId);
            return View(trajet);
        }

        // GET: Transport/EditTrajet/5
        public async Task<IActionResult> EditTrajet(int? id)
        {
            if (id == null) return NotFound();
            var trajet = await _context.Trajets.FindAsync(id);
            if (trajet == null) return NotFound();
            ViewBag.MoyensTransport = new SelectList(
                _context.MoyensTransport.Where(m => m.EstDisponible),
                "Id", "Type", trajet.MoyenTransportId);
            return View(trajet);
        }

        // POST: Transport/EditTrajet/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTrajet(int id, [Bind("Id,VilleDepart,VilleArrivee,DistanceKm,DureeEstimeeHeures,MoyenTransportId")] Trajet trajet)
        {
            if (id != trajet.Id) return NotFound();
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
            ViewBag.MoyensTransport = new SelectList(
                _context.MoyensTransport.Where(m => m.EstDisponible),
                "Id", "Type", trajet.MoyenTransportId);
            return View(trajet);
        }
    }
}
