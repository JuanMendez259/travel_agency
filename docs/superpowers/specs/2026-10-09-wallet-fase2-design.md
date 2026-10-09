# Diseño — Wallet Fase 2 (usar saldo y pedir reembolso)

Fecha: 2026-10-09
Estado: propuesto (pendiente de revisión)
Alcance: **Fase 2** — consumo del saldo (reserva nueva y existente) y "Pedir Reembolso" con payout administrado. Construye sobre la Fase 1 (`docs/superpowers/specs/2026-10-08-cancelacion-parcial-wallet-design.md`).

## Contexto y objetivo

La Fase 1 acredita los reembolsos a un saldo (wallet). Fase 2 permite **usar ese saldo** (pagar una reserva nueva o existente, con monto elegible) y **pedir un reembolso real** (transferencia), que congela el saldo hasta que el admin lo marque como pagado.

## Decisiones (confirmadas)

1. "Pedir Reembolso" **congela** el monto: el saldo baja al instante (`WalletTransaction` negativo) y queda **pendiente** hasta que el admin lo marque pagado.
2. Se crea una **UI de admin** para ver y resolver los payouts (marcar pagado / rechazar).
3. En "Pedir Reembolso" el cliente **elige el monto** (tope = saldo disponible).
4. En el checkout hay un control **"Usar mi saldo"** que el cliente activa y elige el monto (tope = `min(saldo, total a pagar)`).

## Modelo de datos

### `PayoutRequest`

```
Id, UserId, Amount, Status (PayoutStatus), Note?, CreatedAt,
ResolvedAt?, ResolvedByUserId?
```

`enum PayoutStatus { Pending = 0, Paid = 1, Rejected = 2 }`.

### `WalletTransaction`

- Nuevo `int? PayoutRequestId` (liga la transacción con la solicitud; sin serializar al cliente).
- `WalletType` ya existe: `CancellationCredit=0`, `BookingPayment=1`, `PayoutRequest=2`, `Adjustment=3`.

### Semántica del saldo (congelado)

- **Saldo total = suma de `WalletTransaction.Amount`** (positivos suman, negativos consumen).
- **Congelado (`Held`) = suma de `PayoutRequest` en estado `Pending`**.
- **Saldo disponible = Saldo total − Held** (lo que el cliente puede gastar).
- Al crear un `PayoutRequest`: se crea `PayoutRequest(Pending)` (sin transacción). El monto queda **congelado**: el disponible baja, el saldo total **no** cambia.
- **Marcar pagado**: `Status=Paid` (+ `ResolvedAt`/`ResolvedByUserId`) **+ `WalletTransaction(PayoutRequest, −amount, PayoutRequestId)`** → el saldo total baja por el monto (`nuevo saldo = saldo actual − monto`) y el congelado se libera.
- **Rechazar**: `Status=Rejected` (sin transacción); el monto deja de estar congelado y vuelve al disponible.

`GET /api/users/me/wallet` expone `Balance` (total), `Held` (congelado) y `Available` (disponible). El cliente muestra el **disponible** (y opcionalmente "X en proceso").

### Migración

- `EnsurePayoutRequestsTableAsync`: crea `PayoutRequests` (sqlite/postgres) con FK a `Users` (Restrict) e índices por `UserId` y `Status`.
- `EnsureWalletTransactionPayoutColumnAsync`: agrega `WalletTransactions.PayoutRequestId` (int null).
- `AppDbContext`: `DbSet<PayoutRequest> PayoutRequests` + config; relación `WalletTransaction.PayoutRequestId`.

## API

### Usar saldo en reserva nueva

`POST /api/bookings` acepta `WalletAmount` (decimal?, opcional) en `CreateBookingRequest`.
- Validar `0 ≤ WalletAmount ≤ min(balance, TotalAmount)`; si excede, `400`.
- Tras crear la reserva: `Payment(Method=Wallet, Amount=WalletAmount, Status=Completed)` + `WalletTransaction(BookingPayment, −WalletAmount, BookingId)`; si `TotalAmount` queda cubierto → `Status=Confirmed`.
- Todo en el `SaveChanges` de creación (o un save inmediato atómico).

### Pagar reserva existente con saldo

`POST /api/bookings/{id}/pay-with-wallet` (owner o Admin), body `{ Amount }`.
- Validar reserva no cancelada; `0 < Amount ≤ min(balance, saldo pendiente de la reserva)`.
- Crear `Payment(Method=Wallet, Amount, Status=Completed)` + `WalletTransaction(BookingPayment, −Amount, BookingId)`; si liquida → `Status=Confirmed`; notificar.

### Pedir reembolso (payout)

- `POST /api/users/me/payout-requests` (cliente), body `{ Amount, Note? }`.
  - Validar `0 < Amount ≤ saldo disponible`; crear `PayoutRequest(Pending)` (congela el monto, sin transacción); notificar al staff (`SendToStaffAsync`); responder el `PayoutRequest` y el saldo (con el disponible actualizado).
- `GET /api/users/me/payout-requests` (cliente): lista propia.
- `GET /api/payout-requests?status=` (StaffOnly): lista para el admin (con datos del usuario).
- `POST /api/payout-requests/{id}/resolve` (AdminOnly), body `{ Approve: bool }`:
  - `Approve=true` → `Status=Paid` (+ `ResolvedAt`/`ResolvedByUserId`) **+ `WalletTransaction(PayoutRequest, −Amount, PayoutRequestId)`** (el saldo total baja por el monto); notificar al cliente.
  - `Approve=false` → `Status=Rejected` (se libera el monto congelado; sin transacción); notificar al cliente.

## UI

### Cliente — perfil (`ClientProfilePage`)
- Debajo de la tarjeta "Mi saldo", label **"Pedir Reembolso"**.
- Al pulsar: `DisplayPromptAsync` del monto (default = saldo, tope = saldo); crea el payout request; muestra el nuevo saldo y abre **WhatsApp** (`https://wa.me/5214444579256?text=...`) con datos del cliente (nombre, correo, teléfono), saldo y monto solicitado + reservas recientes (si es simple).
- Mostrar saldo disponible (ya neto de lo congelado).

### Cliente — checkout (`ClientBookingSeatsPage`)
- Control **"Usar mi saldo"** (switch + input de monto, tope = `min(saldo, total)`); muestra el saldo aplicado y el nuevo total. Pasa `WalletAmount` a `CreateBookingWithSeatsAsync`.

### Cliente — detalle de reserva (`ClientMyBookingDetailPage`)
- Botón **"Realizar pago"** cuando hay saldo pendiente y no está cancelada → vista/modal con el saldo actual + input de monto (tope = `min(saldo, saldo pendiente)`) + "Pagar con saldo". (Preparado para métodos futuros tipo MercadoPago; por ahora solo wallet.)

### Admin — payouts (`AdminPayoutsPage`, nuevo)
- Lista de solicitudes (`GET /api/payout-requests`) con usuario, monto, fecha y estado; acciones **"Marcar como pagado"** y **"Rechazar"** por las pendientes.
- Nueva pestaña admin "Reembolsos" (route `payouts`) visible solo para Admin (`AppShell.xaml` + `App.xaml.cs`).

## Casos límite

- `WalletAmount`/`Amount` ≤ 0 o > saldo o > saldo pendiente → `400`.
- Payout `Amount > balance` → `400`.
- Rechazar un payout ya resuelto → `400` (idempotencia de estado).
- Reserva cancelada → no permite pagar.
- Saldo congelado: un payout pendiente no puede gastarse (el disponible lo excluye vía `Held`).
- Concurrencia: validar el saldo contra la suma de transacciones en el mismo save (evita doble gasto).
- `Payment(Method=Wallet)` no debe contarse como "pago con multa"; el reembolso histórico sigue igual.

## Fuera de alcance

- Integración real con pasarela (MercadoPago u otra): el botón "Realizar pago" queda preparado; solo wallet por ahora.
- Pago mixto (parte wallet + parte pasarela) en una sola transacción.
- Historial paginado del wallet.

## Verificación

- Build API + App.
- Manual:
  1. Cliente con saldo: en checkout aplica saldo parcial → total baja, reserva queda con saldo pendiente; con saldo total → liquidada.
  2. Reserva existente con saldo pendiente: "Realizar pago" con saldo parcial y total.
  3. "Pedir Reembolso" por un monto ≤ disponible → el disponible baja (congelado), el saldo total no; notifica al admin.
  4. Admin: marca pagado → el saldo total baja por el monto (`WalletTransaction`); rechaza → el congelado se libera (saldo total sin cambio).
  5. Validaciones de montos inválidos → `400`.

## Archivos afectados (estimado)

- `src/TravelAgency.Shared/Models/PayoutRequest.cs` (nuevo) + `WalletTransaction.cs` (`PayoutRequestId`) + `AuthModels`/DTOs del checkout.
- `src/TravelAgency.Api/Data/AppDbContext.cs` + `Program.cs` (endpoints + migraciones).
- `src/TravelAgency.App/Services/ApiService.cs`.
- `src/TravelAgency.App/Modules/Client/Views/ClientProfilePage.xaml(.cs)`, `ClientBookingSeatsPage.xaml(.cs)`, `ClientMyBookingDetailPage.xaml(.cs)`.
- `src/TravelAgency.App/Modules/Admin/Views/AdminPayoutsPage.xaml(.cs)` (nuevo) + `AppShell.xaml`, `App.xaml.cs`.
