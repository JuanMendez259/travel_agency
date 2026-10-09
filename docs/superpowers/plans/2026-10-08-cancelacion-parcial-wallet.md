# Cancelación parcial con wallet (Fase 1) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir cancelar un boleto (acompañante) dentro de una reserva, liberar su asiento y acreditar el reembolso a un saldo (wallet) del cliente; y que la cancelación global también acredite a la wallet (con motivo obligatorio).

**Architecture:** El reembolso se calcula con `BookingRefundPolicy` (prorrata por la parte abonada del boleto) y se registra en `BookingRefund` + se acredita en `WalletTransaction` (saldo = suma). No se devuelve dinero ni se marcan los pagos como `Refunded`. La disponibilidad se deriva de `NumberOfSeats`, así que la cancelación parcial reduce `NumberOfSeats` y elimina el `TripPassenger` para liberar el asiento.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API + EF Core (Supabase/Postgres; SQLite fallback), .NET MAUI (net10.0-maccatalyst). Sin proyecto de tests.

**Spec:** `docs/superpowers/specs/2026-10-08-cancelacion-parcial-wallet-design.md`

## Global Constraints

- .NET 10. App: `net10.0-maccatalyst`. API: `src/TravelAgency.Api`.
- **No existe proyecto de tests.** Verificar con `dotnet build` + comprobaciones manuales/HTTP indicadas en cada tarea. No crear proyecto de tests salvo pedido explícito.
- Textos de UI en español, estilo existente (sin acentos en copy nuevo), iconos con emoji.
- Migraciones idempotentes: toda tabla/columna nueva va en un `Ensure*` o el arranque de la API falla.
- Todo reembolso (parcial y global) va a la **wallet**; **no** se marca ningún pago como `Refunded` por este flujo.
- Motivo **obligatorio** en cancelación parcial y global.
- Cancelación parcial: solo acompañantes (`TripPassenger`); con opciones, se elige la línea (`BookingItem`).
- Política de reembolso vigente: 100% dentro del límite, 70% fuera (multa 30%) sobre la parte abonada del boleto.
- Commit solo con confirmación explícita del usuario (AGENTS.md).

## Review Focus

- Cancelar el último acompañante deja `NumberOfSeats == 1` (solo titular); cancelar cuando `NumberOfSeats` llegaría a 0 → la reserva pasa a `Cancelled`.
- Viaje con opciones: cancelar sin `BookingItemId`, o con uno de otra reserva → `400`; con la línea correcta → se decrementa esa línea y el total baja por el precio de esa entrada.
- Reembolso fuera del límite de días → 70% de la parte abonada del boleto (multa 30%), nunca negativo.
- Cancelación global: los pagos quedan `Completed` (no `Refunded`), el saldo sube y la reserva muestra "Saldo a favor", no "Reembolsado".
- `paid == 0`: `refund = 0`, se libera el asiento y no se genera saldo negativo; no se puede cancelar un `PassengerId` de otra reserva.

---

### Task 1: Modelos compartidos + helpers de reembolso

**Files:**
- Create: `src/TravelAgency.Shared/Models/BookingRefund.cs`
- Create: `src/TravelAgency.Shared/Models/WalletTransaction.cs`
- Modify: `src/TravelAgency.Shared/Models/Payment.cs`
- Modify: `src/TravelAgency.Shared/Models/BookingRefundPolicy.cs`

**Interfaces:**
- Produces: `BookingRefund` (Id, BookingId, UserId, Amount, PenaltyAmount, Reason, PassengerName?, SeatNumber?, BookingItemId?, CreatedAt); `WalletTransaction` (Id, UserId, Amount, Type, BookingId?, RefundId?, Note?, CreatedAt); `enum WalletType { CancellationCredit=0, BookingPayment=1, PayoutRequest=2, Adjustment=3 }`; `PaymentMethod.Wallet=6`; `BookingRefundPolicy.NetUnitPrice(Booking, decimal) -> decimal`; `BookingRefundPolicy.PaidShare(Booking, decimal paid, decimal netUnit) -> decimal`.

- [ ] **Step 1: Crear `BookingRefund`**

En `BookingRefund.cs` (namespace `TravelAgency.Shared.Models`): propiedades `int Id`, `int BookingId`, `int UserId`, `decimal Amount`, `decimal PenaltyAmount`, `string Reason` (no nullable), `string? PassengerName`, `int? SeatNumber`, `int? BookingItemId`, `DateTime CreatedAt`.

- [ ] **Step 2: Crear `WalletTransaction` y `WalletType`**

En `WalletTransaction.cs`: `int Id`, `int UserId`, `decimal Amount`, `WalletType Type`, `int? BookingId`, `int? RefundId`, `string? Note`, `DateTime CreatedAt`; y `enum WalletType { CancellationCredit = 0, BookingPayment = 1, PayoutRequest = 2, Adjustment = 3 }`.

- [ ] **Step 3: `PaymentMethod.Wallet`**

En `Payment.cs`, agregar `Wallet = 6` al enum `PaymentMethod`.

- [ ] **Step 4: Helpers en `BookingRefundPolicy`**

Agregar:
- `public static decimal NetUnitPrice(Booking booking, decimal grossUnitPrice)` — `grossBefore = booking.TotalAmount + booking.DiscountAmount`; si `grossBefore > 0` → `Math.Round(grossUnitPrice * booking.TotalAmount / grossBefore, 2)`; si no → `grossUnitPrice`.
- `public static decimal PaidShare(Booking booking, decimal paid, decimal netUnit)` — si `booking.TotalAmount > 0` → `Math.Round(paid * netUnit / booking.TotalAmount, 2)`; si no → `booking.NumberOfSeats > 0 ? paid / booking.NumberOfSeats : paid`.

- [ ] **Step 5: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 6: Commit**

```bash
git add src/TravelAgency.Shared/Models/BookingRefund.cs src/TravelAgency.Shared/Models/WalletTransaction.cs src/TravelAgency.Shared/Models/Payment.cs src/TravelAgency.Shared/Models/BookingRefundPolicy.cs
git commit -m "wallet: modelos de reembolso/saldo y helpers de prorrata"
```

---

### Task 2: `AppDbContext` + migraciones idempotentes

**Files:**
- Modify: `src/TravelAgency.Api/Data/AppDbContext.cs`
- Modify: `src/TravelAgency.Api/Program.cs`

**Interfaces:**
- Consumes: `BookingRefund`, `WalletTransaction`.
- Produces: `AppDbContext.BookingRefunds`, `AppDbContext.WalletTransactions`; `EnsureBookingRefundsTableAsync(AppDbContext, string provider)`; `EnsureWalletTransactionsTableAsync(AppDbContext, string provider)`.

- [ ] **Step 1: DbSets y configuración**

En `AppDbContext`: `public DbSet<BookingRefund> BookingRefunds => Set<BookingRefund>();` y `public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();`. En `OnModelCreating`: `ToTable("BookingRefunds")` / `ToTable("WalletTransactions")`; `Reason` requerido máx 500; `Note` máx 300; FK a `Users` con `OnDelete(DeleteBehavior.Restrict)`; índices por `BookingId` y `UserId`.

- [ ] **Step 2: `EnsureBookingRefundsTableAsync` y `EnsureWalletTransactionsTableAsync`**

En `Program.cs` (patrón `Ensure*` con `ColumnExistsAsync`/`TryExecAsync`, ramas sqlite y postgres):
- `BookingRefunds`: `Id` PK identity/autoincrement, `BookingId` int, `UserId` int, `Amount` numeric/text, `PenaltyAmount`, `Reason` varchar(500), `PassengerName` varchar(120) null, `SeatNumber` int null, `BookingItemId` int null, `CreatedAt` timestamp/text; FKs a `Bookings`/`Users` (Restrict); índices `IX_BookingRefunds_BookingId`, `IX_BookingRefunds_UserId`.
- `WalletTransactions`: `Id` PK, `UserId` int, `Amount` numeric/text, `Type` int, `BookingId` int null, `RefundId` int null, `Note` varchar(300) null, `CreatedAt`; FK a `Users` (Restrict); índice `IX_WalletTransactions_UserId`.

- [ ] **Step 3: Llamarlas en el arranque**

En el bloque de migraciones, junto a `EnsureBookingDiscountColumnsAsync`, agregar las dos llamadas (antes de cualquier consulta que materialice estas tablas).

- [ ] **Step 4: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 5: Verificar tablas (local o tras deploy)**

Con `psql` (o arranque local): `SELECT table_name FROM information_schema.tables WHERE table_name IN ('BookingRefunds','WalletTransactions');`
Expected: 2 filas.

- [ ] **Step 6: Commit**

```bash
git add src/TravelAgency.Api/Data/AppDbContext.cs src/TravelAgency.Api/Program.cs
git commit -m "wallet: tablas BookingRefunds y WalletTransactions con migracion idempotente"
```

---

### Task 3: Endpoint de cancelación parcial

**Files:**
- Modify: `src/TravelAgency.Api/Program.cs`

**Interfaces:**
- Consumes: `BookingRefundPolicy.NetUnitPrice/PaidShare`, `BookingRefund`, `WalletTransaction`, `RecomputeAvailabilityAsync`, `SendToStaffAsync`.
- Produces: `POST /api/bookings/{id}/cancel-ticket`; records `CancelTicketRequest(int PassengerId, int? BookingItemId, string? Reason)`, `CancelTicketResult(Booking Booking, decimal RefundAmount, decimal PenaltyAmount, bool WithinPolicy, decimal WalletBalance)`.

- [ ] **Step 1: Records**

Cerca de `CancelBookingResult`, agregar `record CancelTicketRequest(int PassengerId, int? BookingItemId, string? Reason);` y `record CancelTicketResult(Booking Booking, decimal RefundAmount, decimal PenaltyAmount, bool WithinPolicy, decimal WalletBalance);`.

- [ ] **Step 2: Endpoint `POST /api/bookings/{id}/cancel-ticket`**

Requiere autorización (owner o `Admin`). Cargar booking con `Trip`, `User`, `Payments`, `Passengers`, `Items`. Validar: no cancelada; `!trip.DepartureCompleted`; `!trip.Finalized`; `trip.StartDate.Date > today`; `Reason` no vacío; `PassengerId` pertenece a la reserva; si `trip.HasOptions` y hay >1 línea → `BookingItemId` obligatorio y perteneciente a la reserva. Luego aplicar los efectos de la spec (sección "Cancelación parcial"), incluyendo: `paid = booking.PaidTotal()`; `grossUnitPrice` (línea por `IsChild` o `TotalAmount/NumberOfSeats`); `netUnit = NetUnitPrice`; `paidShare = PaidShare`; `refund/penalty` según política; decrementar la línea (eliminarla si queda en 0); eliminar el pasajero; `NumberOfSeats--`; `DiscountAmount`/`TotalAmount` ajustados proporcionalmente; crear `BookingRefund` + `WalletTransaction(CancellationCredit, +refund)`; si `NumberOfSeats == 0` → `Cancelled` + `CancelledAt` + `CancelledByUserId`; `RecomputeAvailabilityAsync`; notificar cliente y staff. Responder `CancelTicketResult` con el saldo resultante (`Sum(WalletTransaction.Amount)` del usuario).

- [ ] **Step 3: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 4: Verificar (local/HTTP)**

- Reserva de 3 asientos sin opciones, dentro del límite: cancelar 1 acompañante → `NumberOfSeats=2`, total reducido, `refund` = 100% de la parte abonada del boleto, saldo sube, asiento liberado.
- Mismo caso fuera del límite → `refund` = 70% (multa 30%).
- Viaje con opciones: cancelar sin `BookingItemId` → `400`; con línea válida → esa línea baja 1 y el total baja por su precio.
- `PassengerId` de otra reserva → `400`; `Reason` vacío → `400`.
- `paid == 0` → `refund = 0`, asiento liberado, sin saldo negativo.

- [ ] **Step 5: Commit**

```bash
git add src/TravelAgency.Api/Program.cs
git commit -m "wallet: endpoint de cancelacion parcial de boleto"
```

---

### Task 4: Cancelación global a wallet + `GET /api/users/me/wallet`

**Files:**
- Modify: `src/TravelAgency.Api/Program.cs`

**Interfaces:**
- Consumes: `BookingRefund`, `WalletTransaction`, `BookingRefundPolicy`.
- Produces: `POST /api/bookings/{id}/cancel` con body `CancelBookingRequest(string? Reason)`; `GET /api/users/me/wallet`; record `CancelBookingRequest(string? Reason)`, `WalletSummary(decimal Balance, List<WalletTransaction> Transactions)`.

- [ ] **Step 1: Record del body y `WalletSummary`**

Agregar `record CancelBookingRequest(string? Reason);` y `record WalletSummary(decimal Balance, List<WalletTransaction> Transactions);`.

- [ ] **Step 2: Modificar `POST /api/bookings/{id}/cancel`**

Cambiar la firma para recibir `CancelBookingRequest request`. Validar `Reason` no vacío (`400` si falta). Mantener validaciones de estado/política. Calcular `refund = RefundFrom(paid, withinPolicy)`. **Eliminar** el bloque que marca los pagos `Refunded`; en su lugar crear `BookingRefund` (Amount=refund, PenaltyAmount=penalty, Reason, PassengerName=null, SeatNumber=null, BookingItemId=null) + `WalletTransaction(CancellationCredit, +refund)`. Mantener `Status=Cancelled`, `CancelledAt`, `CancelledByUserId`, `RecomputeAvailabilityAsync` y la notificación. Responder `CancelBookingResult` como hoy.

- [ ] **Step 3: `GET /api/users/me/wallet`**

`app.MapGet("/api/users/me/wallet", ...)`: para el usuario del token, `Balance = Sum(WalletTransaction.Amount)` y `Transactions` ordenadas desc por `CreatedAt`. `RequireAuthorization()`.

- [ ] **Step 4: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 5: Verificar (local/HTTP)**

- Cancelar global con motivo → reserva `Cancelled`, pagos siguen `Completed` (no `Refunded`), saldo sube por el reembolso.
- Sin motivo → `400`.
- `GET /api/users/me/wallet` devuelve `Balance` y movimientos.

- [ ] **Step 6: Commit**

```bash
git add src/TravelAgency.Api/Program.cs
git commit -m "wallet: cancelacion global acredita saldo y endpoint de wallet"
```

---

### Task 5: ApiService

**Files:**
- Modify: `src/TravelAgency.App/Services/ApiService.cs`

**Interfaces:**
- Consumes: endpoints de Tasks 3–4.
- Produces: `CancelTicketAsync(int bookingId, int passengerId, int? bookingItemId, string reason) -> Task<CancelTicketResult?>`; `CancelBookingAsync(int bookingId, string reason) -> Task<CancelBookingResult?>` (firma nueva); `GetWalletAsync() -> Task<WalletSummary?>`; records privados `CancelTicketRequest`, `CancelBookingRequest`.

- [ ] **Step 1: Records privados**

Agregar `private record CancelTicketRequest(int PassengerId, int? BookingItemId, string? Reason);` y `private record CancelBookingRequest(string? Reason);`.

- [ ] **Step 2: Métodos**

- `CancelTicketAsync`: `POST /api/bookings/{bookingId}/cancel-ticket` con el body; lee `CancelTicketResult`.
- `CancelBookingAsync(int bookingId, string reason)`: cambia la firma para enviar `{ Reason = reason }` a `POST /api/bookings/{bookingId}/cancel`.
- `GetWalletAsync`: `GET /api/users/me/wallet` → `WalletSummary`.

- [ ] **Step 3: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 4: Commit**

```bash
git add src/TravelAgency.App/Services/ApiService.cs
git commit -m "wallet: ApiService de cancelacion parcial, global y saldo"
```

---

### Task 6: UI cliente (perfil + detalle de reserva)

**Files:**
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientProfilePage.xaml(.cs)`
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml(.cs)`

**Interfaces:**
- Consumes: `ApiService.CancelTicketAsync`, `CancelBookingAsync(reason)`, `GetWalletAsync`.

- [ ] **Step 1: Perfil — tarjeta "Mi saldo"**

En `ClientProfilePage.xaml`, una tarjeta `Border` (estilo de las demás) con `SaldoLabel` (monto `{0:C}`) y el título "Mi saldo". En `.cs` (`OnAppearing`), cargar `GetWalletAsync()` y setear `SaldoLabel.Text`; si falla, mostrar `$0.00 MXN`.

- [ ] **Step 2: Detalle — cancelar acompañante**

En `ClientMyBookingDetailPage`, por cada acompañante (lista de pasajeros del QR) agregar acción "Cancelar boleto": pide **motivo** (`DisplayPromptAsync`) y, si `trip.HasOptions` con >1 línea, pide la **entrada** (`DisplayActionSheet` con los `Items`); llama `CancelTicketAsync(id, passenger.Id, bookingItemId, reason)`; muestra el reembolso; recarga.

- [ ] **Step 3: Detalle — motivo en cancelación global**

En `OnCancelClicked`, pedir **motivo** (`DisplayPromptAsync`) antes de llamar `CancelBookingAsync(id, reason)`; si se cancela sin motivo, abortar.

- [ ] **Step 4: Detalle — "Saldo a favor"**

Cuando la reserva está `Cancelled`, mostrar en `BalanceLabel` "Saldo a favor: {X}" usando el saldo acreditado (el `refund` devuelto por el endpoint o el saldo del usuario), en vez del texto de reembolso por pagos `Refunded`.

- [ ] **Step 5: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 6: Verificar manualmente**

- Perfil muestra el saldo tras una cancelación.
- Detalle: cancelar un acompañante (con motivo y, si aplica, entrada) libera el asiento y sube el saldo.
- Cancelación global pide motivo y deja "Saldo a favor".

- [ ] **Step 7: Commit**

```bash
git add src/TravelAgency.App/Modules/Client/Views/ClientProfilePage.xaml src/TravelAgency.App/Modules/Client/Views/ClientProfilePage.xaml.cs src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml.cs
git commit -m "cliente: saldo en perfil y cancelacion parcial de boleto"
```

---

### Task 7: UI admin (cancelar acompañante)

**Files:**
- Modify: `src/TravelAgency.App/Modules/Admin/Views/AdminBookingDetailPage.xaml(.cs)`

**Interfaces:**
- Consumes: `ApiService.CancelTicketAsync`.

- [ ] **Step 1: Listar acompañantes con acción**

En `AdminBookingDetailPage`, mostrar la lista de `_booking.Passengers` (nombre + asiento) con un botón "Cancelar boleto" por acompañante que pide **motivo** y, si el viaje tiene opciones con >1 línea, la **entrada**; llama `CancelTicketAsync` y recarga.

- [ ] **Step 2: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 3: Verificar manualmente**

- El admin puede cancelar un boleto de una reserva y el asiento se libera.

- [ ] **Step 4: Commit**

```bash
git add src/TravelAgency.App/Modules/Admin/Views/AdminBookingDetailPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminBookingDetailPage.xaml.cs
git commit -m "admin: cancelar boleto desde el detalle de reserva"
```

---

## Verificación final (todo el plan)

- `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj`
- `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
- Recorrido manual completo: reserva de 3 asientos → cancelar 1 acompañante (dentro y fuera del límite) → verificar asiento libre, saldo y avisos → viaje con opciones (elegir línea) → cancelar hasta 0 → cancelación global con motivo → `GET /api/users/me/wallet`.
