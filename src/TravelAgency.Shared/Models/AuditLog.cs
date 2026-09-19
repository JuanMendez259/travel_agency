namespace TravelAgency.Shared.Models;

public class AuditLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public string Action { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}