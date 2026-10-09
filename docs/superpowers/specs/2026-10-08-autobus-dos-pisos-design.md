# Diseño — Autobús de dos pisos (dos niveles)

Fecha: 2026-10-08
Estado: propuesto (pendiente de revisión)

## Contexto y objetivo

El administrador define el cupo del transporte por viaje. Algunos autobuses tienen
**dos pisos**, lo que hoy se representa como una sola planta de `1..Capacity`, y la
selección de asientos se vuelve confusa.

Objetivo: permitir al admin marcar que un viaje usa **autobús de dos pisos** y definir
el cupo de cada piso; y que el cliente (y el admin) seleccionen asientos viendo el
mapa del piso correspondiente.

Alcance acotado a `TransportType.Camion` (el "camión/autobús" de la app). Para
`Camioneta` no aplica.

## Estado actual

- El asiento se identifica con un entero global `TripPassenger.SeatNumber` en `1..Capacity`.
- `BuildSeatRows(capacity, assigned)` arma filas de 4 asientos (2 + pasillo + 2).
- El mapa lo comparten:
  - Cliente: `GET /api/trips/{id}/seats` → `TripSeatAvailability`.
  - Admin: `GET /api/trips/{id}/seatmap` → `TripSeatMap`.
- Validación de reserva, cálculo de ocupación y auto-asignación usan el rango `1..Capacity`.

## Decisiones (confirmadas)

1. **Numeración global continua** con prefijo de piso en etiquetas.
   Piso 1 = `1..F1`; Piso 2 = `F1+1..F1+F2`. `Capacity = F1 + F2`.
2. La opción **solo aplica a `TransportType.Camion`**.
3. El **mapa del admin** usa selector de piso (paridad con el cliente).
4. Con **hotel + dos pisos**: `Capacity = F1 + F2` y el cupo del hotel sigue aplicando
   como tope aparte (`BookableCapacity = min(Capacity, HotelCapacity)`).

## Diseño

### 1. Modelo de datos (`Trip`)

Campos nuevos:

- `bool HasTwoFloors` — default `false`.
- `int? Floor1Capacity` — cupo del piso 1 (null si no aplica).
- `int? Floor2Capacity` — cupo del piso 2 (null si no aplica).

Invariante de normalización (en creación y edición):

- Si `HasTwoFloors == true` **y** `TransportType == Camion`: se exige `F1 >= 1` y `F2 >= 1`,
  y `Capacity = F1 + F2`.
- En cualquier otro caso: `HasTwoFloors = false`, `Floor1Capacity = null`,
  `Floor2Capacity = null`, y `Capacity` se usa tal cual lo captura el admin.

`AppDbContext`: configurar `HasTwoFloors` (requerido) y las dos capacidades (nullable).

Migración idempotente `EnsureTripFloorsColumnsAsync(db, provider)` siguiendo el patrón
de `EnsureTripHotelColumnsAsync`:

- `Trips.HasTwoFloors`: sqlite `INTEGER NOT NULL DEFAULT 0`; postgres `boolean NOT NULL DEFAULT false`.
- `Trips.Floor1Capacity`: `INTEGER NULL` / `integer NULL`.
- `Trips.Floor2Capacity`: `INTEGER NULL` / `integer NULL`.

Se llama junto a `EnsureTripHotelColumnsAsync`. Los viajes existentes quedan en
`HasTwoFloors = false` (comportamiento idéntico al actual).

### 2. Modelo compartido de asientos

- `TripSeatRow`: nuevo `int Floor` (default `1`).
- `TripSeatMap` y `TripSeatAvailability`: nuevos
  `bool HasTwoFloors`, `int? Floor1Capacity`, `int? Floor2Capacity`.
- Numeración global continua; `RowNumber` reinicia en cada piso.

### 3. API

`BuildSeatRows` pasa a:

```
List<TripSeatRow> BuildSeatRows(int floor1Capacity, int floor2Capacity, Dictionary<int, TripSeat> assigned)
```

- Construye las filas del piso 1 con asientos `1..F1` (`Floor = 1`, `RowNumber` desde 1).
- Si `floor2Capacity > 0`, construye las filas del piso 2 con asientos `F1+1..F1+F2`
  (`Floor = 2`, `RowNumber` desde 1).
- El contador de asiento es global y continuo.
- `floor2Capacity <= 0` ⇒ planta única (comportamiento actual).

Endpoints:

- `GET /api/trips/{id}/seats` (cliente) y `GET /api/trips/{id}/seatmap` (staff):
  leen `HasTwoFloors`/`Floor1Capacity`/`Floor2Capacity` del viaje, los exponen en el
  DTO y llaman al nuevo `BuildSeatRows`.
- `POST /api/trips` y `PUT /api/trips/{id}`: aplican la normalización/validación de la
  sección 1. Si el switch viene activo con `TransportType != Camion`, se ignora
  (se normaliza a planta única). Errores: `"El cupo del piso 1 debe ser al menos 1."`,
  `"El cupo del piso 2 debe ser al menos 1."`.

Sin cambios en: validación de asientos de reserva (`1..Capacity`), `GetOccupiedSeatsAsync`,
`GetTakenSeatNumbersAsync`, `AssignFreeSeatsAsync`.

### 4. Admin — formulario de viaje (`AdminDashboardPage`)

- Nuevo switch **"Autobús de dos pisos"**, visible **solo** cuando el transporte
  seleccionado es `Camion`; al elegir `Camioneta` se oculta y se fuerza `false`.
- Al activarlo:
  - Se oculta "Capacidad total (asientos)".
  - Aparecen "Cupo piso 1" y "Cupo piso 2" (entradas numéricas) y un label de
    "Total: F1+F2" (solo lectura).
  - Validación: ambos `>= 1`.
- Al desactivarlo: se vuelve a mostrar "Capacidad total" y se limpian los pisos.
- `StartEdit` precarga switch y cupos; `ClearFormAsync` los reinicia (switch off,
  capacidad única).

### 5. Cliente — selección de asientos (`ClientBookingSeatsPage`)

- Si `HasTwoFloors`: se muestra un **selector "Piso 1" / "Piso 2"** (dos botones tipo
  chip, resaltando el activo) sobre la silueta del autobús.
- Se renderizan **solo las filas del piso seleccionado** (filtro por `TripSeatRow.Floor`).
- La selección de asientos (`_slots[].Seat`, número global) **se conserva** al cambiar de piso.
- El rótulo del encabezado pasa de "Planta única" a "Dos pisos".
- Etiquetas con prefijo de piso en el resumen de asientos elegidos: `P1 #5`, `P2 #25`.
- Sin dos pisos: comportamiento actual (una sola planta, sin selector).

`ClientTripDetailPage` (opcional, incluido): en el resumen de capacidad, si
`HasTwoFloors`, mostrar "Piso 1: F1 · Piso 2: F2".

### 6. Admin — mapa de asientos (`AdminTripSeatsPage`)

- Mismo **selector de piso** cuando `HasTwoFloors`.
- Resumen por piso y lista de ocupados con prefijo de piso: `Piso 1 · Asiento 12`.
- Sin dos pisos: comportamiento actual.

### 7. Compatibilidad

- Viajes existentes y nuevos sin el switch: idénticos a hoy.
- `TripPassenger.SeatNumber` sigue siendo un `int` global; no cambia el contrato de
  reservas, QR ni check-in. El piso es solo una agrupación de presentación.

## Casos límite

- Switch activo con `TransportType == Camioneta` ⇒ se normaliza a planta única
  (server-side, defensivo) y el formulario no permite activarlo.
- `F1` o `F2` en 0/negativo ⇒ error de validación.
- Cambiar de dos pisos a una planta: `Capacity` vuelve a capturarse manualmente.
- Hotel + dos pisos: `Capacity = F1+F2`; `BookableCapacity` sigue siendo
  `min(Capacity, HotelCapacity)`.
- Piso 2 con 0 asientos no debe existir: si `HasTwoFloors`, ambos pisos `>= 1`.

## Fuera de alcance

- Numeración por piso (prefijos tipo `P1-5`) o cambios a `SeatNumber`.
- Más de dos pisos o layouts por piso.
- Pasillo/asientos por piso configurables (se mantiene 2 + pasillo + 2).

## Verificación

- Build: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj` y
  `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`.
- Prueba manual:
  1. Crear viaje `Camion` con "Autobús de dos pisos", F1=20, F2=15 → capacidad total 35.
  2. Cliente: mapa con selector Piso 1/Piso 2; seleccionar asientos en ambos pisos;
     confirmar reserva; validar ocupación correcta por asiento global.
  3. Admin: mapa de asientos con selector; verificar ocupados con prefijo de piso.
  4. Verificar que `Camioneta` no permite dos pisos.
  5. Verificar hotel + dos pisos (tope por hotel).
  6. Viaje de una planta: sin selector, comportamiento actual.

## Archivos afectados (estimado)

- `src/TravelAgency.Shared/Models/Trip.cs`
- `src/TravelAgency.Shared/Models/TripSeatMap.cs`
- `src/TravelAgency.Shared/Models/TripSeatAvailability.cs`
- `src/TravelAgency.Api/Data/AppDbContext.cs`
- `src/TravelAgency.Api/Program.cs`
- `src/TravelAgency.App/Modules/Admin/Views/AdminDashboardPage.xaml(.cs)`
- `src/TravelAgency.App/Modules/Admin/Views/AdminTripSeatsPage.xaml(.cs)`
- `src/TravelAgency.App/Modules/Client/Views/ClientBookingSeatsPage.xaml(.cs)`
- `src/TravelAgency.App/Modules/Client/Views/ClientTripDetailPage.xaml(.cs)` (opcional)
