# Canliya Alma Checklist

## 1) Zorunlu ortam degiskenleri

- `ASPNETCORE_ENVIRONMENT=Production`
- `ConnectionStrings__DefaultConnection=<canli SQL connection string>`

Ornek:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Production"
$env:ConnectionStrings__DefaultConnection="Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;Encrypt=True"
```

## 2) Startup schema sync davranisi

- Production icin varsayilan: `Database:RunSchemaSyncAtStartup=false`
- Eger migration/isletim SQL adimlarini uygulama acilisinda calistirmak istersen:
  - `Database__RunSchemaSyncAtStartup=true`

## 3) Build ve publish

```powershell
dotnet build -c Release
dotnet publish -c Release -o .\publish
```

## 4) Hizli saglik kontrolu

- Uygulama ayaga kalktiktan sonra:
  - `GET /health` => `{ "status": "ok", "utc": "..." }`

## 5) Guvenlik notlari

- Session cookie ayarlari guvenli hale getirildi (`HttpOnly`, `SameSite=Lax`, `SecurePolicy=SameAsRequest`).
- Remember-me cookie HTTPS isteginde `Secure=true` olarak yazilir.
