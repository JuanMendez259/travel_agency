# Autobús de dos pisos — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que un viaje en `Camion` se marque como autobús de dos pisos con cupo por piso, y que cliente y admin seleccionen/vean asientos por piso.

**Architecture:** El piso es una agrupación de presentación; el asiento conserva su número global continuo (`1..Capacity`, `Capacity = F1 + F2`), de modo que la validación de reserva, ocupación y auto-asignación no cambian. `Trip` gana tres campos; el mapa de asientos gana `Floor` por fila y metadatos de piso en los DTO; `BuildSeatRows` se generaliza a dos pisos. Cliente y admin agregan un selector de piso.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API + EF Core (Supabase/Postgres; SQLite fallback), .NET MAUI (net10.0-maccatalyst). Sin proyecto de tests.

**Spec:** `docs/superpowers/specs/2026-10-08-autobus-dos-pisos-design.md`

## Global Constraints

- .NET 10. App: `net10.0-maccatalyst`. API: `src/TravelAgency.Api`.
- **No existe proyecto de tests.** Verificar con `dotnet build` + comprobaciones manuales/HTTP indicadas en cada tarea. No crear proyecto de tests salvo pedido explícito.
- Textos de UI en español, en el estilo existente (sin acentos en copy nuevo), iconos con emoji.
- Migraciones idempotentes: cualquier columna nueva debe agregarse en un `Ensure*` o el arranque de la API falla.
- Dos pisos **solo** cuando `TransportType == Camion`; en otro caso normalizar a planta única.
- Numeración global continua: Piso 1 = `1..F1`, Piso 2 = `F1+1..F1+F2`; `Capacity = F1 + F2`.
- Viajes existentes/nuevos sin el switch: comportamiento idéntico (`HasTwoFloors = false`).
- Commit solo con confirmación explícita del usuario (AGENTS.md).

## Review Focus

- Switch de dos pisos activo con `TransportType == Camioneta` → el viaje se guarda como planta única, sin `Floor1Capacity`/`Floor2Capacity`, y `Capacity` es la capturada.
- `Floor1Capacity` o `Floor2Capacity` vacío/0/negativo con el switch activo → error de validación, no se guarda un viaje con piso vacío.
- Reservar asientos del piso 2 (números `> F1`) → se validan y se ocupan correctamente (rango global `1..Capacity`).
- Editar un viaje de una planta → no se activa `HasTwoFloors` ni se pierde/duplica `Capacity`.
- Hotel + dos pisos → `Capacity = F1+F2`, `BookableCapacity = min(Capacity, HotelCapacity)` y el mapa usa `F1+F2`.

---

### Task 1: Modelo `Trip` + migración de columnas

**Files:**
- Modify: `src/TravelAgency.Shared/Models/Trip.cs`
- Modify: `src/TravelAgency.Api/Data/AppDbContext.cs`
- Modify: `src/TravelAgency.Api/Program.cs`

**Interfaces:**
- Produces: `Trip.HasTwoFloors` (`bool`), `Trip.Floor1Capacity` (`int?`), `Trip.Floor2Capacity` (`int?`); `EnsureTripFloorsColumnsAsync(AppDbContext db, string provider)`.

- [ ] **Step 1: Agregar los campos al modelo `Trip`**

En `Trip.cs`, junto a `IncludesHotel`/`HotelCapacity`:
```csharp
public bool HasTwoFloors { get; set; }
public int? Floor1Capacity { get; set; }
public int? Floor2Capacity { get; set; }
```

- [ ] **Step 2: Configurar en `AppDbContext`**

En la config de `Trip` (`OnModelCreating`), junto a las demás propiedades:
```csharp
entity.Property(t => t.HasTwoFloors).IsRequired();
```

- [ ] **Step 3: Agregar `EnsureTripFloorsColumnsAsync`**

En `Program.cs`, junto a `EnsureTripHotelColumnsAsync` (patrón `ColumnExistsAsync` + `TryExecAsync`):
- `Trips.HasTwoFloors`: sqlite `INTEGER NOT NULL DEFAULT 0`; postgres `boolean NOT NULL DEFAULT false`.
- `Trips.Floor1Capacity`: sqlite `INTEGER NULL`; postgres `integer NULL`.
- `Trips.Floor2Capacity`: sqlite `INTEGER NULL`; postgres `integer NULL`.

Y llamarla en el bloque de arranque, justo después de `EnsureTripHotelColumnsAsync(db, provider);`.

- [ ] **Step 4: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 5: Verificar la columna en la BD (tras desplegar o en local)**

Si hay conexión a Supabase, con `psql`:
`SELECT column_name FROM information_schema.columns WHERE table_name='Trips' AND column_name IN ('HasTwoFloors','Floor1Capacity','Floor2Capacity');`
Expected: 3 filas.

- [ ] **Step 6: Commit**

```bash
git add src/TravelAgency.Shared/Models/Trip.cs src/TravelAgency.Api/Data/AppDbContext.cs src/TravelAgency.Api/Program.cs
git commit -m "viajes: campos de dos pisos en Trip con migracion idempotente"
```

---

### Task 2: Modelo compartido de asientos

**Files:**
- Modify: `src/TravelAgency.Shared/Models/TripSeatMap.cs`
- Modify: `src/TravelAgency.Shared/Models/TripSeatAvailability.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `TripSeatRow.Floor` (`int`, default `1`); `TripSeatMap.HasTwoFloors`/`Floor1Capacity`/`Floor2Capacity`; `TripSeatAvailability.HasTwoFloors`/`Floor1Capacity`/`Floor2Capacity`.

- [ ] **Step 1: `TripSeatRow.Floor`**

En `TripSeatMap.cs`, agregar a `TripSeatRow`:
```csharp
public int Floor { get; set; } = 1;
```

- [ ] **Step 2: Metadatos de piso en los DTO**

Agregar en **ambas** clases (`TripSeatMap` y `TripSeatAvailability`):
```csharp
public bool HasTwoFloors { get; set; }
public int? Floor1Capacity { get; set; }
public int? Floor2Capacity { get; set; }
```

- [ ] **Step 3: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 4: Commit**

```bash
git add src/TravelAgency.Shared/Models/TripSeatMap.cs src/TravelAgency.Shared/Models/TripSeatAvailability.cs
git commit -m "asientos: piso por fila y metadatos de pisos en los DTO"
```

---

### Task 3: `BuildSeatRows` a dos pisos + endpoints

**Files:**
- Modify: `src/TravelAgency.Api/Program.cs` (`BuildSeatRows`, `GET /api/trips/{id:int}/seatmap`, `GET /api/trips/{id:int}/seats`)

**Interfaces:**
- Consumes: `Trip.HasTwoFloors`/`Floor1Capacity`/`Floor2Capacity`; `TripSeatRow.Floor`; campos de piso en los DTO.
- Produces: `List<TripSeatRow> BuildSeatRows(int floor1Capacity, int floor2Capacity, Dictionary<int, TripSeat> assigned)`.

- [ ] **Step 1: Reescribir `BuildSeatRows`**

Nueva firma `BuildSeatRows(int floor1Capacity, int floor2Capacity, Dictionary<int, TripSeat> assigned)`:
- Piso 1: filas para `floor1Capacity` asientos, `Floor = 1`, `RowNumber` desde 1, asientos `1..F1`.
- Si `floor2Capacity > 0`: filas para `floor2Capacity` asientos, `Floor = 2`, `RowNumber` desde 1, asientos `F1+1..F1+F2`.
- El contador de asiento es global y continuo; layout por fila sin cambios (2 + pasillo + 2).

- [ ] **Step 2: Endpoint `seatmap` (staff)**

Calcular `f1 = trip.HasTwoFloors ? (trip.Floor1Capacity ?? 0) : capacity`, `f2 = trip.HasTwoFloors ? (trip.Floor2Capacity ?? 0) : 0`. Setear `HasTwoFloors`/`Floor1Capacity`/`Floor2Capacity` en el `TripSeatMap` y llamar `BuildSeatRows(f1, f2, assigned)`.

- [ ] **Step 3: Endpoint `seats` (cliente)**

Igual que el paso 2, sobre `TripSeatAvailability`.

- [ ] **Step 4: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 5: Verificar salida del endpoint (local, SQLite)**

Levantar la API local y, con un token válido, `GET /api/trips/{id}/seats` de un viaje con dos pisos (F1=20, F2=15).
Expected: `Rows` con filas `Floor=1` (asientos 1–20) y `Floor=2` (asientos 21–35); `HasTwoFloors=true`.

- [ ] **Step 6: Commit**

```bash
git add src/TravelAgency.Api/Program.cs
git commit -m "asientos: mapa por piso con numeracion global continua"
```

---

### Task 4: Normalización y validación en crear/editar viaje

**Files:**
- Modify: `src/TravelAgency.Api/Program.cs` (`POST /api/trips`, `PUT /api/trips/{id}`)

**Interfaces:**
- Consumes: `Trip.HasTwoFloors`/`Floor1Capacity`/`Floor2Capacity`.
- Produces: viajes con pisos normalizados (`Capacity = F1+F2`) o planta única.

- [ ] **Step 1: Helper de normalización**

Agregar un helper local, p. ej. `static string? NormalizeAndValidateFloors(Trip trip)`:
- Si `trip.HasTwoFloors && trip.TransportType == TransportType.Camion`:
  - `Floor1Capacity < 1` ⇒ devolver `"El cupo del piso 1 debe ser al menos 1."`.
  - `Floor2Capacity < 1` ⇒ devolver `"El cupo del piso 2 debe ser al menos 1."`.
  - `trip.Capacity = Floor1Capacity.Value + Floor2Capacity.Value`.
- En cualquier otro caso: `HasTwoFloors = false`, `Floor1Capacity = null`, `Floor2Capacity = null`.
- Devolver `null` si todo bien.

- [ ] **Step 2: Aplicar en `POST /api/trips`**

Llamar al helper tras las validaciones existentes (antes de `db.Trips.Add(trip)`); si devuelve mensaje, `return Results.BadRequest(mensaje)`. Nota: `AvailableSeats = trip.Capacity` ya se asigna después; el orden debe dejar `Capacity` calculado antes de esa línea.

- [ ] **Step 3: Aplicar en `PUT /api/trips/{id}`**

Llamar al helper sobre `input`; si hay error, `BadRequest`. Luego, al mapear campos, copiar `trip.HasTwoFloors = input.HasTwoFloors; trip.Floor1Capacity = input.Floor1Capacity; trip.Floor2Capacity = input.Floor2Capacity;` antes de `await RecomputeAvailabilityAsync(db, trip);`.

- [ ] **Step 4: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 5: Verificar normalización (local/HTTP)**

- Camión + dos pisos F1=20, F2=15 ⇒ respuesta con `Capacity=35`, `HasTwoFloors=true`.
- Camioneta + dos pisos ⇒ `HasTwoFloors=false`, `Floor1Capacity=null`, `Floor2Capacity=null`, `Capacity` = capturada.
- Camión + dos pisos F2=0 ⇒ `400` con el mensaje del piso 2.

- [ ] **Step 6: Commit**

```bash
git add src/TravelAgency.Api/Program.cs
git commit -m "viajes: normalizar y validar pisos al crear/editar"
```

---

### Task 5: Formulario admin (switch + cupos por piso)

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminDashboardPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminDashboardPage.xaml.cs`

**Interfaces:**
- Consumes: `Trip.HasTwoFloors`/`Floor1Capacity`/`Floor2Capacity`; `TransportType.Camion`.
- Produces: viaje enviado con pisos correctos.

- [ ] **Step 1: XAML — switch y campos**

Junto al bloque de capacidad: un switch `TwoFloorsSwitch` "Autobús de dos pisos" (con `Toggled="OnTwoFloorsToggled"`) y un layout `FloorFieldsLayout` (`IsVisible=False`) con `Floor1CapacityEntry`, `Floor2CapacityEntry` y `FloorsTotalLabel` ("Total: N"). El switch y el layout deben poder ocultarse cuando el transporte no sea Camión.

- [ ] **Step 2: Code-behind — visibilidad por transporte**

En el handler del `TransportTypePicker` (`SelectedIndexChanged`): mostrar el switch solo si el índice corresponde a `Camion`; si no, forzar `TwoFloorsSwitch.IsToggled = false` y ocultar `FloorFieldsLayout`. Agregar el handler en XAML.

- [ ] **Step 3: Code-behind — toggle y total**

`OnTwoFloorsToggled`: cuando `true`, ocultar el bloque de "Capacidad total" (p. ej. envolver `CapacityEntry` en un layout `SingleCapacityLayout`) y mostrar `FloorFieldsLayout`; cuando `false`, lo inverso. Actualizar `FloorsTotalLabel` al cambiar F1/F2 (handler `TextChanged`).

- [ ] **Step 4: Code-behind — leer y validar en `OnSaveClicked`**

Al construir el `Trip`: setear `HasTwoFloors = TwoFloorsSwitch.IsToggled && TransportTypePicker.SelectedIndex == (int)TransportType.Camion`, `Floor1Capacity`/`Floor2Capacity` parseados. Si el switch está activo: exigir F1≥1 y F2≥1 (si no, `DisplayAlert` y return) y `Capacity = F1 + F2`. Si no, usar `CapacityEntry` como hoy.

- [ ] **Step 5: `StartEdit` y `ClearFormAsync`**

`StartEdit`: `TwoFloorsSwitch.IsToggled = trip.HasTwoFloors`, cargar `Floor1CapacityEntry`/`Floor2CapacityEntry`, aplicar visibilidad por transporte. `ClearFormAsync`: switch off, limpiar cupos, restaurar layout de capacidad única.

- [ ] **Step 6: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 7: Verificar manualmente**

- Transporte Camioneta ⇒ no aparece el switch.
- Camión + activar ⇒ aparecen Cupo piso 1/2 y Total; guardar crea el viaje con `Capacity=F1+F2`.
- Editar un viaje de una planta ⇒ switch off, capacidad única intacta.

- [ ] **Step 8: Commit**

```bash
git add src/TravelAgency.App/Modules/Admin/Views/AdminDashboardPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminDashboardPage.xaml.cs
git commit -m "admin: formulario con opcion de autobus de dos pisos"
```

---

### Task 6: Selección de asientos del cliente con selector de piso

**Files:**
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientBookingSeatsPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientBookingSeatsPage.xaml.cs`

**Interfaces:**
- Consumes: `TripSeatAvailability.HasTwoFloors`/`Floor1Capacity`/`Floor2Capacity`; `TripSeatRow.Floor`.
- Produces: render filtrado por piso conservando la selección.

- [ ] **Step 1: XAML — selector de piso y rótulo dinámico**

Reemplazar el label fijo "Planta única" por `FloorBadgeLabel` (texto dinámico). Agregar un contenedor `FloorSelector` (dos botones/chips "Piso 1" y "Piso 2", `IsVisible=False`) sobre la silueta del autobús, con `Clicked` a un handler.

- [ ] **Step 2: Code-behind — estado de piso**

Agregar `int _currentFloor = 1;` y guardar la lista de `TripSeatRow` recibida (`_rows`). Tras cargar `availability`, si `availability.HasTwoFloors`: `FloorSelector.IsVisible = true; FloorBadgeLabel.Text = "Dos pisos";` y resaltar el chip activo; si no, `FloorSelector.IsVisible = false; FloorBadgeLabel.Text = "Planta única";`.

- [ ] **Step 3: Code-behind — render por piso**

Modificar `RenderMap` (o extraer `RenderFloor(int floor)`) para dibujar solo `_rows.Where(r => r.Floor == floor)`. Los handlers de los chips cambian `_currentFloor`, actualizan el resaltado y re-renderizan. La selección (`_slots[].Seat`) no se toca al cambiar de piso.

- [ ] **Step 4: Code-behind — etiquetas con prefijo de piso**

Donde se muestran los asientos elegidos (resumen `SelectedSeatsLabel` y el badge por pasajero), anteponer `P{floor} ` cuando el viaje tiene dos pisos; p. ej. `P1 #5`, `P2 #25`. Helper: piso de un número = `n <= F1 ? 1 : 2`.

- [ ] **Step 5: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 6: Verificar manualmente**

- Viaje de dos pisos: aparece el selector; Piso 1 muestra 1..F1 y Piso 2 F1+1..F1+F2; se pueden elegir asientos en ambos pisos y se conservan al cambiar de piso; confirmar reserva valida y ocupa bien.
- Viaje de una planta: sin selector, igual que antes.

- [ ] **Step 7: Commit**

```bash
git add src/TravelAgency.App/Modules/Client/Views/ClientBookingSeatsPage.xaml src/TravelAgency.App/Modules/Client/Views/ClientBookingSeatsPage.xaml.cs
git commit -m "cliente: selector de piso en la eleccion de asientos"
```

---

### Task 7: Mapa de asientos del admin con selector de piso

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripSeatsPage.xaml`
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminTripSeatsPage.xaml.cs`

**Interfaces:**
- Consumes: `TripSeatMap.HasTwoFloors`/`Floor1Capacity`/`Floor2Capacity`; `TripSeatRow.Floor`.
- Produces: mapa del admin filtrado por piso.

- [ ] **Step 1: XAML — selector de piso**

Agregar `FloorSelector` (chips "Piso 1"/"Piso 2", `IsVisible=False`) sobre `RowsContainer`.

- [ ] **Step 2: Code-behind — render por piso**

Guardar `_rows`; si `map.HasTwoFloors`, mostrar el selector y renderizar solo las filas del piso activo; el resumen incluye "Piso 1: F1 · Piso 2: F2". Los chips cambian el piso activo y re-renderizan.

- [ ] **Step 3: Code-behind — ocupados con piso**

En la lista de ocupados, mostrar `Piso {floor} · Asiento {Number}` (piso derivado de `Floor` de la fila, o `n <= F1 ? 1 : 2`).

- [ ] **Step 4: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 5: Verificar manualmente**

- Viaje de dos pisos: selector visible; ocupados muestran prefijo de piso; cambiar de piso re-renderiza.
- Viaje de una planta: sin selector, igual que antes.

- [ ] **Step 6: Commit**

```bash
git add src/TravelAgency.App/Modules/Admin/Views/AdminTripSeatsPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminTripSeatsPage.xaml.cs
git commit -m "admin: mapa de asientos con selector de piso"
```

---

### Task 8: Resumen de capacidad por piso en el detalle del viaje (cliente)

**Files:**
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientTripDetailPage.xaml.cs`
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientTripDetailPage.xaml` (si se requiere un label nuevo)

**Interfaces:**
- Consumes: `Trip.HasTwoFloors`/`Floor1Capacity`/`Floor2Capacity`.
- Produces: resumen de capacidad con desglose por piso.

- [ ] **Step 1: Mostrar pisos en el resumen de capacidad**

En `LoadTripAsync`, si `_trip.HasTwoFloors`, ajustar el texto de capacidad (p. ej. `CapacityTitleLabel`/`SeatsLeftLabel`) para incluir `Piso 1: F1 · Piso 2: F2`. Sin dos pisos: texto actual.

- [ ] **Step 2: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 3: Commit**

```bash
git add src/TravelAgency.App/Modules/Client/Views/ClientTripDetailPage.xaml.cs src/TravelAgency.App/Modules/Client/Views/ClientTripDetailPage.xaml
git commit -m "cliente: resumen de capacidad por piso en detalle del viaje"
```

---

## Verificación final (todo el plan)

- `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj`
- `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
- Recorrido manual completo (crear viaje de dos pisos → reservar en ambos pisos → admin ve mapa por piso → hotel + dos pisos → viaje de una planta sin cambios).
