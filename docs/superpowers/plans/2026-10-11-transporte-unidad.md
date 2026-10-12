# Información de la unidad (transporte) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir al admin publicar por viaje una foto + placas de la unidad de transporte (temporal), mostrarla al cliente en el detalle de su reserva y notificarlo, y borrarla al finalizar el viaje.

**Architecture:** Tabla nueva `TripTransports` (1:1 con `Trip`, FK cascade). Endpoints `POST`/`GET /api/trips/{id}/transport`; la imagen va al storage (bucket `trips`) reusando `ImageStorageService`. El borrado ocurre al finalizar/eliminar el viaje. Notificación a los clientes con reservas no canceladas solo en la transición ausente → presente.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API + EF Core (Supabase/Postgres; SQLite fallback), .NET MAUI (net10.0-maccatalyst). Sin proyecto de tests.

**Spec:** `docs/superpowers/specs/2026-10-11-transporte-unidad-design.md`

## Global Constraints

- .NET 10. App: `net10.0-maccatalyst`. API: `src/TravelAgency.Api`.
- **No existe proyecto de tests.** Verificar con `dotnet build` + comprobaciones manuales/HTTP. No crear proyecto de tests.
- Textos de UI en español, sin acentos en copy nuevo; iconos con emoji.
- Migraciones idempotentes: toda tabla nueva va en un `Ensure*` o el arranque falla.
- Imagen: reusar `ImageStorageService` (bucket `trips`) y el procesamiento de `TripImageProcessor`; formatos `.jpg/.jpeg/.png/.webp`; borrar la anterior al reemplazar.
- Foto y placas **opcionales** (al menos uno); **notificación solo la primera vez**; **borrado solo al finalizar**.
- No usar transacción explícita (choca con `AuditLogInterceptor`).
- Commit solo con confirmación explícita del usuario (AGENTS.md).

## Review Focus

- Guardar sin foto y sin placas → `400`; guardar solo placas (o solo foto) → permitido y **no** borra el otro campo.
- Re-guardar con la misma info (o con info ya presente) → **no** re-notifica; solo la transición ausente → presente notifica.
- Reemplazar la imagen → borra la anterior del storage.
- Finalizar el viaje → se elimina la fila `TripTransport` y la imagen; el cliente vuelve a "Se Actualizara Pronto".
- Imagen con formato/tamaño inválido → `400` (mismo tratamiento que las imágenes de viaje).

---

### Task 1: Modelo compartido `TripTransport` + `TripTransportInfo`

**Files:**
- Create: `src/TravelAgency.Shared/Models/TripTransport.cs`

**Interfaces:**
- Produces: `TripTransport` (Id, TripId, ImageUrl?, Plate?, CreatedAt, UpdatedAt?); `record TripTransportInfo(string? ImageUrl, string? Plate)`.

- [ ] **Step 1: Crear `TripTransport.cs`**

Namespace `TravelAgency.Shared.Models`:
```csharp
public class TripTransport
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public string? ImageUrl { get; set; }
    public string? Plate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public record TripTransportInfo(string? ImageUrl, string? Plate);
```

- [ ] **Step 2: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 3: Commit**

```bash
git add src/TravelAgency.Shared/Models/TripTransport.cs
git commit -m "transporte: modelo TripTransport e info"
```

---

### Task 2: `AppDbContext` + migración `TripTransports`

**Files:**
- Modify: `src/TravelAgency.Api/Data/AppDbContext.cs`
- Modify: `src/TravelAgency.Api/Program.cs`

**Interfaces:**
- Consumes: `TripTransport`.
- Produces: `AppDbContext.TripTransports`; `EnsureTripTransportsTableAsync(AppDbContext, string)`.

- [ ] **Step 1: DbSet + config**

`public DbSet<TripTransport> TripTransports => Set<TripTransport>();`. En `OnModelCreating`:
```csharp
modelBuilder.Entity<TripTransport>(e =>
{
    e.ToTable("TripTransports");
    e.Property(x => x.ImageUrl).HasMaxLength(500);
    e.Property(x => x.Plate).HasMaxLength(30);
    e.HasIndex(x => x.TripId).IsUnique().HasDatabaseName("IX_TripTransports_TripId");
    e.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
});
```

- [ ] **Step 2: `EnsureTripTransportsTableAsync`**

En `Program.cs` (patrón `Ensure*`, ramas sqlite/postgres): `TripTransports` (`Id` PK identity/autoincrement, `TripId` int NOT NULL, `ImageUrl` varchar(500) null, `Plate` varchar(30) null, `CreatedAt` timestamp/text NOT NULL, `UpdatedAt` timestamp/text null), FK a `Trips` `ON DELETE CASCADE`, índice UNIQUE `IX_TripTransports_TripId`, RLS activo. Llamarla en el arranque junto a las demás `Ensure*`.

- [ ] **Step 3: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 4: Commit**

```bash
git add src/TravelAgency.Api/Data/AppDbContext.cs src/TravelAgency.Api/Program.cs
git commit -m "transporte: tabla TripTransports con migracion idempotente"
```

---

### Task 3: Endpoints de transporte + borrado en finalize/delete

**Files:**
- Modify: `src/TravelAgency.Api/Program.cs`

**Interfaces:**
- Consumes: `TripTransport`, `TripTransportInfo`, `ImageStorageService`, `TripImageProcessor`, `SendToStaffAsync` (para el patrón de notificación).
- Produces: `POST /api/trips/{id}/transport` (AdminOnly, multipart), `GET /api/trips/{id}/transport`.

- [ ] **Step 1: `GET /api/trips/{id}/transport`**

`app.MapGet("/api/trips/{id}/transport", ...)` (RequireAuthorization): devuelve `TripTransportInfo` del viaje o `null` si no existe. `Results.Ok(new TripTransportInfo(t.ImageUrl, t.Plate))` o `Results.Ok((TripTransportInfo?)null)`.

- [ ] **Step 2: `POST /api/trips/{id}/transport`**

`app.MapPost(...).RequireAuthorization("AdminOnly")` (patrón del endpoint `POST /api/trips/{id}/image`, línea ~366):
- Validar viaje existe; `plate = request.Form["plate"].ToString().Trim()`; `file = request.Form.Files.FirstOrDefault()`.
- Si no hay `file` y `plate` está vacío → `400` ("Agrega una fotografia o las placas.").
- Cargar/crear la fila `TripTransport` del viaje. Detectar `wasEmpty = existing is null || (string.IsNullOrWhiteSpace(existing.ImageUrl) && string.IsNullOrWhiteSpace(existing.Plate))`.
- Si hay `file`: validar extensión/tamaño (igual que la imagen de viaje), `TripImageProcessor.Prepare`, `storage.UploadAsync`; guardar `ImageUrl` y borrar la anterior (`storage.DeleteAsync(previous)`).
- Si `plate` no vacío: `Plate = plate.Length <= 30 ? plate : plate[..30]`.
- `UpdatedAt = DateTime.UtcNow`; `SaveChangesAsync`.
- Si `wasEmpty` y ahora hay info → notificar a los usuarios con reservas no canceladas del viaje (`db.Bookings.Where(b => b.TripId == id && b.Status != BookingStatus.Cancelled).Select(b => b.UserId).Distinct()`) con `UserNotification`: `$"Ya esta disponible la informacion del transporte para tu proximo viaje a {trip.Destination}."`; `SaveChangesAsync`.
- Responder `TripTransportInfo`.

- [ ] **Step 3: Borrado al finalizar**

En `POST /api/trips/{id}/finalize`, dentro del bloque `if (request.Finalized)`: cargar la fila `TripTransport` del viaje; si existe, borrar la imagen (`storage.DeleteAsync(t.ImageUrl)`), `db.TripTransports.Remove(t)` y `SaveChangesAsync` (junto con la notificación existente).

- [ ] **Step 4: Borrado al eliminar el viaje**

En `DELETE /api/trips/{id}`, junto al borrado de `trip.ImageUrl` (línea ~613): cargar la fila `TripTransport`, borrar su imagen del storage (la fila cae por cascade al borrar el Trip).

- [ ] **Step 5: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 6: Commit**

```bash
git add src/TravelAgency.Api/Program.cs
git commit -m "transporte: endpoints, notificacion y borrado al finalizar"
```

---

### Task 4: ApiService

**Files:**
- Modify: `src/TravelAgency.App/Services/ApiService.cs`

**Interfaces:**
- Consumes: endpoints de Task 3; `TripTransportInfo` (Shared).
- Produces: `GetTripTransportAsync(int tripId) -> Task<TripTransportInfo?>`; `UploadTripTransportAsync(int tripId, FileResult? file, string? plate) -> Task<TripTransportInfo?>`.

- [ ] **Step 1: Métodos**

- `GetTripTransportAsync`: `GET /api/trips/{tripId}/transport` → `TripTransportInfo?`.
- `UploadTripTransportAsync`: `multipart/form-data` a `POST /api/trips/{tripId}/transport` con `file` (si hay) y `plate` (si hay); `EnsureSuccessStatusCode`; lee `TripTransportInfo?`. (Patrón de `UploadTripImageAsync`, línea ~271.)

- [ ] **Step 2: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 3: Commit**

```bash
git add src/TravelAgency.App/Services/ApiService.cs
git commit -m "transporte: ApiService de info de la unidad"
```

---

### Task 5: UI admin (detalle del viaje)

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripDetailPage.xaml(.cs)`

**Interfaces:**
- Consumes: `ApiService.GetTripTransportAsync`, `UploadTripTransportAsync`.

- [ ] **Step 1: Botón + sección**

En `AdminTripDetailPage.xaml`, agregar el botón **"Agregar Informacion de la Unidad"** (visible para Admin, junto a las demás acciones) y una sección que muestre la info actual (imagen + placas) o "Sin informacion".

- [ ] **Step 2: Code-behind**

- `OnAppearing`/`LoadTripAsync`: `GetTripTransportAsync(_trip.Id)` y renderizar (imagen vía `_api.GetTripImageAsync`, texto de placas).
- Al pulsar: abrir la captura (una sección inline con `ImagePreview` + `Entry` de placas + Guardar, o un `DisplayPrompt` para placas + `MediaPicker` para la foto, patrón de `AdminDashboardPage.OnPickImageClicked`). Guardar → `UploadTripTransportAsync(_trip.Id, _selectedImage, plate)`; refrescar; alerta.

- [ ] **Step 3: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 4: Verificar manualmente**

- Admin agrega foto + placas → se ven en el detalle y se suben a Supabase.

- [ ] **Step 5: Commit**

```bash
git add src/TravelAgency.App/Modules/Admin/Views/AdminTripDetailPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminTripDetailPage.xaml.cs
git commit -m "admin: agregar informacion de la unidad al viaje"
```

---

### Task 6: UI cliente (detalle de reserva)

**Files:**
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml(.cs)`

**Interfaces:**
- Consumes: `ApiService.GetTripTransportAsync`.

- [ ] **Step 1: Sección "Información del Transporte"**

En `ClientMyBookingDetailPage.xaml`, tarjeta (estilo existente) con `TransportImage` + `TransportPlateLabel` y un `TransportSoonLabel` ("Se Actualizara Pronto").

- [ ] **Step 2: Code-behind**

En `LoadBookingAsync`, si `trip?.Id` válido: `GetTripTransportAsync(trip.Id)`. Si hay `ImageUrl`/`Plate`: mostrar la sección con la imagen (`GetTripImageAsync`) + placas; si no, mostrar "Se Actualizara Pronto".

- [ ] **Step 3: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 4: Verificar manualmente**

- Cliente con reserva ve la foto + placas; sin info ve "Se Actualizara Pronto".

- [ ] **Step 5: Commit**

```bash
git add src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml.cs
git commit -m "cliente: seccion de informacion del transporte en la reserva"
```

---

## Verificación final (todo el plan)

- `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj`
- `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
- Recorrido manual: admin publica foto+placas → cliente ve la sección y recibe la notificación → re-guardar no re-notifica → finalizar el viaje borra la fila y la imagen → el cliente vuelve a "Se Actualizara Pronto".
