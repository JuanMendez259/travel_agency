# Wallet Fase 2 (usar saldo y pedir reembolso) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir usar el saldo (wallet) en una reserva nueva o existente y "Pedir Reembolso" (payout con saldo congelado, resuelto por el admin).

**Architecture:** El saldo total = suma de `WalletTransaction.Amount`; `Held` = suma de `PayoutRequest` pendientes; disponible = total − Held. Pedir reembolso crea un `PayoutRequest(Pending)` (congela, sin transacción); el admin lo marca pagado (crea `WalletTransaction(PayoutRequest, −monto)` → el saldo total baja) o lo rechaza (libera el congelado). Usar saldo crea `Payment(Method=Wallet)` + `WalletTransaction(BookingPayment, −monto)`.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API + EF Core (Supabase/Postgres; SQLite fallback), .NET MAUI (net10.0-maccatalyst). Sin proyecto de tests.

**Spec:** `docs/superpowers/specs/2026-10-09-wallet-fase2-design.md`

## Global Constraints

- .NET 10. App: `net10.0-maccatalyst`. API: `src/TravelAgency.Api`.
- **No existe proyecto de tests.** Verificar con `dotnet build` + comprobaciones manuales/HTTP. No crear proyecto de tests.
- Textos de UI en español, sin acentos en copy nuevo; iconos con emoji.
- Migraciones idempotentes: toda tabla/columna nueva va en un `Ensure*` o el arranque falla.
- Saldo: **total** = suma de `WalletTransaction.Amount`; **Held** = suma de `PayoutRequest` `Pending`; **disponible** = total − Held.
- Pedir reembolso: **congela** (crea `PayoutRequest(Pending)`, sin transacción). Marcar pagado: **descuenta** (`WalletTransaction(PayoutRequest, −monto)`). Rechazar: libera (sin transacción).
- `PaymentMethod.Wallet` (ya existe, =6) para pagos con saldo.
- WhatsApp de la agencia: `https://wa.me/5214444579256?text=...` (mismo link del botón "Cotizar").
- Todo en un `SaveChanges` por operación (transacción implícita de EF); **no** usar transacción explícita (choca con `AuditLogInterceptor`).
- Commit solo con confirmación explícita del usuario (AGENTS.md).

## Review Focus

- Payout `Amount > disponible` (total − Held) → `400`; un monto congelado no puede gastarse ni pedirse dos veces.
- Marcar pagado descuenta el saldo total por el monto (`WalletTransaction` PayoutRequest); rechazar lo libera **sin** transacción (el total no cambia).
- `WalletAmount` en checkout `> min(disponible, TotalAmount)` → `400`; con saldo que cubre el total → reserva `Confirmed`.
- `pay-with-wallet` `> min(disponible, saldo pendiente)` → `400`; no permite pagar una reserva cancelada.
- Doble gasto: validar siempre contra la suma de transacciones pendientes de guardar en el mismo `SaveChanges` (sin transacción explícita).

---

### Task 1: Modelos compartidos (payout + wallet)

**Files:**
- Create: `src/TravelAgency.Shared/Models/PayoutRequest.cs`
- Modify: `src/TravelAgency.Shared/Models/WalletTransaction.cs`
- Modify: `src/TravelAgency.Shared/Models/AuthModels.cs` (o el archivo donde vive `WalletSummary`)

**Interfaces:**
- Produces: `PayoutRequest` (Id, UserId, Amount, Status, Note?, CreatedAt, ResolvedAt?, ResolvedByUserId?); `enum PayoutStatus { Pending=0, Paid=1, Rejected=2 }`; `WalletTransaction.PayoutRequestId` (`int?`); `WalletSummary(decimal Balance, decimal Held, decimal Available, List<WalletTransaction> Transactions)`.

- [ ] **Step 1: Crear `PayoutRequest`**

En `PayoutRequest.cs` (namespace `TravelAgency.Shared.Models`): `int Id`, `int UserId`, `decimal Amount`, `PayoutStatus Status`, `string? Note`, `DateTime CreatedAt`, `DateTime? ResolvedAt`, `int? ResolvedByUserId`; y `enum PayoutStatus { Pending = 0, Paid = 1, Rejected = 2 }`.

- [ ] **Step 2: `WalletTransaction.PayoutRequestId`**

Agregar a `WalletTransaction`: `public int? PayoutRequestId { get; set; }`.

- [ ] **Step 3: Extender `WalletSummary`**

Cambiar a `public record WalletSummary(decimal Balance, decimal Held, decimal Available, List<WalletTransaction> Transactions);`.

- [ ] **Step 4: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores` (los usos de `WalletSummary` en la API se ajustan en Task 3).

- [ ] **Step 5: Commit**

```bash
git add src/TravelAgency.Shared/Models/PayoutRequest.cs src/TravelAgency.Shared/Models/WalletTransaction.cs src/TravelAgency.Shared/Models/AuthModels.cs
git commit -m "wallet: modelo PayoutRequest y saldo con Held/Available"
```

---

### Task 2: `AppDbContext` + migraciones

**Files:**
- Modify: `src/TravelAgency.Api/Data/AppDbContext.cs`
- Modify: `src/TravelAgency.Api/Program.cs`

**Interfaces:**
- Consumes: `PayoutRequest`, `PayoutStatus`, `WalletTransaction.PayoutRequestId`.
- Produces: `AppDbContext.PayoutRequests`; `EnsurePayoutRequestsTableAsync(AppDbContext, string)`; `EnsureWalletTransactionPayoutColumnAsync(AppDbContext, string)`.

- [ ] **Step 1: DbSet + config**

`public DbSet<PayoutRequest> PayoutRequests => Set<PayoutRequest>();`. En `OnModelCreating`: `ToTable("PayoutRequests")`; `Amount` precision (18,2); `Note` máx 300; `HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict)`; índices por `UserId` y `Status`. En la config de `WalletTransaction`: `HasOne<WalletTransaction>().WithMany()` no aplica; agregar `entity.HasIndex(t => t.PayoutRequestId)` (opcional) — no se requiere FK.

- [ ] **Step 2: `EnsurePayoutRequestsTableAsync`**

En `Program.cs` (patrón `Ensure*`, ramas sqlite/postgres): tabla `PayoutRequests` (`Id` PK identity/autoincrement, `UserId` int, `Amount` numeric/text, `Status` int, `Note` varchar(300) null, `CreatedAt` timestamp/text, `ResolvedAt` timestamp/text null, `ResolvedByUserId` int null), FK a `Users` (Restrict), índices `IX_PayoutRequests_UserId` y `IX_PayoutRequests_Status`.

- [ ] **Step 3: `EnsureWalletTransactionPayoutColumnAsync`**

Agregar `WalletTransactions.PayoutRequestId` (sqlite `INTEGER NULL`; postgres `integer NULL`) con `ColumnExistsAsync`.

- [ ] **Step 4: Llamarlas en el arranque**

Junto a `EnsureWalletTransactionsTableAsync`, antes de consultas que materialicen estas tablas.

- [ ] **Step 5: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 6: Commit**

```bash
git add src/TravelAgency.Api/Data/AppDbContext.cs src/TravelAgency.Api/Program.cs
git commit -m "wallet: tabla PayoutRequests y columna PayoutRequestId"
```

---

### Task 3: Endpoints de payout + `GET /api/users/me/wallet`

**Files:**
- Modify: `src/TravelAgency.Api/Program.cs`

**Interfaces:**
- Consumes: `PayoutRequest`, `PayoutStatus`, `WalletTransaction`, `SendToStaffAsync`.
- Produces: `POST /api/users/me/payout-requests`; `GET /api/users/me/payout-requests`; `GET /api/payout-requests`; `POST /api/payout-requests/{id}/resolve`; `WalletSummary(Balance, Held, Available, Transactions)` en `GET /api/users/me/wallet`; records `CreatePayoutRequest(decimal Amount, string? Note)`, `ResolvePayoutRequest(bool Approve)`.

- [ ] **Step 1: Helper de saldo**

Agregar un helper local `static async Task<(decimal Balance, decimal Held)> WalletTotalsAsync(AppDbContext db, int userId)`:
- `Balance = Sum(WalletTransaction.Amount where UserId == userId)`.
- `Held = Sum(PayoutRequest.Amount where UserId == userId && Status == Pending)`.

- [ ] **Step 2: `GET /api/users/me/wallet` con Held/Available**

Reemplazar el cálculo actual: `Balance`, `Held`, `Available = Balance - Held`, `Transactions` (desc por `CreatedAt`).

- [ ] **Step 3: `POST /api/users/me/payout-requests`**

Body `CreatePayoutRequest(Amount, Note?)`. Validar `Amount > 0` y `Amount <= Available` (usar el helper; `400` con mensaje si excede). Crear `PayoutRequest(Pending, Amount, Note, CreatedAt=UtcNow)`. `SaveChanges`. `SendToStaffAsync(db, $"{user} solicitó un reembolso de {Amount:C} de su saldo.")`. Responder `{ PayoutRequest, Balance, Held, Available }`.

- [ ] **Step 4: `GET /api/users/me/payout-requests` y `GET /api/payout-requests?status=`**

El primero: las del usuario del token, desc. El segundo: `RequireAuthorization("StaffOnly")`, todas (incluye `UserId`); filtro opcional por `status` (`PayoutStatus`).

- [ ] **Step 5: `POST /api/payout-requests/{id}/resolve`**

`RequireAuthorization("AdminOnly")`, body `ResolvePayoutRequest(Approve)`. Cargar el payout; si `Status != Pending` → `400` ("ya resuelto"). Si `Approve`: `Status=Paid`, `ResolvedAt=UtcNow`, `ResolvedByUserId`, **y** crear `WalletTransaction(PayoutRequest, −Amount, PayoutRequestId=id, BookingId=null, Note="Payout")`. Si no: `Status=Rejected`, `ResolvedAt`, `ResolvedByUserId` (sin transacción). `SaveChanges`. Notificar al cliente (aprobado: "Tu reembolso de {Amount:C} fue pagado"; rechazado: "Tu solicitud de reembolso fue rechazada; el saldo sigue disponible"). Responder el payout.

- [ ] **Step 6: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 7: Verificar (local/HTTP, sin tocar producción)**

- Pedir reembolso ≤ disponible → `Available` baja, `Balance` igual.
- Pedir > disponible → `400`.
- Resolver `Approve=true` → `Balance` baja por el monto, `Held` baja.
- Resolver `Approve=false` → `Held` baja, `Balance` igual.
- Resolver dos veces → `400`.

- [ ] **Step 8: Commit**

```bash
git add src/TravelAgency.Api/Program.cs
git commit -m "wallet: endpoints de payout y saldo con Held/Available"
```

---

### Task 4: Usar saldo en checkout y en reserva existente

**Files:**
- Modify: `src/TravelAgency.Api/Program.cs`

**Interfaces:**
- Consumes: helper de saldo (Task 3), `WalletTransaction`, `Payment`, `PaymentMethod.Wallet`.
- Produces: `WalletAmount` en `CreateBookingRequest`; `POST /api/bookings/{id}/pay-with-wallet`; record `PayWithWalletRequest(decimal Amount)`.

- [ ] **Step 1: `WalletAmount` en `CreateBookingRequest`**

Agregar `decimal? WalletAmount = null` al record `CreateBookingRequest`.

- [ ] **Step 2: Aplicar saldo en `POST /api/bookings`**

Antes de guardar: si `WalletAmount is > 0`: validar `WalletAmount <= min(Available, TotalAmount)` (`400` si excede). Tras crear la reserva, agregar `Payment(Method=Wallet, Amount=WalletAmount, Status=Completed, PaymentDate=UtcNow)` + `WalletTransaction(BookingPayment, −WalletAmount, BookingId=booking.Id)`. Si `WalletAmount >= TotalAmount` → `booking.Status = Confirmed`. Todo en el/los `SaveChanges` de creación existentes (un solo save para la parte de saldo).

- [ ] **Step 3: `POST /api/bookings/{id}/pay-with-wallet`**

Owner o Admin. Cargar la reserva con `Trip`/`User`/`Payments`. Validar: no `Cancelled`; `Amount > 0`; `Amount <= min(Available, saldo pendiente)` (pendiente = `TotalAmount - PaidTotal()`). Crear `Payment(Method=Wallet, Amount, Status=Completed)` + `WalletTransaction(BookingPayment, −Amount, BookingId)`. Si liquida → `Status=Confirmed`. `SaveChanges`. Notificar al cliente. Responder `CancelBookingResult`-like: usar `{ booking, paid, remaining }` o `Booking`.

- [ ] **Step 4: Verificar build**

Run: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj -v q`
Expected: `0 Errores`.

- [ ] **Step 5: Verificar (local/HTTP, sin tocar producción)**

- Checkout con `WalletAmount` parcial → total baja, saldo pendiente > 0.
- Checkout con `WalletAmount` = total → reserva `Confirmed`.
- `WalletAmount > disponible` o `> total` → `400`.
- `pay-with-wallet` parcial/total; exceder pendiente → `400`.

- [ ] **Step 6: Commit**

```bash
git add src/TravelAgency.Api/Program.cs
git commit -m "wallet: pagar reserva (nueva y existente) con saldo"
```

---

### Task 5: ApiService

**Files:**
- Modify: `src/TravelAgency.App/Services/ApiService.cs`

**Interfaces:**
- Consumes: endpoints de Tasks 3–4; `WalletSummary` (nuevo).
- Produces: `CreatePayoutRequestAsync(decimal amount, string? note)`; `GetMyPayoutRequestsAsync()`; `GetPayoutRequestsAsync(PayoutStatus? status)`; `ResolvePayoutRequestAsync(int id, bool approve)`; `PayWithWalletAsync(int bookingId, decimal amount)`; `CreateBookingWithSeatsAsync`/`CreateBookingAsync` con `decimal? walletAmount = null`.

- [ ] **Step 1: Métodos y records**

Implementar los métodos con el patrón existente (`PostAsJsonAsync`, `EnsureSuccessStatusCode`/`EnsureSuccessAsync`, records privados para los bodies `CreatePayoutRequest`/`ResolvePayoutRequest`/`PayWithWalletRequest`). `GetWalletAsync` ya devuelve `WalletSummary` (nuevo record).

- [ ] **Step 2: `walletAmount` en la creación de reserva**

Agregar `decimal? walletAmount = null` a `CreateBookingWithSeatsAsync` y `CreateBookingAsync`, y al record privado `CreateBookingRequest`.

- [ ] **Step 3: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 4: Commit**

```bash
git add src/TravelAgency.App/Services/ApiService.cs
git commit -m "wallet: ApiService de payout y pago con saldo"
```

---

### Task 6: UI cliente (perfil, checkout, detalle)

**Files:**
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientProfilePage.xaml(.cs)`
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientBookingSeatsPage.xaml(.cs)`
- Modify: `src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml(.cs)`

**Interfaces:**
- Consumes: `ApiService.CreatePayoutRequestAsync`, `GetWalletAsync`, `PayWithWalletAsync`, `CreateBookingWithSeatsAsync(..., walletAmount)`.

- [ ] **Step 1: Perfil — "Pedir Reembolso"**

Bajo la tarjeta "Mi saldo" (mostrar `Available` y, si `Held>0`, "X en proceso"), agregar el label **"Pedir Reembolso"**. Al pulsar: `DisplayPromptAsync` del monto (default = disponible, tope = disponible); `CreatePayoutRequestAsync(amount, null)`; mostrar el nuevo saldo; abrir WhatsApp `https://wa.me/5214444579256?text={Uri.EscapeDataString(mensaje)}` con nombre, correo, teléfono, saldo y monto.

- [ ] **Step 2: Checkout — "Usar mi saldo"**

En `ClientBookingSeatsPage`, agregar un control (switch + input de monto, tope = `min(disponible, total)`) que muestre el saldo aplicado y ajuste el total; pasar `walletAmount` a `CreateBookingWithSeatsAsync`.

- [ ] **Step 3: Detalle — "Realizar pago"**

En `ClientMyBookingDetailPage`, si hay saldo pendiente y la reserva no está cancelada, botón **"Realizar pago"** → vista/modal con el saldo disponible + input de monto (tope = `min(disponible, pendiente)`) + "Pagar con saldo" → `PayWithWalletAsync` → recargar.

- [ ] **Step 4: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 5: Verificar manualmente**

- Perfil: pedir reembolso congela el disponible y abre WhatsApp.
- Checkout: aplicar saldo parcial/total.
- Detalle: pagar con saldo.

- [ ] **Step 6: Commit**

```bash
git add src/TravelAgency.App/Modules/Client/Views/ClientProfilePage.xaml src/TravelAgency.App/Modules/Client/Views/ClientProfilePage.xaml.cs src/TravelAgency.App/Modules/Client/Views/ClientBookingSeatsPage.xaml src/TravelAgency.App/Modules/Client/Views/ClientBookingSeatsPage.xaml.cs src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml.cs
git commit -m "cliente: usar saldo y pedir reembolso"
```

---

### Task 7: UI admin de payouts

**Files:**
- Create: `src/TravelAgency.App/Modules/Admin/Views/AdminPayoutsPage.xaml(.cs)`
- Modify: `src/TravelAgency.App/AppShell.xaml`, `src/TravelAgency.App/AppShell.xaml.cs`, `src/TravelAgency.App/App.xaml.cs`, `src/TravelAgency.App/MauiProgram.cs`

**Interfaces:**
- Consumes: `ApiService.GetPayoutRequestsAsync`, `ResolvePayoutRequestAsync`.

- [ ] **Step 1: Página `AdminPayoutsPage`**

Lista de `PayoutRequest` (usuario, monto, fecha, estado) con acciones **"Marcar como pagado"** / **"Rechazar"** por las `Pending` (con confirmación) → `ResolvePayoutRequestAsync(id, true/false)` → recargar.

- [ ] **Step 2: Registrar ruta + pestaña admin**

`Routing.RegisterRoute("payouts", typeof(AdminPayoutsPage))` en `AppShell.xaml.cs`; `<ShellContent Title="Reembolsos" ContentTemplate="{DataTemplate admin:AdminPayoutsPage}" Route="payouts" />` en `AppShell.xaml`; incluir `"payouts"` en la lista de tabs visibles para Admin en `App.xaml.cs`; `AddTransient<AdminPayoutsPage>()` en `MauiProgram`.

- [ ] **Step 3: Verificar build**

Run: `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst -v q`
Expected: `0 Errores`.

- [ ] **Step 4: Verificar manualmente**

- Admin ve los payouts pendientes y puede marcarlos pagados/rechazados; el saldo del cliente refleja el resultado.

- [ ] **Step 5: Commit**

```bash
git add src/TravelAgency.App/Modules/Admin/Views/AdminPayoutsPage.xaml src/TravelAgency.App/Modules/Admin/Views/AdminPayoutsPage.xaml.cs src/TravelAgency.App/AppShell.xaml src/TravelAgency.App/AppShell.xaml.cs src/TravelAgency.App/App.xaml.cs src/TravelAgency.App/MauiProgram.cs
git commit -m "admin: pagina de reembolsos (payouts)"
```

---

## Verificación final (todo el plan)

- `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj`
- `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`
- Recorrido manual: pedir reembolso (congela) → admin paga (descuenta) / rechaza (libera) → usar saldo en checkout y en reserva existente → WhatsApp.
