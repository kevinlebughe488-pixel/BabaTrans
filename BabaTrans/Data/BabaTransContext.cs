using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using BabaTrans.Models;

namespace BabaTrans.Data
{
    /// <summary>
    /// Contexte de base de données principal de l'application BABA-Trans.
    /// Hérite de IdentityDbContext pour intégrer ASP.NET Identity.
    /// </summary>
    public class BabaTransContext : IdentityDbContext<Utilisateur>
    {
        public BabaTransContext(DbContextOptions<BabaTransContext> options) : base(options) { }

        public DbSet<Client> Clients { get; set; }
        public DbSet<Commande> Commandes { get; set; }
        public DbSet<Colis> Colis { get; set; }
        public DbSet<TimbreQRCode> TimbresQRCode { get; set; }
        public DbSet<Livraison> Livraisons { get; set; }
        public DbSet<MoyenTransport> MoyensTransport { get; set; }
        public DbSet<PositionLivreur> PositionsLivreur { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configuration TimbreQRCode : relation 1-1 avec Colis
            builder.Entity<TimbreQRCode>()
                .HasOne(t => t.Colis)
                .WithOne(c => c.TimbreQRCode)
                .HasForeignKey<TimbreQRCode>(t => t.ColisId);

            // Configuration Livraison
            builder.Entity<Livraison>()
                .HasOne(l => l.Colis)
                .WithMany(c => c.Livraisons)
                .HasForeignKey(l => l.ColisId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Livraison>()
                .HasOne(l => l.Livreur)
                .WithMany()
                .HasForeignKey(l => l.LivreurId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Livraison>()
                .HasOne(l => l.MoyenTransport)
                .WithMany()
                .HasForeignKey(l => l.MoyenTransportId)
                .OnDelete(DeleteBehavior.SetNull);

            // Configuration Commande
            builder.Entity<Commande>()
                .HasOne(c => c.Client)
                .WithMany(cl => cl.Commandes)
                .HasForeignKey(c => c.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Commande>()
                .Property(c => c.PoidsEstimeKg)
                .HasPrecision(10, 2);

            builder.Entity<Commande>()
                .Property(c => c.DistanceKm)
                .HasPrecision(10, 2);

            // Configuration Colis
            builder.Entity<Colis>()
                .HasOne(c => c.Commande)
                .WithMany(cmd => cmd.Colis)
                .HasForeignKey(c => c.CommandeId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configuration PositionLivreur : l'index sert à lire rapidement la dernière position d'une livraison.
            builder.Entity<PositionLivreur>()
                .HasOne(p => p.Livraison)
                .WithMany(l => l.Positions)
                .HasForeignKey(p => p.LivraisonId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PositionLivreur>()
                .HasIndex(p => new { p.LivraisonId, p.DateEnregistrement });

            // Index unique pour le code de suivi du colis
            builder.Entity<Colis>()
                .HasIndex(c => c.CodeSuivi)
                .IsUnique()
                .HasFilter("[CodeSuivi] IS NOT NULL");

            // Seed : Moyens de transport par défaut
            builder.Entity<MoyenTransport>().HasData(
                new MoyenTransport { Id = 1, Type = "Camion", Capacite = 5000, Description = "Camion grande capacité", EstDisponible = true },
                new MoyenTransport { Id = 2, Type = "Camionnette", Capacite = 1500, Description = "Camionnette de livraison", EstDisponible = true },
                new MoyenTransport { Id = 3, Type = "Moto", Capacite = 50, Description = "Moto de livraison rapide", EstDisponible = true },
                new MoyenTransport { Id = 4, Type = "Vélo cargo", Capacite = 30, Description = "Vélo cargo pour livraison urbaine", EstDisponible = true }
            );
        }
    }
}
