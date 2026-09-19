using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using TravelAgency.Shared.Models;

namespace TravelAgency.Api.Data;

public class AuditLogInterceptor : SaveChangesInterceptor
{
    private static readonly AsyncLocal<List<PendingAudit>> PendingStore = new();

    private static readonly HashSet<string> TripTracked = new()
    {
        "Title", "Destination", "Description", "StartDate", "EndDate", "Price", "Capacity",
        "AvailableSeats", "TransportType", "IsActive",
        "OriginLatitude", "OriginLongitude", "DestinationLatitude", "DestinationLongitude"
    };

    private static readonly HashSet<string> BookingTracked = new()
    {
        "NumberOfSeats", "Status", "TotalAmount"
    };

    private static readonly HashSet<string> PaymentTracked = new()
    {
        "Amount", "Method", "Status", "TransactionReference"
    };

    private readonly IHttpContextAccessor _http;
    private readonly DbContextOptions<AppDbContext> _auditOptions;

    public AuditLogInterceptor(IHttpContextAccessor http, IConfiguration config)
    {
        _http = http;

        var provider = config["Database:Provider"] ?? "sqlite";
        var sqlServer = config.GetConnectionString("SqlServer");
        var supabase = config.GetConnectionString("Supabase");
        var options = new DbContextOptionsBuilder<AppDbContext>();

        if (provider == "sqlserver" && !string.IsNullOrEmpty(sqlServer))
            options.UseSqlServer(sqlServer);
        else if (provider == "supabase" && !string.IsNullOrEmpty(supabase))
            options.UseNpgsql(supabase);
        else
            options.UseSqlite("Data Source=travelagency.db");

        _auditOptions = options.Options;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is not null)
        {
            var pending = PendingStore.Value;
            if (pending is null)
            {
                pending = new List<PendingAudit>();
                PendingStore.Value = pending;
            }

            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                    continue;

                if (entry.Entity is AuditLog)
                    continue;

                if (entry.Entity is not (Trip or Booking or Payment))
                    continue;

                var action = entry.State switch
                {
                    EntityState.Added => "Created",
                    EntityState.Modified => "Updated",
                    _ => "Deleted"
                };

                var diff = BuildDiff(entry, action);
                if (action == "Updated" && diff is null)
                    continue;

                pending.Add(new PendingAudit(entry, action, diff));
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        var pending = PendingStore.Value;
        PendingStore.Value = null;

        if (pending is { Count: > 0 })
        {
            await PersistAsync(pending, cancellationToken);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private static Dictionary<string, string[]>? BuildDiff(EntityEntry entry, string action)
    {
        if (action != "Updated")
            return null;

        var tracked = entry.Entity switch
        {
            Trip => TripTracked,
            Booking => BookingTracked,
            _ => PaymentTracked
        };

        Dictionary<string, string[]>? diff = null;
        foreach (var property in entry.Properties)
        {
            if (!tracked.Contains(property.Metadata.Name))
                continue;

            var current = property.CurrentValue;
            var original = property.Metadata.IsShadowProperty() ? null : property.OriginalValue;

            if (Equals(current, original))
                continue;

            diff ??= new Dictionary<string, string[]>();
            diff[property.Metadata.Name] = new[]
            {
                original?.ToString() ?? "",
                current?.ToString() ?? ""
            };
        }

        return diff;
    }

    private async Task PersistAsync(List<PendingAudit> pending, CancellationToken cancellationToken)
    {
        var userId = 0;
        var userName = "";
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var idValue = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
            userId = int.TryParse(idValue, out var id) ? id : 0;
            userName = user.FindFirstValue(ClaimTypes.Name)
                ?? user.FindFirstValue("name")
                ?? "";
        }

        await using var ctx = new AppDbContext(_auditOptions);

        foreach (var item in pending)
        {
            var entityType = item.Entry.Entity.GetType().Name;
            var entityId = item.Entry.Property("Id").CurrentValue is int v ? v : 0;
            var summary = await BuildSummaryAsync(ctx, item, cancellationToken);
            if (string.IsNullOrEmpty(summary))
                continue;

            ctx.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                UserName = userName,
                EntityType = entityType,
                EntityId = entityId,
                Action = item.Action,
                Summary = summary,
                Details = item.Diff is null ? null : JsonSerializer.Serialize(item.Diff),
                CreatedAt = DateTime.UtcNow
            });
        }

        await ctx.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string> BuildSummaryAsync(AppDbContext ctx, PendingAudit item, CancellationToken cancellationToken)
    {
        var action = item.Action;
        var entity = item.Entry.Entity;

        if (entity is Trip trip)
        {
            var title = trip.Title ?? "";
            return action switch
            {
                "Created" => $"Se creó el viaje: {title}",
                "Updated" => $"Se modificó el viaje: {title}",
                _ => $"Se eliminó el viaje: {title}"
            };
        }

        if (entity is Booking booking)
        {
            var tripTitle = await ctx.Trips
                .AsNoTracking()
                .Where(t => t.Id == booking.TripId)
                .Select(t => t.Title)
                .FirstOrDefaultAsync(cancellationToken);
            var label = string.IsNullOrEmpty(tripTitle) ? $"viaje #{booking.TripId}" : tripTitle;
            var seats = booking.NumberOfSeats;
            return action switch
            {
                "Created" => $"Se creó la reserva de {seats} asiento(s) para {label}",
                "Updated" => $"Se modificó la reserva ({seats} asiento(s)) para {label}",
                _ => $"Se eliminó la reserva de {seats} asiento(s) para {label}"
            };
        }

        if (entity is Payment payment)
        {
            var amount = $"${payment.Amount:0.##}";
            return action switch
            {
                "Created" => $"Se registró un pago de {amount} ({payment.Method})",
                "Updated" => $"Se actualizó el pago de {amount}",
                _ => $"Se eliminó un pago de {amount}"
            };
        }

        return "";
    }

    private sealed record PendingAudit(EntityEntry Entry, string Action, Dictionary<string, string[]>? Diff);
}