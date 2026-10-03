using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Data;
using BabaTrans.Helpers;
using BabaTrans.Models;
using BabaTrans.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration Entity Framework Core avec SQL Server
builder.Services.AddDbContext<BabaTransContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configuration ASP.NET Identity avec RBAC
builder.Services.AddIdentity<Utilisateur, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<BabaTransContext>()
.AddClaimsPrincipalFactory<BabaTransClaimsPrincipalFactory>()
.AddDefaultTokenProviders();

// Revalide le cookie toutes les 5 minutes : un compte désactivé est déconnecté rapidement.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});

// Configuration des cookies d'authentification
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// Enregistrer les services
builder.Services.AddScoped<QRCodeService>();
builder.Services.AddScoped<TarificationService>();
builder.Services.AddScoped<CompteClientService>();

// Géolocalisation : les itinéraires calculés sont gardés en cache pour ne pas solliciter le service routier à chaque saisie.
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<GeolocalisationService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Geolocalisation:DelaiItineraireSecondes", 5));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BabaTrans/1.0");
});

// Ajouter MVC, avec les messages d'erreur de saisie en français
builder.Services.AddControllersWithViews(options =>
{
    var messages = options.ModelBindingMessageProvider;
    messages.SetValueMustBeANumberAccessor(champ => $"Le champ « {champ} » doit être un nombre.");
    messages.SetAttemptedValueIsInvalidAccessor((valeur, champ) => $"La valeur « {valeur} » n'est pas valide pour « {champ} ».");
    messages.SetValueMustNotBeNullAccessor(champ => $"Le champ « {champ} » est obligatoire.");
    messages.SetValueIsInvalidAccessor(valeur => $"La valeur « {valeur} » n'est pas valide.");
    messages.SetMissingBindRequiredValueAccessor(champ => $"Le champ « {champ} » est obligatoire.");
    messages.SetUnknownValueIsInvalidAccessor(champ => $"La valeur saisie pour « {champ} » n'est pas valide.");
});

var app = builder.Build();

// Seed de la base de données
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<BabaTransContext>();
    context.Database.EnsureCreated();
    await DbInitializer.SeedAsync(scope.ServiceProvider);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// Affichage en français (dates, séparateur de milliers) quelle que soit la langue de Windows.
// Le séparateur décimal reste le point : c'est le format envoyé par les champs numériques des navigateurs.
var cultureApplication = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
cultureApplication.NumberFormat.NumberDecimalSeparator = ".";
cultureApplication.NumberFormat.CurrencyDecimalSeparator = ".";
var optionsLocalisation = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(cultureApplication),
    SupportedCultures = new[] { cultureApplication },
    SupportedUICultures = new[] { cultureApplication }
};
optionsLocalisation.RequestCultureProviders.Clear();
app.UseRequestLocalization(optionsLocalisation);

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
