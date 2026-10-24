# Galería multi-imagen por viaje — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar hasta 10 imágenes por viaje, carrusel en detalle de viaje/reserva, visor con zoom y administración multi-imagen en el alta/edición de viajes.

**Architecture:** Tabla `TripImages` con orden y portada; `Trip.ImageUrl` se conserva como espejo de portada. La API expone endpoints AdminOnly para subir, borrar, reordenar y elegir portada. La app MAUI usa un `TripImageCarousel` reutilizable y una página de visor con pinch/pan. El admin administra una colección local de imágenes y sincroniza contra la API al guardar.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, EF Core, SQLite/PostgreSQL, .NET MAUI, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-24-multi-image-trip-gallery-design.md`

## Global Constraints

- Máximo **10 imágenes** por viaje.
- Formatos permitidos: `.jpg`, `.jpeg`, `.png`, `.webp`.
- Tamaño máximo de entrada: **20 MB** por archivo.
- `Trip.ImageUrl` debe seguir siendo la URL de la portada.
- Si hay imágenes, debe existir **exactamente una** con `IsCover = true`.
- `Order` debe quedar contiguo: 1..N.
- Los endpoints de mutación de galería son `AdminOnly`.
- La UI debe estar en español.
- No usar UI tests automáticos; la verificación visual la hace el usuario.
- Desplegar primero la API y después la app.
- No ejecutar `git commit` ni `git push` sin confirmación explícita del usuario.
- Mantener `POST /api/trips/{id}/image` por compatibilidad.

## Review Focus

- Un archivo `.heic` o con extensión no soportada: debe mostrar un error claro y no agregarse ni dejar estado parcial.
- Subir exactamente 10 imágenes debe funcionar; la 11 debe rechazarse sin borrar ni corromper las existentes.
- Eliminar la portada o la última imagen: debe reasignar portada o dejar `Trip.ImageUrl = null` con fallback a placeholder.
- Carrusel dentro de un `ScrollView` vertical: el swipe horizontal no debe bloquear el scroll vertical.
- Crear un viaje y fallar la subida de imágenes: no debe duplicarse el viaje al reintentar; debe quedar en modo edición.
- Reordenar con IDs obsoletos o incompletos: la API debe devolver 400 y no dejar `Order` corrupto.

---

### Task 1: Modelo compartido y reglas de galería

**Files:**
- Create: `src/TravelAgency.Shared/Models/TripImage.cs`
- Create: `src/TravelAgency.Shared/Models/TripImagesUploadResult.cs`
- Create: `src/TravelAgency.Shared/Models/TripImageRules.cs`
- Create: `src/TravelAgency.Shared/Models/TripImageFileRules.cs`
- Modify: `src/TravelAgency.Shared/Models/Trip.cs`
- Test: `tests/TravelAgency.App.Tests/TripImageRulesTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `TripImage` con `Id`, `TripId`, `Url`, `Order`, `IsCover`, `CreatedAt`, `Trip?`.
  - `Trip.Images` como `ICollection<TripImage>`.
  - `TripImagesUploadResult { Trip Trip; List<TripImage> Uploaded; }`.
  - `TripImageRules.MaxPerTrip = 10`.
  - `TripImageRules.HasCapacity(int currentCount, int addCount) -> bool`
    (false when `addCount <= 0` or when no slots remain).
  - `TripImageRules.NextOrder(IEnumerable<TripImage> images) -> int`.
  - `TripImageRules.NormalizeOrder(IList<TripImage> images) -> void`.
  - `TripImageRules.ChooseCover(IEnumerable<TripImage> images) -> TripImage?`.
  - `TripImageRules.IsValidOrder(IEnumerable<int> requestedIds, IEnumerable<int> currentIds) -> bool`.
  - `TripImageRules.BuildGalleryUrls(Trip trip) -> IReadOnlyList<string>`.
  - `TripImageFileRules.AllowedExtensions`, `MaxInputBytes = 20L * 1024 * 1024`,
    `IsAllowed(string? fileName, long length) -> bool`.

- [ ] **Step 1: Write the failing tests**

Add `tests/TravelAgency.App.Tests/TripImageRulesTests.cs` with:

```csharp
using TravelAgency.Shared.Models;
using Xunit;

public class TripImageRulesTests
{
    [Fact]
    public void HasCapacity_AllowsTenth_RejectsEleventh()
    {
        Assert.True(TripImageRules.HasCapacity(9, 1));
        Assert.False(TripImageRules.HasCapacity(10, 1));
        Assert.False(TripImageRules.HasCapacity(10, 0));
    }

    [Fact]
    public void NextOrder_EmptyIsOne_AndUsesMaxPlusOne()
    {
        Assert.Equal(1, TripImageRules.NextOrder(Array.Empty<TripImage>()));
        Assert.Equal(4, TripImageRules.NextOrder(new[]
        {
            new TripImage { Id = 1, Order = 3 },
            new TripImage { Id = 2, Order = 2 },
        }));
    }

    [Fact]
    public void NormalizeOrder_SortsAndReassignsContiguousOrders()
    {
        var images = new List<TripImage>
        {
            new() { Id = 1, Order = 9 },
            new() { Id = 2, Order = 3 },
            new() { Id = 3, Order = 7 },
        };

        TripImageRules.NormalizeOrder(images);

        Assert.Equal(new[] { 2, 3, 1 }, images.Select(i => i.Id));
        Assert.Equal(new[] { 1, 2, 3 }, images.Select(i => i.Order));
    }

    [Fact]
    public void ChooseCover_PrefersExistingCover_ThenLowestOrder()
    {
        var cover = new TripImage { Id = 2, Order = 5, IsCover = true };
        var first = new TripImage { Id = 1, Order = 1 };
        Assert.Same(cover, TripImageRules.ChooseCover(new[] { first, cover }));

        cover.IsCover = false;
        Assert.Same(first, TripImageRules.ChooseCover(new[] { cover, first }));
    }

    [Fact]
    public void IsValidOrder_RequiresExactSetWithoutDuplicates()
    {
        Assert.True(TripImageRules.IsValidOrder(new[] { 3, 1, 2 }, new[] { 1, 2, 3 }));
        Assert.False(TripImageRules.IsValidOrder(new[] { 1, 2 }, new[] { 1, 2, 3 }));
        Assert.False(TripImageRules.IsValidOrder(new[] { 1, 1, 2 }, new[] { 1, 2, 3 }));
        Assert.False(TripImageRules.IsValidOrder(new[] { 1, 2, 4 }, new[] { 1, 2, 3 }));
    }

    [Fact]
    public void BuildGalleryUrls_UsesOrderedImages_ThenFallsBackToImageUrl()
    {
        var trip = new Trip
        {
            ImageUrl = "cover.jpg",
            Images = new List<TripImage>
            {
                new() { Url = "second.jpg", Order = 2 },
                new() { Url = "first.jpg", Order = 1 },
            }
        };
        Assert.Equal(new[] { "first.jpg", "second.jpg" }, TripImageRules.BuildGalleryUrls(trip));

        trip.Images.Clear();
        Assert.Equal(new[] { "cover.jpg" }, TripImageRules.BuildGalleryUrls(trip));

        trip.ImageUrl = null;
        Assert.Empty(TripImageRules.BuildGalleryUrls(trip));
    }

    [Theory]
    [InlineData("foto.jpg", 100, true)]
    [InlineData("foto.JPEG", 100, true)]
    [InlineData("foto.png", 100, true)]
    [InlineData("foto.webp", 100, true)]
    [InlineData("foto.heic", 100, false)]
    [InlineData("foto.gif", 100, false)]
    [InlineData("foto.jpg", 0, false)]
    [InlineData("foto.jpg", 20L * 1024 * 1024 + 1, false)]
    public void IsAllowed_ValidatesExtensionAndSize(string name, long length, bool expected)
    {
        Assert.Equal(expected, TripImageFileRules.IsAllowed(name, length));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/TravelAgency.App.Tests/TravelAgency.App.Tests.csproj --filter TripImageRulesTests -v minimal`
Expected: FAIL because `TripImageRules`/`TripImageFileRules` do not exist.

- [ ] **Step 3: Implement the shared model and rules**

Create `TripImage.cs`:

```csharp
using System.Text.Json.Serialization;

namespace TravelAgency.Shared.Models;

public class TripImage
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public string Url { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsCover { get; set; }
    public DateTime CreatedAt { get; set; }

    [JsonIgnore]
    public Trip? Trip { get; set; }
}
```

Add to `Trip.cs`:

```csharp
public ICollection<TripImage> Images { get; set; } = new List<TripImage>();
```

Create `TripImagesUploadResult.cs`:

```csharp
namespace TravelAgency.Shared.Models;

public class TripImagesUploadResult
{
    public Trip Trip { get; set; } = new();
    public List<TripImage> Uploaded { get; set; } = new();
}
```

Implement `TripImageRules` with ordering by `Order` then `Id`; `NormalizeOrder` assigns
1..N; `HasCapacity` returns false when `addCount <= 0` or `currentCount + addCount` exceeds
`MaxPerTrip`; `BuildGalleryUrls` uses ordered `Images` and falls back to `Trip.ImageUrl`
when empty. Implement `TripImageFileRules` with a case-insensitive extension set and 20 MB
limit.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/TravelAgency.App.Tests/TravelAgency.App.Tests.csproj --filter TripImageRulesTests -v minimal`
Expected: PASS.

- [ ] **Step 5: Build Shared**

Run: `dotnet build src/TravelAgency.Shared/TravelAgency.Shared.csproj`
Expected: 0 errors.

- [ ] **Step 6: Commit (solo con confirmación del usuario)**

```bash
git add src/TravelAgency.Shared/Models/TripImage.cs \
  src/TravelAgency.Shared/Models/TripImagesUploadResult.cs \
  src/TravelAgency.Shared/Models/TripImageRules.cs \
  src/TravelAgency.Shared/Models/TripImageFileRules.cs \
  src/TravelAgency.Shared/Models/Trip.cs \
  tests/TravelAgency.App.Tests/TripImageRulesTests.cs
git commit -m "feat: add trip image model and gallery rules"
```

---

### Task 2: Esquema de base de datos y migración

**Files:**
- Modify: `src/TravelAgency.Api/Data/AppDbContext.cs`
- Modify: `src/TravelAgency.Api/Program.cs` (llamada de arranque y `EnsureTripImagesTableAsync`)

**Interfaces:**
- Consumes: `TripImage`, `Trip.Images`.
- Produces: tabla `TripImages`; `EnsureTripImagesTableAsync(AppDbContext db, string provider)`.

- [ ] **Step 1: Add the EF mapping**

In `AppDbContext` add:

```csharp
public DbSet<TripImage> TripImages => Set<TripImage>();
```

In `OnModelCreating` add a `TripImage` block with:

```csharp
e.ToTable("TripImages");
e.Property(x => x.Url).IsRequired().HasMaxLength(500);
e.Property(x => x.Order).IsRequired();
e.Property(x => x.IsCover).IsRequired();
e.Property(x => x.CreatedAt).IsRequired();
e.HasIndex(x => x.TripId);
e.HasOne(x => x.Trip)
 .WithMany(t => t.Images)
 .HasForeignKey(x => x.TripId)
 .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 2: Add `EnsureTripImagesTableAsync`**

In `Program.cs`, add the call after `EnsureTripTransportsTableAsync(db, provider);` and before
`EnsureTripExpensesTableAsync(db, provider);`.

Implement the function with provider-specific SQL:

SQLite:

```sql
CREATE TABLE IF NOT EXISTS "TripImages" (
  "Id" INTEGER NOT NULL CONSTRAINT "PK_TripImages" PRIMARY KEY AUTOINCREMENT,
  "TripId" INTEGER NOT NULL,
  "Url" TEXT NOT NULL,
  "Order" INTEGER NOT NULL,
  "IsCover" INTEGER NOT NULL DEFAULT 0,
  "CreatedAt" TEXT NOT NULL,
  CONSTRAINT "FK_TripImages_Trips_TripId"
    FOREIGN KEY ("TripId") REFERENCES "Trips" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_TripImages_TripId" ON "TripImages" ("TripId");
```

Postgres: same columns with `integer GENERATED BY DEFAULT AS IDENTITY`,
`character varying(500)`, `boolean DEFAULT false`, `timestamp without time zone`.

Backfill, SQLite:

```sql
INSERT INTO "TripImages" ("TripId", "Url", "Order", "IsCover", "CreatedAt")
SELECT t."Id", t."ImageUrl", 1, 1, datetime('now')
FROM "Trips" t
WHERE t."ImageUrl" IS NOT NULL
  AND t."ImageUrl" NOT LIKE '%/placeholder.jpg'
  AND NOT EXISTS (
    SELECT 1 FROM "TripImages" i WHERE i."TripId" = t."Id"
  );
```

Postgres: use `true` and `now()`.

- [ ] **Step 3: Build the API**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj`
Expected: 0 errors.

- [ ] **Step 4: Verify migration and backfill**

Run the API once with SQLite:

```bash
dotnet run --project src/TravelAgency.Api/TravelAgency.Api.csproj --urls http://localhost:5199
```

In another terminal:

```bash
sqlite3 src/TravelAgency.Api/travelagency.db ".schema TripImages"
sqlite3 src/TravelAgency.Api/travelagency.db \
  "SELECT COUNT(*) FROM TripImages WHERE IsCover = 1;"
```

Expected: table exists; at least one cover row for trips with real `ImageUrl`.
Stop the API with `Ctrl+C`.

- [ ] **Step 5: Commit (solo con confirmación del usuario)**

```bash
git add src/TravelAgency.Api/Data/AppDbContext.cs src/TravelAgency.Api/Program.cs
git commit -m "feat: add trip images schema and backfill"
```

---

### Task 3: API de subida y eliminación

**Files:**
- Modify: `src/TravelAgency.Api/Program.cs`

**Interfaces:**
- Consumes: `TripImageRules`, `TripImageFileRules`, `TripImagesUploadResult`, `ImageStorageService`.
- Produces:
  - `POST /api/trips/{id}/images` -> `TripImagesUploadResult`.
  - `DELETE /api/trips/{id}/images/{imageId}` -> `Trip`.
  - `static void NormalizeTripImages(Trip trip)`.

- [ ] **Step 1: Add the upload endpoint**

Place it after the existing `POST /api/trips/{id}/image` endpoint.

Behavior:

- `RequireAuthorization("AdminOnly")`.
- Load `Trip` with `Include(t => t.Images)`.
- `var files = request.Form.Files;`
- Reject empty files, `!TripImageRules.HasCapacity(trip.Images.Count, files.Count)`, invalid
  extension/size.
- For each file:
  - Copy to `MemoryStream`.
  - `TripImageProcessor.Prepare(input, extension)`.
  - `storage.UploadAsync(...)`.
  - Add `TripImage` to a local `created` list and to `trip.Images` with
    `Order = TripImageRules.NextOrder(trip.Images)`.
- On any failure: delete all URLs uploaded in this request from `storage`, return `400` with
  the corresponding Spanish message.
- Call `NormalizeTripImages(trip)`.
- `await db.SaveChangesAsync()`.
- Return `Results.Ok(new TripImagesUploadResult { Trip = trip, Uploaded = created })`.
- If `SaveChangesAsync` fails, delete uploaded files and return 500.

- [ ] **Step 2: Add the delete endpoint**

`DELETE /api/trips/{id}/images/{imageId}` (`AdminOnly`):

- Load trip with images.
- Find image; if missing: `404`.
- Remove from `trip.Images`.
- Call `NormalizeTripImages(trip)`.
- Save.
- After save, `await storage.DeleteAsync(image.Url)`.
- Return `Results.Ok(trip)`.

- [ ] **Step 3: Add `NormalizeTripImages`**

```csharp
static void NormalizeTripImages(Trip trip)
{
    var ordered = trip.Images.OrderBy(i => i.Order).ThenBy(i => i.Id).ToList();
    for (var i = 0; i < ordered.Count; i++) ordered[i].Order = i + 1;

    if (ordered.Count == 0)
    {
        trip.ImageUrl = null;
        return;
    }

    var cover = ordered.FirstOrDefault(i => i.IsCover) ?? ordered[0];
    foreach (var image in ordered) image.IsCover = ReferenceEquals(image, cover);
    trip.ImageUrl = cover.Url;
}
```

- [ ] **Step 4: Verify with API calls**

Start API (see Task 2). Set env vars from the seed credentials in
`EnsureSeedUsersAsync`:

```bash
export ADMIN_EMAIL="<email del seed>"
export ADMIN_PASSWORD="<password del seed>"
TOKEN=$(curl -s -X POST http://localhost:5199/api/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | jq -r .token)
```

Create a trip and upload two images:

```bash
TRIP=$(curl -s -X POST http://localhost:5199/api/trips \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"title":"Test galeria","destination":"Puebla","startDate":"2027-01-10T08:00:00","endDate":"2027-01-11T18:00:00","price":100,"capacity":10,"transportType":"Camion","isActive":true,"cancellationDaysLimit":3}')
TRIP_ID=$(echo "$TRIP" | jq -r .id)

curl -s -X POST "http://localhost:5199/api/trips/$TRIP_ID/images" \
  -H "Authorization: Bearer $TOKEN" \
  -F "files=@src/TravelAgency.App/Resources/Images/placeholder.jpg" \
  -F "files=@src/TravelAgency.App/Resources/Images/logo.jpg" \
  | jq '{count:(.uploaded|length), cover:.trip.imageUrl, orders:[.trip.images[].order]}'
```

Expected: `count=2`, one cover, orders `[1,2]`.

Delete the cover and verify the new cover:

```bash
COVER_ID=$(curl -s "http://localhost:5199/api/trips/$TRIP_ID" \
  -H "Authorization: Bearer $TOKEN" | jq -r '.images[] | select(.isCover) | .id')

curl -s -X DELETE "http://localhost:5199/api/trips/$TRIP_ID/images/$COVER_ID" \
  -H "Authorization: Bearer $TOKEN" \
  | jq '{count:(.images|length), cover:.imageUrl, covers:[.images[]|select(.isCover)|.id]}'
```

Expected: `count=1`, `cover` equals the remaining image, one cover.

- [ ] **Step 5: Build and commit**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj`
Expected: 0 errors.

```bash
git add src/TravelAgency.Api/Program.cs
git commit -m "feat: add trip image upload and delete endpoints"
```

---

### Task 4: Reordenar, portada, respuestas e imagen legacy

**Files:**
- Modify: `src/TravelAgency.Api/Program.cs`

**Interfaces:**
- Consumes: `TripImageRules.IsValidOrder`, `NormalizeTripImages`.
- Produces:
  - `PUT /api/trips/{id}/images/order` body `{ "imageIds": [...] }`.
  - `PUT /api/trips/{id}/images/{imageId}/cover`.
  - `Images` en `GET /api/trips/{id}`, `GET /api/trips/manage`, `GET /api/bookings/{id}`.
  - `POST /api/trips/{id}/image` actualizado.

- [ ] **Step 1: Add the reorder endpoint**

`PUT /api/trips/{id}/images/order` (`AdminOnly`):

- Body record: `record TripImageOrderRequest(List<int>? ImageIds);` at the bottom of `Program.cs`.
- Load trip with images.
- `var requested = request.ImageIds ?? new();`
- If `!TripImageRules.IsValidOrder(requested, trip.Images.Select(i => i.Id))`:
  `400` “El orden debe incluir exactamente las imágenes del viaje.”
- Create a `Dictionary<int, int>` order; assign each image `.Order = index + 1`.
- `NormalizeTripImages(trip)`; save; return `trip`.

- [ ] **Step 2: Add the cover endpoint**

`PUT /api/trips/{id}/images/{imageId}/cover` (`AdminOnly`):

- Load trip with images.
- Find image; `404` if missing.
- Set every `IsCover = false`; target `IsCover = true`.
- `NormalizeTripImages(trip)`; save; return `trip`.

- [ ] **Step 3: Include images in responses**

- `GET /api/trips/{id}`: add `.Include(t => t.Images)`.
- `GET /api/trips/manage`: add `.Include(t => t.Images)`.
- `GET /api/bookings/{id}`: change `.Include(b => b.Trip)` to
  `.Include(b => b.Trip).ThenInclude(t => t.Images)`.

- [ ] **Step 4: Update the legacy image endpoint**

In `POST /api/trips/{id}/image`:

- Load trip with images.
- Upload the new image as today.
- If a cover image exists, update its `Url` to the new URL.
- Else if images exist, use the lowest `Order`, set `IsCover = true`, update its `Url`.
- Else create a `TripImage` with `Order = 1`, `IsCover = true`.
- Delete the previous image file after save if it changed.
- Call `NormalizeTripImages(trip)` and save.
- Return `trip`.

- [ ] **Step 5: Verify with API calls**

Start API and reuse `$TOKEN`/`$TRIP_ID` from Task 3.

```bash
curl -s -X PUT "http://localhost:5199/api/trips/$TRIP_ID/images/order" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"imageIds\":$(curl -s "http://localhost:5199/api/trips/$TRIP_ID" -H "Authorization: Bearer $TOKEN" | jq -c '[.images[].id] | reverse')}" \
  | jq '[.images[].order]'
```

Expected: `[1,2]` after reversing the order.

```bash
NEW_COVER=$(curl -s "http://localhost:5199/api/trips/$TRIP_ID" -H "Authorization: Bearer $TOKEN" | jq -r '.images[-1].id')
curl -s -X PUT "http://localhost:5199/api/trips/$TRIP_ID/images/$NEW_COVER/cover" \
  -H "Authorization: Bearer $TOKEN" | jq '{cover:.imageUrl, covers:[.images[]|select(.isCover)|.id]}'
```

Expected: one cover matching `NEW_COVER`.

Invalid order check:

```bash
FIRST_ID=$(curl -s "http://localhost:5199/api/trips/$TRIP_ID" -H "Authorization: Bearer $TOKEN" | jq -r '.images[0].id')
HTTP=$(curl -s -o /tmp/reorder-error.json -w '%{http_code}' \
  -X PUT "http://localhost:5199/api/trips/$TRIP_ID/images/order" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"imageIds\":[$FIRST_ID,999999]}")
test "$HTTP" = "400" && echo "stale order rejected"
```

Expected: `stale order rejected`; no order corruption.

Legacy check:

```bash
BEFORE=$(curl -s "http://localhost:5199/api/trips/$TRIP_ID" -H "Authorization: Bearer $TOKEN" | jq '.images|length')
curl -s -X POST "http://localhost:5199/api/trips/$TRIP_ID/image" \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@src/TravelAgency.App/Resources/Images/placeholder.jpg" | jq '.imageUrl'
AFTER=$(curl -s "http://localhost:5199/api/trips/$TRIP_ID" -H "Authorization: Bearer $TOKEN" | jq '.images|length')
echo "before=$BEFORE after=$AFTER"
```

Expected: `before == after`; cover URL changed.

- [ ] **Step 6: Build and commit**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj`
Expected: 0 errors.

```bash
git add src/TravelAgency.Api/Program.cs
git commit -m "feat: add trip image reorder, cover, and responses"
```

---

### Task 5: Cliente API de la app

**Files:**
- Modify: `src/TravelAgency.App/Services/ApiService.cs`

**Interfaces:**
- Consumes: `TripImagesUploadResult`, `TripImage`.
- Produces:
  - `Task<TripImagesUploadResult?> UploadTripImagesAsync(int tripId, IReadOnlyList<FileResult> files)`
  - `Task<Trip?> DeleteTripImageAsync(int tripId, int imageId)`
  - `Task<Trip?> ReorderTripImagesAsync(int tripId, IReadOnlyList<int> imageIds)`
  - `Task<Trip?> SetTripImageCoverAsync(int tripId, int imageId)`
  - `public const int FullImageWidth = 1600;`

- [ ] **Step 1: Add `FullImageWidth`**

Next to `LargeImageWidth`, add `public const int FullImageWidth = 1600;`.

- [ ] **Step 2: Implement upload**

Use `MultipartFormDataContent`:

```csharp
using var content = new MultipartFormDataContent();
foreach (var file in files)
{
    var stream = await file.OpenReadAsync();
    var part = new StreamContent(stream);
    part.Headers.ContentType = new MediaTypeHeaderValue(
        file.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" :
        file.FileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp" :
        "image/jpeg");
    content.Add(part, "files", file.FileName);
}
var response = await _http.PostAsync($"/api/trips/{tripId}/images", content);
if (!response.IsSuccessStatusCode) throw new HttpRequestException(await response.Content.ReadAsStringAsync());
return await response.Content.ReadFromJsonAsync<TripImagesUploadResult>(JsonOptions);
```

Add `using System.Net.Http.Headers;` if needed.

- [ ] **Step 3: Implement delete, reorder, cover**

- Delete: `DELETE /api/trips/{tripId}/images/{imageId}`.
- Reorder: `PUT /api/trips/{tripId}/images/order` with `new { imageIds }`.
- Cover: `PUT /api/trips/{tripId}/images/{imageId}/cover` with no body.

Each returns `Trip?`; throw on non-success.

- [ ] **Step 4: Build the app**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errors.

- [ ] **Step 5: Commit (solo con confirmación del usuario)**

```bash
git add src/TravelAgency.App/Services/ApiService.cs
git commit -m "feat: add trip gallery API client methods"
```

---

### Task 6: Carrusel y visor del cliente

**Files:**
- Create: `src/TravelAgency.App/ViewModels/TripImageItem.cs`
- Create: `src/TravelAgency.App/Controls/TripImageCarousel.xaml`
- Create: `src/TravelAgency.App/Controls/TripImageCarousel.xaml.cs`
- Create: `src/TravelAgency.App/Modules/Client/Views/ClientImageViewerPage.xaml`
- Create: `src/TravelAgency.App/Modules/Client/Views/ClientImageViewerPage.xaml.cs`
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientTripDetailPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientTripDetailPage.xaml.cs`
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml.cs`
- Modify: `src/TravelAgency.App/AppShell.xaml.cs`
- Modify: `src/TravelAgency.App/MauiProgram.cs`

**Interfaces:**
- Consumes: `TripImageRules.BuildGalleryUrls`, `ApiService.GetTripImage`, `ApiService.FullImageWidth`.
- Produces:
  - `TripImageItem(string? url, ImageSource source, bool isCover)`.
  - `TripImageCarousel.ItemsSource` (`IEnumerable<TripImageItem>`), `ImageTapped` event.
  - `ClientImageViewerPage(ApiService api)` implementing `IQueryAttributable`.
  - Ruta `imageviewer`.

- [ ] **Step 1: Add `TripImageItem`**

```csharp
public sealed class TripImageItem
{
    public string? Url { get; }
    public ImageSource Source { get; }
    public bool IsCover { get; }
    public TripImageItem(string? url, ImageSource source, bool isCover)
    {
        Url = url; Source = source; IsCover = isCover;
    }
}
```

- [ ] **Step 2: Build `TripImageCarousel`**

XAML: `ContentView` with a `Grid` containing:

- `CarouselView x:Name="Carousel" Loop="False"`.
- `IndicatorView x:Name="Indicators"`.
- Counter `Label x:Name="CounterLabel" HorizontalOptions="End" VerticalOptions="Start"`.
- `DataTemplate` with `NetworkImage Source="{Binding Source}" Aspect="AspectFill"` and a
  `TapGestureRecognizer Tapped="OnImageTapped" CommandParameter="{Binding .}"`.

Code-behind:

- Bindable `ItemsSource` of type `IEnumerable<TripImageItem>`.
- On change: `Carousel.ItemsSource = value;` update indicator/counter visibility.
- `ImageTapped` event with `TripImageTappedEventArgs { string? Url; int Index; }`.
- `OnImageTapped` raises the event.
- `Carousel.PositionChanged` updates `CounterLabel.Text = $"{position + 1} / {count}"`.
- If count <= 1: `Carousel.IsSwipeEnabled = false`, `Indicators.IsVisible = false`,
  `CounterLabel.IsVisible = false`.
- If count == 0: set a single placeholder item.

- [ ] **Step 3: Build `ClientImageViewerPage`**

XAML:

- `ContentPage` black background, `Shell.NavBarIsVisible="False"`.
- `Grid` with `NetworkImage x:Name="ZoomImage" Aspect="AspectFit"`.
- Close button top-left with `Text="✕"`.
- Title label top-center.
- Hint label bottom: “Pellizca para hacer zoom · doble toque para acercar”.
- `PinchGestureRecognizer PinchUpdated="OnPinchUpdated"`.
- `PanGestureRecognizer PanUpdated="OnPanUpdated"`.
- `TapGestureRecognizer NumberOfTapsRequired="2" Tapped="OnDoubleTapped"`.

Code-behind:

- `IQueryAttributable` reads `url` and `title`.
- `OnAppearing`: set `ZoomImage.Source = _api.GetTripImage(url, ApiService.FullImageWidth);`
- Pinch: store `_startScale` on `PinchStarted`, set
  `ZoomImage.Scale = Math.Clamp(_startScale * e.Scale, 1, 4)`.
- Pan: only when `Scale > 1`; update `TranslationX/Y`; clamp to
  `(Scale - 1) * Width / 2` and `(Scale - 1) * Height / 2`.
- Double tap: if `Scale > 1`, reset to 1 and translations 0; else set scale 2.5.
- `OnDisappearing`: reset.
- Close: `await Shell.Current.GoToAsync("..");`.

- [ ] **Step 4: Integrate in trip detail**

Replace the hero `NetworkImage` with:

```xml
<controls:TripImageCarousel x:Name="TripImagesCarousel"
                            HeightRequest="230"
                            ImageTapped="OnTripImageTapped" />
```

In code-behind:

```csharp
TripImagesCarousel.ItemsSource = BuildTripImageItems(_trip);
```

where `BuildTripImageItems`:

- `var urls = TripImageRules.BuildGalleryUrls(trip);`
- Map each URL to `new TripImageItem(url, _api.GetTripImage(url, ApiService.LargeImageWidth), isCover)`.
- If empty, return one item with `Source = ApiService.PlaceholderSource()`.

`OnTripImageTapped`:

```csharp
if (string.IsNullOrWhiteSpace(e.Url)) return;
await Shell.Current.GoToAsync(
    $"imageviewer?url={Uri.EscapeDataString(e.Url)}&title={Uri.EscapeDataString(_trip?.Title ?? "Viaje")}");
```

- [ ] **Step 5: Integrate in booking detail**

Same as Step 4, using `trip` from the booking, height 200, and title `trip.Title`.

- [ ] **Step 6: Register route and DI**

- `AppShell.xaml.cs`: `Routing.RegisterRoute("imageviewer", typeof(ClientImageViewerPage));`
- `MauiProgram.cs`: `builder.Services.AddTransient<ClientImageViewerPage>();`

- [ ] **Step 7: Build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errors.

- [ ] **Step 8: Manual verification**

Open a trip with 0, 1 and N images. Verify:

- Placeholder with 0 images.
- No dots/swipe with 1 image.
- Dots/counter/swipe with N images.
- Tap opens viewer.
- Pinch zooms 1x–4x, pan works only zoomed, double-tap toggles, close returns.

- [ ] **Step 9: Commit (solo con confirmación del usuario)**

```bash
git add src/TravelAgency.App/ViewModels/TripImageItem.cs \
  src/TravelAgency.App/Controls/TripImageCarousel.xaml \
  src/TravelAgency.App/Controls/TripImageCarousel.xaml.cs \
  src/TravelAgency.App/Modules/Client/Views/ClientImageViewerPage.xaml \
  src/TravelAgency.App/Modules/Client/Views/ClientImageViewerPage.xaml.cs \
  src/TravelAgency.App/Modules/Client/Views/ClientTripDetailPage.xaml \
  src/TravelAgency.App/Modules/Client/Views/ClientTripDetailPage.xaml.cs \
  src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml \
  src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml.cs \
  src/TravelAgency.App/AppShell.xaml.cs src/TravelAgency.App/MauiProgram.cs
git commit -m "feat: add client trip image carousel and viewer"
```

---

### Task 7: Administración multi-imagen

**Files:**
- Create: `src/TravelAgency.App/Modules/Admin/ViewModels/AdminTripImageItem.cs`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminDashboardPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminDashboardPage.xaml.cs`
- Modify: `src/TravelAgency.App/Resources/Styles/Styles.xaml` (solo si faltan estilos)

**Interfaces:**
- Consumes: `ApiService` methods from Task 5, `TripImageRules`, `TripImageFileRules`.
- Produces: `AdminTripImageItem` y la UI de galería.

- [ ] **Step 1: Add `AdminTripImageItem`**

```csharp
public sealed class AdminTripImageItem : INotifyPropertyChanged
{
    public int? Id { get; init; }
    public string? Url { get; init; }
    public FileResult? File { get; init; }
    public byte[]? PreviewBytes { get; init; }
    public ImageSource Source { get; init; } = null!;
    public bool IsNew => File is not null;
    public bool IsDeleted { get; set; }
    public int Order { get; set; }
    public bool IsCover { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyCoverChanged() => PropertyChanged?.Invoke(...);
}
```

- [ ] **Step 2: Replace the image card in XAML**

Replace the single image card with:

- Header “Imágenes del viaje” and `ImageCounterLabel`.
- Button `📷 Agregar imágenes` (`OnPickImagesClicked`).
- `CollectionView x:Name="TripImagesList"` horizontal, `ItemsSource="{Binding ...}"` set in
  code-behind, `EmptyView` “Sin imágenes. Se usará el placeholder.”
- Item template:
  - `NetworkImage Source="{Binding Source}"` / 120×90.
  - `Label Text="Portada" IsVisible="{Binding IsCover}"`.
  - Buttons `←`, `→`, `Portada`, `Eliminar` with `CommandParameter="{Binding .}"`.
- Helper label: “La portada se muestra en las tarjetas. Usa las flechas para ordenar.”

- [ ] **Step 3: Load existing images in edit mode**

`LoadForEditAsync` calls `LoadTripImages(trip)` after `StartEdit(trip)`.

`LoadTripImages`:

- Clear `_tripImages` and `_deletedImageIds`.
- Add `trip.Images.OrderBy(i => i.Order)` as `AdminTripImageItem` with
  `Source = _api.GetTripImage(url, ApiService.LargeImageWidth)`.
- If no `Images` and `trip.ImageUrl` is non-empty and not placeholder, add a visual-only
  fallback item with `Id = null`; it is not editable (the API backfill should make this rare).
- Set `IsCover` from the server.
- Update counter and empty state.

- [ ] **Step 4: Implement multi-select**

`OnPickImagesClicked`:

- `remaining = TripImageRules.MaxPerTrip - _tripImages.Count`.
- If `remaining <= 0`, alert and return.
- `PickPhotosAsync` with `SelectionLimit = remaining`, `MaximumWidth = 1920`,
  `MaximumHeight = 1920`, `CompressionQuality = 80`, `RotateImage = true`.
- For each result:
  - Validate `TripImageFileRules.IsAllowed(result.FileName, length)`.
  - Read bytes; create preview `ImageSource.FromStream(() => new MemoryStream(bytes))`.
  - Add `AdminTripImageItem` at the end with `IsNew = true`.
- Update counter.
- If any file is invalid, show alert and skip it.

- [ ] **Step 5: Implement remove, move, cover**

- Remove: remove item; if `Id` has value, add to `_deletedImageIds`; if it was cover and
  items remain, mark first as cover; update counter.
- Move left/right: swap in `_tripImages`; reassign `Order`; refresh list.
- Cover: set all `IsCover = false`, selected `true`, notify.

- [ ] **Step 6: Implement `SyncTripImagesAsync`**

```csharp
private async Task<Trip?> SyncTripImagesAsync(int tripId, Trip savedTrip)
```

Steps:

1. `var newItems = _tripImages.Where(i => i.IsNew && !i.IsDeleted).ToList();`
2. `var upload = await _api.UploadTripImagesAsync(tripId, newItems.Select(i => i.File!).ToList());`
3. Assign IDs from `upload.Uploaded` to `newItems` in order.
4. Build `orderedIds = _tripImages.Where(i => !i.IsDeleted && i.Id.HasValue).OrderBy(Order).Select(i => i.Id!.Value).ToList();`
5. If `orderedIds.Count > 1`, `await _api.ReorderTripImagesAsync(tripId, orderedIds);`
6. If a cover item has an ID and is not the server cover, `await _api.SetTripImageCoverAsync(tripId, coverId);`
7. For each `_deletedImageIds`, `await _api.DeleteTripImageAsync(tripId, id);`
8. Return `await _api.GetTripAsync(tripId);`

- [ ] **Step 7: Update save flow**

In `OnSaveClicked` after trip create/update:

- If creation succeeded, set `_editingTrip = created` and switch labels to edit mode before
  image sync.
- `var synced = await SyncTripImagesAsync(trip.Id, savedTrip);`
- If `synced is null`, show error and stay in edit mode.
- Only after success: alert, reset form, navigate to `//admintrips`.

- [ ] **Step 8: Build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errors.

- [ ] **Step 9: Manual verification**

- Create a trip with 0, 1 and 10 images.
- Try to add an 11th: blocked.
- Try `.heic`: rejected with message.
- Reorder, set cover, delete cover, delete last image.
- Edit existing trip and confirm order/cover persist.
- Force an upload failure (e.g. stop API) after creating a trip; confirm the form stays in
  edit mode and a retry updates instead of creating a duplicate.

- [ ] **Step 10: Commit (solo con confirmación del usuario)**

```bash
git add src/TravelAgency.App/Modules/Admin/ViewModels/AdminTripImageItem.cs \
  src/TravelAgency.App/Modules/Admin/Views/AdminDashboardPage.xaml \
  src/TravelAgency.App/Modules/Admin/Views/AdminDashboardPage.xaml.cs \
  src/TravelAgency.App/Resources/Styles/Styles.xaml
git commit -m "feat: add admin multi-image trip management"
```

---

### Task 8: Verificación final

**Files:** ninguno nuevo.

- [ ] **Step 1: Run unit tests**

Run: `dotnet test`
Expected: all tests pass.

- [ ] **Step 2: Build API**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj`
Expected: 0 errors.

- [ ] **Step 3: Build app**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
Expected: 0 errors.

- [ ] **Step 4: Verify compatibility**

- `GET /api/trips` still returns `imageUrl`.
- Old `POST /api/trips/{id}/image` still works and does not add a gallery row.
- `GET /api/trips/{id}` includes `images`.
- `GET /api/bookings/{id}` includes `trip.images`.
- Coordinator gets 403 on gallery mutations.

- [ ] **Step 5: Final report**

Report:
- Files changed.
- Tests/build results.
- Manual checks pending by the user.
- Any known limitation (HEIC rejected).
- Do not commit/push unless the user confirms.
