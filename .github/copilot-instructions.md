# Instructions Copilot pour BabaTrans

## Objectif

BabaTrans est une application de gestion logistique et de transport pour des supermarchés partenaires en RDC. Toute modification doit préserver le flux métier : commande, colis, QR code, affectation, scans de livraison, confirmation et suivi.

## Stack obligatoire

- ASP.NET Core MVC avec .NET 10.
- C# avec `Nullable` activé et les conventions modernes du langage.
- Entity Framework Core 9 avec SQL Server et approche Code First.
- ASP.NET Core Identity avec autorisation par rôles.
- QRCoder pour les timbres QR.
- Bootstrap 5 et le CSS existant dans `BabaTrans/wwwroot/css/site.css` pour l'interface.

Ne pas introduire une autre architecture, un autre ORM, Tailwind, une SPA ou une nouvelle bibliothèque sans justification explicite.

## Méthode de développement

Pour chaque fonctionnalité :

1. Comprendre le besoin métier et consulter `contexte_projet.md`.
2. Identifier les contrôleurs, modèles, services, ViewModels et vues concernés.
3. Proposer un plan court avant une modification importante.
4. Modifier le minimum de fichiers nécessaire.
5. Vérifier les autorisations, la validation, les cas d'erreur et les effets sur la base de données.
6. Compiler avec `dotnet build` après les changements C# ou Razor.
7. Signaler clairement les tests qui ne peuvent pas être exécutés.

## Architecture du projet

- `Controllers/` : orchestration HTTP et autorisation, pas de logique métier complexe.
- `Models/` : entités EF Core et énumérations métier.
- `ViewModels/` : modèles dédiés aux formulaires et aux affichages ; ne pas exposer inutilement les entités dans les formulaires.
- `Data/` : `BabaTransContext`, configuration EF Core et initialisation des données.
- `Services/` : logique métier réutilisable, notamment QR code et tarification.
- `Views/` : Razor MVC, validation côté serveur et affichage accessible.
- `wwwroot/` : styles et scripts existants.

## Sécurité et RBAC

- Utiliser `[Authorize]` et `[Authorize(Roles = "...")]` sur les contrôleurs ou actions concernés.
- Respecter les rôles métier existants : `Administrateur`, `Agent`, `Livreur`, `Client`.
- Ne jamais faire confiance à un identifiant, un rôle ou un statut fourni par le navigateur.
- Vérifier côté serveur la propriété des ressources et les transitions de statut.
- Utiliser les mécanismes antiforgery MVC pour les formulaires POST.
- Ne jamais ajouter de mot de passe, chaîne de connexion, clé ou secret dans le code, les vues ou les nouveaux fichiers.
- Ne pas recopier les identifiants de démonstration dans la documentation ou les réponses de code.

## EF Core et données

- Utiliser les requêtes asynchrones (`Async`/`await`) dans les actions et services d'accès aux données.
- Utiliser `AsNoTracking()` pour les lectures qui ne sont pas destinées à être modifiées.
- Charger explicitement les relations nécessaires et éviter les requêtes répétées dans les boucles.
- Valider les références et les contraintes métier avant `SaveChangesAsync()`.
- Préserver les relations et comportements de suppression définis dans `BabaTransContext`.
- Pour toute modification de schéma, expliquer si une migration EF Core est nécessaire ; ne pas masquer un problème avec une recréation destructive de la base.
- Garantir l'unicité du `CodeSuivi` des colis et éviter les collisions lors de sa génération.

## Flux métier à préserver

- Une commande appartient à un client et peut contenir plusieurs colis.
- Un colis possède un code de suivi unique et un timbre QR.
- Seuls les utilisateurs autorisés peuvent créer, affecter, scanner ou confirmer une livraison.
- Les scans doivent respecter l'ordre des statuts et enregistrer les dates correspondantes.
- La consultation du suivi ne doit pas révéler de données sensibles.

## Formulaires et interface

- Préférer les ViewModels aux entités directement liées aux champs utilisateur.
- Ajouter les annotations de validation et vérifier aussi les données côté serveur.
- Réafficher les erreurs de validation avec les helpers Razor existants.
- Conserver Bootstrap 5, le thème blanc et bleu ciel et les composants déjà présents.
- Ne pas supprimer une action ou un lien visible sans vérifier les besoins des quatre rôles.

## Vérification attendue

Avant de considérer une tâche terminée, vérifier :

- compilation sans nouvelle erreur avec `dotnet build` ;
- autorisation correcte pour chaque rôle concerné ;
- validation des entrées et protection antiforgery ;
- absence de secrets dans les fichiers modifiés ;
- cohérence des statuts commande, colis et livraison ;
- fonctionnement des vues concernées sur les cas nominal et erreur.

Quand une fonctionnalité touche l'authentification, les autorisations, les données ou le suivi des colis, proposer des tests ciblés avant de conclure.