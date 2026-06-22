# Plan de tests back-end Velotech.API

Ce document décrit la stratégie de tests à mettre en place côté back. La création
du projet n'est pas faite automatiquement (elle nécessite la modification du
fichier `.slnx`).

## 1. Création du projet de tests

Dans PowerShell, à la racine de la solution :

```powershell
cd C:\Users\Abdelrahim\source\repos\Velotech.API

# Création du projet xUnit
dotnet new xunit -o Velotech.API.Tests
dotnet add Velotech.API.Tests/Velotech.API.Tests.csproj reference Velotech.API/Velotech.API.csproj

# Packages utiles
dotnet add Velotech.API.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add Velotech.API.Tests package Microsoft.EntityFrameworkCore.InMemory
dotnet add Velotech.API.Tests package FluentAssertions
dotnet add Velotech.API.Tests package Moq

# Ajout à la solution (.slnx)
dotnet sln Velotech.API.slnx add Velotech.API.Tests/Velotech.API.Tests.csproj
```

## 2. Tests prioritaires à écrire

### AuthController
- `Register_ValidDto_ReturnsOkAndCreatesUser`
- `Register_DuplicateEmail_ReturnsBadRequest`
- `Register_PasswordTooShort_ReturnsBadRequest`
- `Login_ValidCredentials_ReturnsJwtToken`
- `Login_InvalidPassword_ReturnsUnauthorized`
- `Login_NonExistentEmail_ReturnsUnauthorized`
- `ForgotPassword_AnonymizesEmailEnumeration`
- `ResetPassword_ValidToken_UpdatesPasswordHash`
- `ResetPassword_ExpiredToken_ReturnsBadRequest`

### OrdersController
- `CreateOrder_InsufficientStock_ReturnsBadRequest`
- `CreateOrder_ValidDto_DecrementsStock`
- `GetMyOrders_NoToken_ReturnsUnauthorized`
- `GetMyOrders_ValidUser_ReturnsOwnOrdersOnly`

### UsersController
- `GetMe_ReturnsCurrentUserProfile`
- `UpdateMe_WithoutCurrentPassword_FailsIfNewPasswordSet`
- `UpdateMe_DuplicateEmail_ReturnsBadRequest`

## 3. Squelette type d'un test

```csharp
public class AuthControllerTests
{
    private VelotechDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<VelotechDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new VelotechDbContext(options);
    }

    [Fact]
    public async Task Register_PasswordTooShort_ReturnsBadRequest()
    {
        using var db = CreateContext();
        var controller = new AuthController(db, /* IConfiguration mock */);

        var result = await controller.Register(new RegisterDto
        {
            Name = "Test",
            Email = "t@t.com",
            Password = "abc" // < 6
        });

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
```

## 4. Cible de couverture

- Controllers : > 70 % branches
- Services / helpers (hash, JWT) : > 90 %
- Modèles / DTOs : pas de tests (peu de logique)

## 5. Intégration CI

Une fois le projet en place, ajouter dans `.github/workflows/ci.yml` :

```yaml
- name: Test
  run: dotnet test --no-build --verbosity normal
```
