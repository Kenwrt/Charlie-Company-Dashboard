using System.ComponentModel.DataAnnotations;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class CentComChatSession
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = "New CentCom Chat";
    [Required, StringLength(450)] public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<CentComChatMessage> Messages { get; set; } = [];
}

public sealed class CentComChatMessage
{
    public int Id { get; set; }
    public int CentComChatSessionId { get; set; }
    public CentComChatSession CentComChatSession { get; set; } = null!;
    [Required, StringLength(20)] public string Role { get; set; } = "user";
    [Required, StringLength(12000)] public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
