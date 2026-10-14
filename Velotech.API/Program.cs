using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using Velotech.API.Data;
using Velotech.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<VelotechDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        // Azure SQL (offre gratuite serverless) se met en pause apres 1h
        // d'inactivite : la premiere connexion peut echouer le temps du reveil.
        sql => sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)));

// Stripe : cles lues depuis la config (user-secrets en local, App Settings en prod).
// Une seule instance StripeClient (recommandation Stripe : pas de cle globale
// StripeConfiguration.ApiKey). Non enregistree si la cle est absente : l'API
// demarre quand meme et l'endpoint de paiement repond 503.
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
var stripeKey = builder.Configuration["Stripe:SecretKey"];
if (!string.IsNullOrWhiteSpace(stripeKey))
    builder.Services.AddSingleton(new Stripe.StripeClient(stripeKey));
builder.Services.AddScoped<StripeCheckoutService>();
// Libere les locations / commandes non payees apres 30 min (toutes les 5 min)
builder.Services.AddHostedService<PendingPaymentCleanupService>();

builder.Services.AddControllers();

// Swagger + cadenas (Authorize)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Velotech.API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {your JWT token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// JWT
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection("Jwt");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!))
        };
    });

builder.Services.AddAuthorization();

// CORS :
//  - Dev : on autorise TOUTES les origines pour eviter les blocages lors
//    des demos (differents PC, differents ports du front, Swagger, Postman...).
//    Pas d'AllowCredentials car incompatible avec AllowAnyOrigin.
//  - Prod : on garde une liste blanche stricte (uniquement l'URL officielle
//    du front deploye) pour la securite.
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevOpen", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()
    );

    // Origines autorisees : Cors:AllowedOrigins (ex. App Setting
    // Cors__AllowedOrigins__0 = https://xxx.azurestaticapps.net)
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? new[] { "https://velotech.be", "https://www.velotech.be" };

    options.AddPolicy("ProdStrict", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
    );
});

var app = builder.Build();



// Seed (1 seule fois)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VelotechDbContext>();
    await DbSeeder.SeedAsync(db);
}

// Swagger : toujours en dev ; en prod seulement si l'App Setting
// Swagger__Enabled = true (interrupteur sans redeploiement, ex. demo / defense).
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// En dev on NE force PAS la redirection HTTPS : ca evite le probleme ou
// Swagger (http://localhost:5094) est redirige vers https://localhost:7248
// et provoque une erreur CORS entre les deux ports.
// En prod on garde la redirection pour imposer HTTPS.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors(app.Environment.IsDevelopment() ? "DevOpen" : "ProdStrict");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Sonde de sante (monitoring / "reveil" de l'app sur un hebergement gratuit)
app.MapGet("/health", () => Results.Ok("ok"));
app.Run();