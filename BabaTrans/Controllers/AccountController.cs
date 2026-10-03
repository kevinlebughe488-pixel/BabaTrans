using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Models;
using BabaTrans.ViewModels;

namespace BabaTrans.Controllers
{
    public class AccountController : Controller
    {
        /// <summary>Les 4 rôles RBAC de l'application : tout autre valeur postée est refusée.</summary>
        private static readonly string[] RolesAutorises = { "Administrateur", "Agent", "Livreur", "Client" };

        private readonly UserManager<Utilisateur> _userManager;
        private readonly SignInManager<Utilisateur> _signInManager;
        private readonly BabaTransContext _context;

        public AccountController(
            UserManager<Utilisateur> userManager,
            SignInManager<Utilisateur> signInManager,
            BabaTransContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
        }

        // GET: /Account/Login
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Dashboard");

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null || !user.EstActif)
            {
                ModelState.AddModelError(string.Empty, "Identifiants invalides ou compte désactivé.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(user.UserName!, model.Password, model.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
                return RedirectToLocal(returnUrl);

            ModelState.AddModelError(string.Empty, "Identifiants invalides.");
            return View(model);
        }

        // GET: /Account/Register (Admin uniquement)
        [HttpGet]
        [Authorize(Roles = "Administrateur")]
        public async Task<IActionResult> Register()
        {
            await PreparerFormulaireAsync();
            return View();
        }

        // POST: /Account/Register
        [HttpPost]
        [Authorize(Roles = "Administrateur")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!RolesAutorises.Contains(model.Role))
                ModelState.AddModelError(nameof(model.Role), "Sélectionnez un rôle valide.");

            // Un compte Client est relié à son supermarché par l'adresse email.
            if (model.Role == "Client")
            {
                var supermarche = await _context.Clients.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Email == model.Email);
                if (supermarche == null)
                    ModelState.AddModelError(nameof(model.Email),
                        "Aucun supermarché n'utilise cet email. Enregistrez d'abord le supermarché avec cette adresse, puis créez le compte.");
                else if (!supermarche.EstActif)
                    ModelState.AddModelError(nameof(model.Email), $"Le supermarché « {supermarche.NomSupermarche} » est désactivé.");
            }

            if (!ModelState.IsValid)
            {
                await PreparerFormulaireAsync();
                return View(model);
            }

            var user = new Utilisateur
            {
                UserName = model.Email,
                Email = model.Email,
                Nom = model.Nom.Trim(),
                Prenom = model.Prenom.Trim(),
                EmailConfirmed = true,
                EstActif = true,
                // Les champs propres à un rôle ne sont conservés que pour ce rôle.
                Matricule = model.Role == "Agent" ? model.Matricule?.Trim() : null,
                Immatriculation = model.Role == "Livreur" ? model.Immatriculation?.Trim() : null
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                var roleResult = await _userManager.AddToRoleAsync(user, model.Role);
                if (roleResult.Succeeded)
                {
                    TempData["Success"] = $"L'utilisateur {model.Prenom} {model.Nom} a été créé avec le rôle {model.Role}.";
                    return RedirectToAction(nameof(Users));
                }

                // Ne pas laisser un compte sans rôle derrière soi.
                await _userManager.DeleteAsync(user);
                result = roleResult;
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            await PreparerFormulaireAsync();
            return View(model);
        }

        // GET: /Account/Users (Admin uniquement)
        [Authorize(Roles = "Administrateur")]
        public async Task<IActionResult> Users()
        {
            var users = await _userManager.Users
                .OrderByDescending(u => u.EstActif)
                .ThenBy(u => u.Nom)
                .ToListAsync();

            // Une seule requête pour tous les rôles, au lieu d'une requête par utilisateur.
            var rolesParUtilisateur = (await (
                    from ur in _context.UserRoles
                    join r in _context.Roles on ur.RoleId equals r.Id
                    select new { ur.UserId, r.Name })
                .ToListAsync())
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => (IList<string>)g.Select(x => x.Name!).ToList());

            ViewBag.UserRoles = users
                .Select(u => (User: u, Roles: rolesParUtilisateur.TryGetValue(u.Id, out var roles) ? roles : (IList<string>)new List<string>()))
                .ToList();
            ViewBag.CurrentUserId = _userManager.GetUserId(User);
            return View();
        }

        // POST: /Account/ToggleUser
        [HttpPost]
        [Authorize(Roles = "Administrateur")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUser(string id)
        {
            if (id == _userManager.GetUserId(User))
            {
                TempData["Error"] = "Vous ne pouvez pas désactiver votre propre compte.";
                return RedirectToAction(nameof(Users));
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user != null)
            {
                user.EstActif = !user.EstActif;
                await _userManager.UpdateAsync(user);

                // Invalide les sessions ouvertes : l'utilisateur désactivé est déconnecté sous 5 minutes.
                if (!user.EstActif)
                    await _userManager.UpdateSecurityStampAsync(user);

                TempData["Success"] = user.EstActif
                    ? $"L'utilisateur {user.Prenom} {user.Nom} a été réactivé."
                    : $"L'utilisateur {user.Prenom} {user.Nom} a été désactivé.";
            }
            return RedirectToAction(nameof(Users));
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        /// <summary>Emails des supermarchés actifs, proposés en saisie pour un compte Client.</summary>
        private async Task PreparerFormulaireAsync()
        {
            ViewBag.EmailsSupermarches = await _context.Clients
                .Where(c => c.EstActif && c.Email != null)
                .OrderBy(c => c.NomSupermarche)
                .Select(c => new SelectListItem { Value = c.Email, Text = c.NomSupermarche })
                .ToListAsync();
        }

        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Dashboard");
        }
    }
}
