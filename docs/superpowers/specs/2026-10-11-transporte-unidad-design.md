# Diseño — Información de la unidad (transporte) por viaje

Fecha: 2026-10-11
Estado: propuesto (pendiente de revisión)

## Contexto y objetivo

El admin necesita publicar, **por viaje**, la información de la unidad de transporte (una **fotografía** y las **placas**), de forma **temporal**: se muestra al cliente en el detalle de su reserva y se **elimina al finalizar** el viaje. Al publicarla, el cliente con reserva en ese viaje recibe una **notificación**.

## Decisiones (confirmadas)

1. **Tabla nueva** `TripTransports` (1:1 con `Trip`), no columnas en `Trips`.
2. Botón de admin en el **detalle del viaje** (`AdminTripDetailPage`).
3. **Foto y placas opcionales** (al menos uno de los dos).
4. **Notificación solo la primera vez** que se publica (transición ausente → presente).
5. **Borrado solo al finalizar** el viaje manualmente.

## Modelo de datos

### `TripTransport` (1:1 con `Trip`)

```
Id, TripId (int, UNIQUE, FK → Trips ON DELETE CASCADE),
ImageUrl (varchar 500, null), Plate (varchar 30, null),
CreatedAt, UpdatedAt (null)
```

- `AppDbContext`: `DbSet<TripTransport> TripTransports`; config `ToTable("TripTransports")`, `HasIndex(t => t.TripId).IsUnique()`, `ImageUrl` máx 500, `Plate` máx 30, FK a `Trips` con `OnDelete(DeleteBehavior.Cascade)`.
- Migración idempotente `EnsureTripTransportsTableAsync` (sqlite/postgres, `IF NOT EXISTS`, índice único, FK cascade, RLS como las demás), llamada en el arranque.

## API

### `POST /api/trips/{id}/transport` (AdminOnly)

`multipart/form-data` con campos opcionales `file` (imagen) y `plate` (texto).
- Validar: el viaje existe; al menos uno de `file`/`plate` presente; formato de imagen permitido (`.jpg/.jpeg/.png/.webp`, reusa `TripImageProcessor` + `ImageStorageService`), tamaño dentro del límite.
- **Upsert** de la fila `TripTransport` del viaje (crear si no existe).
- Si viene `file`: subir al storage (bucket `trips`), guardar `ImageUrl` y **borrar la imagen anterior** de ese viaje.
- Si viene `plate`: guardar `Plate` (trim, ≤ 30).
- `UpdatedAt = UtcNow`.
- **Notificar solo en la transición ausente → presente** (la fila no existía o `ImageUrl`/`Plate` estaban vacíos y ahora hay algo): a los usuarios con reservas **no canceladas** del viaje: *"Ya esta disponible la informacion del transporte para tu proximo viaje a {Destination}."* (crear `UserNotification`).
- Responder `{ imageUrl, plate }`.

### `GET /api/trips/{id}/transport` (autenticado)

Devuelve `{ imageUrl, plate }` del viaje, o `null` si no hay información. Lo consume el cliente.

### Borrado al finalizar

En `POST /api/trips/{id}/finalize`, cuando `request.Finalized == true`: eliminar la fila `TripTransport` del viaje y **borrar la imagen** del storage (`storage.DeleteAsync`).

### Borrado al eliminar el viaje

En `DELETE /api/trips/{id}`: además de la imagen principal, borrar la imagen de transporte (`storage.DeleteAsync(tripTransport.ImageUrl)`). La fila cae por cascade.

## UI

### Admin — `AdminTripDetailPage`

- Botón **"Agregar Informacion de la Unidad"** (visible para Admin) → abre una vista/sección (modal o inline) con:
  - Selector de fotografía (`MediaPicker`, patrón de `AdminDashboardPage.OnPickImageClicked`) con previsualización.
  - `Entry` para las placas.
  - Botón Guardar → `POST /api/trips/{id}/transport`.
  - Muestra la info actual (foto + placas) si existe.
- Tras guardar, refrescar.

### Cliente — `ClientMyBookingDetailPage`

- Nueva sección **"Información del Transporte"** (tarjeta estilo existente): si hay info, mostrar la fotografía + el texto de las placas; si no, mostrar el label **"Se Actualizara Pronto"**.
- Carga vía `GET /api/trips/{booking.Trip.Id}/transport`.

## Casos límite

- Sin foto y sin placas → `400` (al menos uno).
- Guardar solo placas (sin foto) o solo foto (sin placas) → permitido; no borra el otro campo.
- Re-guardar con la misma info → no re-notifica (solo ausente → presente).
- Reemplazar la imagen → borra la anterior del storage.
- Viaje finalizado o eliminado → sin fila `TripTransport`; el cliente muestra "Se Actualizara Pronto".
- Imagen: mismo tratamiento de formato/tamaño/placeholder que las imágenes de viaje.
- El bucket ya existe (se reutiliza `trips`).

## Fuera de alcance

- Varias unidades/imágenes por viaje o historial de cambios de unidad.
- Edición por coordinadores (solo Admin, como el resto de la gestión de viajes).
- Borrado automático por fecha de fin (solo finalización manual).

## Verificación

- Build API + App.
- Manual:
  1. Admin agrega foto + placas a un viaje → se sube a Supabase y se ve en el detalle.
  2. El cliente con reserva en ese viaje ve la sección con foto + placas y recibe la notificación.
  3. Cliente con reserva en un viaje sin info → "Se Actualizara Pronto".
  4. Admin finaliza el viaje → la fila `TripTransport` y la imagen se eliminan; el cliente vuelve a "Se Actualizara Pronto".
  5. Re-guardar no re-notifica.
  6. Validaciones: sin foto ni placas → `400`; formato/tamaño inválidos → `400`.

## Archivos afectados (estimado)

- `src/TravelAgency.Shared/Models/TripTransport.cs` (nuevo)
- `src/TravelAgency.Api/Data/AppDbContext.cs`
- `src/TravelAgency.Api/Program.cs` (endpoints + migración + borrado en finalize/delete)
- `src/TravelAgency.App/Services/ApiService.cs`
- `src/TravelAgency.App/Modules/Admin/Views/AdminTripDetailPage.xaml(.cs)`
- `src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml(.cs)`
