# Diseño — Cancelación parcial de boletos con wallet (Fase 1)

Fecha: 2026-10-08
Estado: propuesto (pendiente de revisión)
Alcance: **Fase 1** (wallet + cancelaciones). La Fase 2 (usar el saldo y "Pedir Reembolso") se diseña por separado.

## Contexto y objetivo

Hoy una reserva solo puede cancelarse **completa** (`POST /api/bookings/{id}/cancel`). Si un cliente
tiene 3 asientos y quiere cancelar uno, no hay forma: tendría que cancelar todo.

Objetivo: permitir cancelar **un boleto (acompañante)** dentro de una reserva, liberar su asiento y
acreditar el reembolso a un **saldo a favor (wallet)** del cliente, sin romper reservas, asientos,
pagos ni la disponibilidad del viaje. El reembolso se calcula con la política existente
(`BookingRefundPolicy`) y **siempre va a la wallet** (no se devuelve dinero en esta fase).

## Decisiones (confirmadas)

1. **Parcial**: solo **acompañantes** (`TripPassenger`). El asiento del titular solo se cancela con la
   reserva completa.
2. **Viajes con opciones**: al cancelar un acompañante se **elige la línea/entrada** (`BookingItem`)
   para saber el precio; la línea se decrementa.
3. **Motivo obligatorio** en cancelación parcial **y** en la global (se guarda en `BookingRefund.Reason`).
4. **Reembolso prorrateado** por la política vigente (100% dentro del límite, 70% fuera) sobre la parte
   abonada del boleto.
5. **Todo el reembolso va a la wallet** (parcial y global). Saldo **global por usuario**.
6. Cliente **y** admin pueden cancelar parcialmente.
7. **Por fases**: esta es la Fase 1.

## Modelo de datos

### `BookingRefund` (registro de cada cancelación que genera saldo)

```
Id, BookingId, UserId, Amount, PenaltyAmount, Reason,
PassengerName (snapshot, nullable), SeatNumber (nullable), BookingItemId (nullable),
CreatedAt
```

### `WalletTransaction` (ledger del saldo; saldo = suma de Amount)

```
Id, UserId, Amount (+crédito / −débito), Type, BookingId (nullable),
RefundId (nullable), Note (nullable), CreatedAt
```

`WalletType` (enum): `CancellationCredit = 0`, `BookingPayment = 1`, `PayoutRequest = 2`, `Adjustment = 3`.
(En Fase 1 solo se usa `CancellationCredit`; el resto queda para Fase 2.)

### `PaymentMethod.Wallet`

Nuevo valor `Wallet = 6` (para Fase 2). No requiere migración (se guarda como int).

### Migraciones idempotentes

- `EnsureBookingRefundsTableAsync`: crea `BookingRefunds` (sqlite/postgres) con FKs a `Bookings` y `Users`
  (`Restrict`) e índice por `BookingId` y por `UserId`.
- `EnsureWalletTransactionsTableAsync`: crea `WalletTransactions` con FK a `Users` (`Restrict`) e índices
  por `UserId`.
- `AppDbContext`: `DbSet<BookingRefund> BookingRefunds`, `DbSet<WalletTransaction> WalletTransactions` y
  su configuración.

## Cálculo del reembolso (fuente única en `BookingRefundPolicy`)

Se agrega a `BookingRefundPolicy` (Shared) para que API y app no diverjan:

- `NetUnitPrice(booking, grossUnitPrice)`: precio **neto** (descontado) del boleto.
  `grossBefore = TotalAmount + DiscountAmount`;
  si `grossBefore > 0` → `Round(grossUnitPrice * TotalAmount / grossBefore, 2)`; si no → `grossUnitPrice`.
- `PaidShare(booking, paid, netUnit)`: parte abonada atribuible al boleto.
  si `TotalAmount > 0` → `Round(paid * netUnit / TotalAmount, 2)`; si no → `paid / NumberOfSeats`.

`withinPolicy = IsWithinPolicy(trip.StartDate, today, trip.CancellationDaysLimit)`.
`refund = RefundFrom(paidShare, withinPolicy)`; `penalty = PenaltyFrom(paidShare, withinPolicy)`.

## Cancelación parcial

### Endpoint

`POST /api/bookings/{id}/cancel-ticket` (owner o Admin).

Body `CancelTicketRequest`:
```
{ PassengerId: int, BookingItemId: int?, Reason: string }
```

### Validaciones

- Reserva existe y no está `Cancelled`.
- `trip.DepartureCompleted == false` y `trip.Finalized == false`; `trip.StartDate.Date > today`.
- `Reason` no vacío (obligatorio).
- El `PassengerId` pertenece a la reserva.
- Si el viaje tiene opciones con más de una línea activa: `BookingItemId` obligatorio y perteneciente a
  la reserva. Si el viaje no tiene opciones, `BookingItemId` se ignora.

### Efectos (una transacción)

1. `grossUnitPrice` = precio de la línea (según `passenger.IsChild`: `UnitPriceChild ?? UnitPriceAdult`,
   o `UnitPriceAdult`) o, sin opciones, `TotalAmount / NumberOfSeats`.
2. `netUnit = NetUnitPrice(booking, grossUnitPrice)`; `paidShare = PaidShare(booking, paid, netUnit)`;
   `refund`/`penalty` según política.
3. Decrementar la línea: `BookingItem.Adults--` o `Children--` (según `IsChild`); si la línea queda en 0,
   eliminarla.
4. Eliminar el `TripPassenger` (libera su asiento).
5. `booking.NumberOfSeats--`.
6. Ajustar dinero: `DiscountAmount -= Round(DiscountAmount * grossUnitPrice / grossBefore, 2)` (≥ 0) y
   `TotalAmount = Max(0, TotalAmount - netUnit)`.
7. Crear `BookingRefund` (Amount = refund, PenaltyAmount = penalty, Reason, PassengerName, SeatNumber,
   BookingItemId).
8. Crear `WalletTransaction` (`CancellationCredit`, `Amount = +refund`, `BookingId`, `RefundId`).
9. Si `NumberOfSeats == 0` → `Status = Cancelled`, `CancelledAt`, `CancelledByUserId`.
10. `RecomputeAvailabilityAsync(trip)`.
11. Notificar al cliente ("Se canceló el asiento N; se acreditaron {refund} a tu saldo") y al staff
    (`SendToStaffAsync`).

Respuesta: `CancelTicketResult` (Booking, RefundAmount, PenaltyAmount, WithinPolicy, WalletBalance).

## Cancelación global (cambio de comportamiento)

`POST /api/bookings/{id}/cancel` pasa a recibir `CancelBookingRequest { Reason }` (motivo obligatorio) y:

- `refund = RefundFrom(paid, withinPolicy)` (como hoy).
- **Ya no** marca los pagos como `Refunded`. En su lugar crea `BookingRefund` + `WalletTransaction`
  (`CancellationCredit`, +refund).
- `Status = Cancelled`, `CancelledAt`, `CancelledByUserId`; `RecomputeAvailabilityAsync`; notifica.
- Los pagos quedan `Completed` (el dinero está en la wallet, no salió).

> Impacto en UI: la reserva cancelada ya no muestra "Reembolsado {X}" (basado en pagos `Refunded`); pasa a
> mostrar "Saldo a favor: {X}" (desde `BookingRefund`/wallet).

## Wallet (API)

`GET /api/users/me/wallet` → `WalletSummary { Balance, Transactions[] }` (saldo = suma de
`WalletTransaction.Amount` del usuario). Autorizado al propio usuario (y Admin).

## UI (Fase 1)

- **`ClientProfilePage`**: tarjeta "Mi saldo" con el monto (`GET /api/users/me/wallet`). El label
  "Pedir Reembolso" queda para Fase 2.
- **`ClientMyBookingDetailPage`**:
  - Por acompañante: acción "Cancelar boleto" → pide **motivo** y, si el viaje tiene opciones, la
    **entrada**; llama al endpoint y refresca.
  - Cancelación global: pide **motivo** antes de cancelar.
  - Estado de la reserva cancelada: "Saldo a favor: {X}" en lugar de "Reembolsado".
- **`AdminBookingDetailPage`**: lista de acompañantes con acción "Cancelar boleto" (mismo endpoint;
  Admin autorizado) + motivo/línea.

## Casos límite

- Cancelar el último acompañante que deja `NumberOfSeats == 1` (solo titular): permitido.
- `NumberOfSeats` llega a 0 → la reserva pasa a `Cancelled`.
- Viaje con opciones y `BookingItemId` ausente o de otra reserva → `400`.
- Reserva ya cancelada / viaje iniciado o finalizado / fuera de la ventana de fechas → `400` (igual que
  la cancelación global).
- Motivo vacío → `400`.
- `paid == 0` (nada abonado): `refund = 0`; se registra igual el `BookingRefund` (Amount 0) y se libera
  el asiento.
- Wallet: los `Amount` son la fuente del saldo; no puede quedar negativo por cancelaciones (solo suman).
- Compatibilidad: reservas y pagos existentes no cambian; las cancelaciones anteriores siguen mostrando
  su reembolso histórico (pagos `Refunded`).

## Fuera de alcance (Fase 2)

- Pagar con saldo una reserva existente o nueva (monto seleccionable).
- "Pedir Reembolso": WhatsApp + `WalletTransaction(PayoutRequest)` + notificación al admin.
- `PaymentMethod.Wallet` en uso y débitos de wallet.

## Verificación

- Build: `dotnet build src/TravelAgency.Api/TravelAgency.Api.csproj` y
  `dotnet build src/TravelAgency.App/TravelAgency.App.csproj -f net10.0-maccatalyst`.
- Prueba manual:
  1. Reserva de 3 asientos (sin opciones), dentro del límite: cancelar un acompañante → asiento liberado,
     `NumberOfSeats=2`, total reducido, saldo a favor = reembolso (100% de la parte abonada), aviso.
  2. Mismo caso fuera del límite → reembolso 70%.
  3. Viaje con opciones (VIP/Normal): cancelar un acompañante eligiendo la línea → línea decrementada,
     total reducido por el precio de esa entrada.
  4. Cancelar el último acompañante → reserva completa.
  5. Cancelación global con motivo → saldo a favor; la vista ya no dice "Reembolsado".
  6. `GET /api/users/me/wallet` refleja el saldo y los movimientos.
  7. Disponibilidad del viaje sube al liberar asientos.

## Archivos afectados (estimado)

- `src/TravelAgency.Shared/Models/BookingRefund.cs` (nuevo)
- `src/TravelAgency.Shared/Models/WalletTransaction.cs` (nuevo)
- `src/TravelAgency.Shared/Models/BookingRefundPolicy.cs` (helpers de precio neto/parte abonada)
- `src/TravelAgency.Shared/Models/Payment.cs` (`PaymentMethod.Wallet`)
- `src/TravelAgency.Api/Data/AppDbContext.cs`
- `src/TravelAgency.Api/Program.cs` (endpoints + migraciones + cancelación global)
- `src/TravelAgency.App/Services/ApiService.cs`
- `src/TravelAgency.App/Modules/Client/Views/ClientProfilePage.xaml(.cs)`
- `src/TravelAgency.App/Modules/Client/Views/ClientMyBookingDetailPage.xaml(.cs)`
- `src/TravelAgency.App/Modules/Admin/Views/AdminBookingDetailPage.xaml(.cs)`
