using System.ComponentModel.DataAnnotations;

namespace CharleyCompany.Dashboard.Web.Data;

public sealed class ControlledDocument
{
    public Guid Id { get; set; }
    [MaxLength(160)] public string Title { get; set; } = "";
    [MaxLength(80)] public string Category { get; set; } = "";
    [MaxLength(1000)] public string Description { get; set; } = "";
    public bool Archived { get; set; }
    [ConcurrencyCheck] public int Revision { get; set; }
    public int LastVersion { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class ControlledDocumentVersion
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int Number { get; set; }
    [MaxLength(200)] public string FileName { get; set; } = "";
    [MaxLength(600)] public string Notes { get; set; } = "";
    [MaxLength(450)] public string UploadedBy { get; set; } = "";
    [MaxLength(256)] public string UploadedByName { get; set; } = "";
    public DateTime UploadedUtc { get; set; }
    public DateTime? PublishedUtc { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public Guid? ConversionAttemptId { get; set; }
    public DocumentVersionStatus Status { get; set; }

    public string WordKey => $"documents/{DocumentId:N}/{Id:N}/source.docx";
    public string GetPdfKey(Guid attempt) => $"documents/{DocumentId:N}/{Id:N}/{attempt:N}.pdf";
}

public enum DocumentVersionStatus
{
    Uploading,
    UploadFailed,
    Queued,
    Converting,
    ConversionFailed,
    Published
}

public sealed class ControlledDocumentEvent
{
    public long Id { get; set; }
    public Guid DocumentId { get; set; }
    [MaxLength(450)] public string UserId { get; set; } = "";
    [MaxLength(256)] public string UserName { get; set; } = "";
    [MaxLength(60)] public string Action { get; set; } = "";
    [MaxLength(3000)] public string Detail { get; set; } = "";
    public DateTime AtUtc { get; set; }
}
