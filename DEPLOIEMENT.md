# Velotech : Stripe, base Azure SQL et hébergement gratuit

## Déploiement en place

| Brique | Ressource Azure (groupe `velotech-rg`) | Région | URL |
|---|---|---|---|
| Front | Static Web App `velotech-front` (Free) | contenu distribué mondialement | https://yellow-mud-0bbbc800f.3.azurestaticapps.net |
| API | App Service `velotech-api` (F1 Free, Linux, .NET 8) | France Central | https://velotech-api.azurewebsites.net (`/health`) |
| Base | Azure SQL `velotech-sql` / `VelotechDB` (offre gratuite, pause auto) | France Central | `velotech-sql.database.windows.net` |

West Europe et Belgium Central refusaient les nouveaux abonnements, et l'offre SQL gratuite n'existe pas en Belgium Central : d'où France Central (Paris).

Sécurité, sans aucun mot de passe stocké :
- **Base** : authentification Microsoft Entra uniquement. L'API s'y connecte par **identité managée** (`Authentication=Active Directory Managed Identity`), avec les rôles `db_datareader`, `db_datawriter` et `db_ddladmin` (migrations au démarrage).
- **Déploiement GitHub → Azure** : **OIDC** (application Entra `velotech-github-deploy`, credentials fédérés sur la branche `main` des deux dépôts, rôle Contributor limité à `velotech-rg`). Les dépôts n'ont que 3 variables non secrètes : `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
- **Secrets de l'API** (`Jwt__Key`, `Stripe__SecretKey`, `Stripe__WebhookSecret`) : définis par `configurer-secrets-prod.ps1`, script local non versionné.

Un push sur `main` redéploie automatiquement. Les sections 2 à 5 ci-dessous décrivent la méthode manuelle par le portail, en alternative.

## Architecture cible (0 € / mois)

| Brique | Service | Offre | Coût |
|---|---|---|---|
| Front Angular | Azure Static Web Apps | Free | 0 € |
| API .NET 8 | Azure App Service (Linux) | F1 Free | 0 € |
| Base de données | Azure SQL Database (SQL Server) | Free offer (serverless) | 0 € |
| Paiement | Stripe Checkout | Sans abonnement | commission par transaction uniquement |

Pourquoi Azure SQL : l'API utilise déjà SQL Server (LocalDB) via EF Core. Rester sur SQL Server = **aucun changement de code ni de migration**. Passer à MySQL obligerait à changer de provider EF et à régénérer toutes les migrations, sans aucun gain.

Compte Azure : avec une adresse e-mail d'école, **Azure for Students** donne 100 $ de crédit sans carte bancaire. Sinon, un compte Azure gratuit classique (carte demandée pour vérifier l'identité, rien n'est facturé avec les offres ci-dessus).

> Limites du gratuit : l'API F1 s'endort après environ 20 min sans trafic (premier appel environ 10 à 20 s) et dispose de 60 min de CPU par jour. La base gratuite se met en pause après 1 h d'inactivité (premier appel environ 30 à 60 s ; l'API réessaie automatiquement). C'est suffisant pour une démo ou un TFE. En production réelle, passer l'API en B1 (environ 13 €/mois).

---

## 1. Stripe

### 1.1 Compte, sandboxes et clés
1. Créer un compte sur https://dashboard.stripe.com/register (pays : Belgique).
2. Créer **deux sandboxes** (menu des comptes → *Sandboxes*), recommandation Stripe :
   - `velotech-local` pour le développement sur ton PC,
   - `velotech-demo` pour la version en ligne.
   Chaque sandbox a ses propres clés, webhooks et données de test : les paiements locaux ne se mélangent pas avec ceux du site en ligne.
3. Dans chaque sandbox, créer une **clé restreinte** (*Developers → API keys → Create restricted key*), plus sûre qu'une clé secrète `sk_` car elle ne peut faire que ce qu'on lui autorise :
   - Nom : `velotech-api`,
   - Permission **Checkout Sessions : Write** (tout le reste : None),
   - copier la clé `rk_test_...`.
4. *Settings → Payment methods* : activer **Bancontact** (indispensable en Belgique), les cartes, Apple Pay et Google Pay. Le code n'a rien à changer : Stripe affiche automatiquement les moyens de paiement activés.

### 1.2 Configuration locale (jamais dans Git)
Depuis `Velotech.API/` (clé de la sandbox `velotech-local`) :
```bash
dotnet user-secrets set "Stripe:SecretKey" "rk_test_..."
```

### 1.3 Webhook en local (Stripe CLI)
1. Installer la CLI : `winget install Stripe.StripeCli`, puis `stripe login`.
2. Lancer :
   ```bash
   stripe listen --events checkout.session.completed,checkout.session.async_payment_succeeded,checkout.session.async_payment_failed,checkout.session.expired --forward-to http://localhost:5094/api/payments/webhook
   ```
3. Copier le `whsec_...` affiché :
   ```bash
   dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..."
   ```

Sans webhook en local, la page de retour `/payment/success` interroge quand même Stripe et valide le paiement. Le webhook reste nécessaire pour libérer le stock des sessions expirées.

### 1.4 Tester
- Carte acceptée : `4242 4242 4242 4242`, date future, n'importe quel CVC.
- Carte refusée : `4000 0000 0000 0002`.
- 3D Secure : `4000 0027 6000 3184`.
- Bancontact : choisir Bancontact sur la page Stripe, puis « Authorize test payment ».

### 1.5 Flux implémenté
```
Front (checkout)  ->  POST /api/orders                       commande "PendingPayment", stock réservé
                  ->  POST /api/payments/checkout-session    montant calculé côté serveur, session Stripe
                  ->  redirection vers checkout.stripe.com
Stripe            ->  POST /api/payments/webhook             signature vérifiée -> paiement "Paid", commande "Paid", notification
                  ->  retour /payment/success                le front vérifie le statut, vide le panier, affiche la confirmation
Abandon           ->  /payment/cancel                        session expirée, commande "Cancelled", stock restitué, panier conservé
```
Endpoints : `POST /api/payments/checkout-session`, `GET /api/payments/checkout-session/{sessionId}`, `POST /api/payments/{id}/cancel`, `POST /api/payments/webhook`.
`POST /api/payments` (ancien paiement simulé) est désormais réservé au staff (Admin, Manager) pour les encaissements en magasin.

---

## 2. Base de données : Azure SQL (offre gratuite)

1. Portail Azure : *Create a resource → Azure SQL → SQL databases → Single database*.
2. Sur la page de création, cliquer sur la bannière **« Apply offer »** (Free serverless database).
3. Paramètres :
   - Resource group : `velotech-rg`, région : **West Europe**.
   - Database name : `VelotechDB`.
   - Server : nouveau serveur, par exemple `velotech-sql`, authentification **SQL** (login `velotechadmin` et un mot de passe fort).
   - Behavior when free limit reached : **Auto-pause until next month** (garantit 0 €).
4. *Networking* : Public endpoint, cocher **« Allow Azure services and resources to access this server »** et **« Add current client IP address »**.
5. Chaîne de connexion (*Connection strings → ADO.NET*) :
   ```
   Server=tcp:velotech-sql.database.windows.net,1433;Initial Catalog=VelotechDB;User ID=velotechadmin;Password=<MDP>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;
   ```

### Transférer les données locales
Option A : garder les données actuelles. `velotech-db.bacpac` est l'export de la LocalDB (dans `Documents/Velotech/`). Pour le régénérer :
```bash
dotnet tool install -g microsoft.sqlpackage --version 162.1.172
sqlpackage /Action:Export /SourceServerName:"(localdb)\mssqllocaldb" /SourceDatabaseName:VelotechDB /SourceTrustServerCertificate:True /TargetFile:velotech-db.bacpac
```
Import dans la base Azure (qui doit être vide) :
```bash
sqlpackage /Action:Import /SourceFile:velotech-db.bacpac /TargetConnectionString:"<chaîne de connexion Azure>"
```

Option B : partir de zéro. Ne rien faire. Au démarrage, l'API applique les migrations EF et lance le `DbSeeder` (magasins, produits, comptes de démo).

---

## 3. API : Azure App Service (F1 gratuit)

1. *Create a resource → Web App* :
   - Name : `velotech-api` (sinon un nom libre ; noter l'URL `https://<nom>.azurewebsites.net`).
   - Publish : Code ; Runtime : **.NET 8 (LTS)** ; OS : **Linux** ; région : West Europe.
   - Pricing plan : **Free F1**.
   - *Deployment* : laisser la livraison continue **désactivée** (le workflow fourni s'en charge).
2. *Settings → Environment variables → App settings* :

   | Nom | Valeur |
   |---|---|
   | `ConnectionStrings__DefaultConnection` | chaîne de connexion Azure SQL |
   | `Jwt__Key` | clé aléatoire longue (voir ci-dessous) |
   | `Stripe__SecretKey` | `rk_test_...` de la sandbox `velotech-demo` (puis `rk_live_...` en production) |
   | `Stripe__WebhookSecret` | `whsec_...` du webhook de prod (étape 5) |
   | `App__FrontendUrl` | `https://<static-web-app>.azurestaticapps.net` |
   | `Cors__AllowedOrigins__0` | `https://<static-web-app>.azurestaticapps.net` |

   Générer une clé JWT (PowerShell) :
   ```powershell
   [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
   ```
   La clé de `appsettings.json` est publique sur GitHub : il faut absolument la remplacer en production.
3. *Settings → Configuration → General settings* : **HTTPS Only = On** et **SCM Basic Auth Publishing Credentials = On** (nécessaire pour le profil de publication).
4. *Overview → Download publish profile*.
5. Dépôt GitHub `velotech-api` → *Settings → Secrets and variables → Actions* :
   - Secret `AZURE_WEBAPP_PUBLISH_PROFILE` = contenu du fichier téléchargé.
   - Variable `AZURE_WEBAPP_NAME` = `velotech-api`.
6. Pousser sur `main` : le workflow `.github/workflows/deploy-api.yml` publie l'API.
7. Vérifier : `https://velotech-api.azurewebsites.net/health` doit répondre `"ok"`.

---

## 4. Front : Azure Static Web Apps (Free)

1. Dans `velotech-front/src/environments/environment.ts`, mettre l'URL réelle de l'API :
   `apiUrl: 'https://velotech-api.azurewebsites.net/api'`.
2. *Create a resource → Static Web App* : plan **Free**, *Deployment source* : **Other**.
3. *Overview → Manage deployment token* : copier le jeton.
4. Dépôt GitHub `velotech-front` → secret `AZURE_STATIC_WEB_APPS_API_TOKEN` = le jeton.
5. Pousser sur `main` : le workflow `.github/workflows/deploy-front.yml` build Angular (Node 22) et déploie.
   `public/staticwebapp.config.json` redirige toutes les routes Angular vers `index.html` (sinon un rafraîchissement sur `/my-orders` renverrait une 404).
6. Reporter l'URL `https://xxx.azurestaticapps.net` dans `App__FrontendUrl` et `Cors__AllowedOrigins__0` de l'API (étape 3.2).

Domaine perso (par exemple velotech.be) : Static Web Apps Free gère un domaine perso avec certificat HTTPS gratuit (*Custom domains*). Ajouter ensuite ce domaine dans `Cors__AllowedOrigins__1` et `App__FrontendUrl`.

---

## 5. Webhook Stripe de production

Stripe Dashboard → *Developers → Webhooks → Add endpoint* :
- URL : `https://velotech-api.azurewebsites.net/api/payments/webhook`
- Événements : `checkout.session.completed`, `checkout.session.async_payment_succeeded`, `checkout.session.async_payment_failed`, `checkout.session.expired`.
- Copier le *Signing secret* (`whsec_...`) dans `Stripe__WebhookSecret` de l'App Service.

Si l'API est endormie (F1), Stripe réessaie automatiquement pendant 3 jours : aucun paiement n'est perdu.

### Passage en paiements réels
Activer le compte Stripe (infos société et IBAN), créer une clé restreinte live (même permission **Checkout Sessions : Write**), remplacer `rk_test_` par `rk_live_` et créer le webhook équivalent en mode live. Parcourir la [Go Live Checklist](https://docs.stripe.com/get-started/checklist/go-live.md).
