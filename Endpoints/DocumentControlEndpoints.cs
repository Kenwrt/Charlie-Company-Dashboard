using System.Security.Claims;
using System.Xml;
using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Endpoints;

public static class DocumentControlEndpoints
{
    public static void MapDocumentControlEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var files = endpoints.MapGroup("/document-files").RequireAuthorization();
        files.MapGet("/{documentId:guid}/{versionId:guid}/pdf", (
            Guid documentId, Guid versionId, HttpContext http, ApplicationDbContext db, DocumentObjectStore storage,
            ILogger<DocumentObjectStore> logger, CancellationToken cancellationToken) =>
            DownloadAsync(documentId, versionId, false, http, db, storage, logger, cancellationToken));
        files.MapGet("/{documentId:guid}/{versionId:guid}/word", (
            Guid documentId, Guid versionId, HttpContext http, ApplicationDbContext db, DocumentObjectStore storage,
            ILogger<DocumentObjectStore> logger, CancellationToken cancellationToken) =>
            DownloadAsync(documentId, versionId, true, http, db, storage, logger, cancellationToken))
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Administrator));

        var admin = endpoints.MapGroup("/document-admin")
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Administrator));
        admin.MapPost("/save", SaveAsync);
        admin.MapPost("/{documentId:guid}/upload", UploadAsync)
            .WithMetadata(new RequestSizeLimitAttribute(WordUpload.MaxBytes + 128 * 1024),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = WordUpload.MaxBytes });
        admin.MapPost("/{documentId:guid}/{versionId:guid}/retry", RetryAsync);
    }

    private static async Task<IResult> SaveAsync(
        [FromForm] Guid id, [FromForm] int revision, [FromForm] string? title,
        [FromForm] string? category, [FromForm] string? description, [FromForm] bool? archived,
        HttpContext http, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        title = title?.Trim() ?? "";
        category = category?.Trim() ?? "";
        description = description?.Trim() ?? "";
        if (title.Length is 0 or > 160 || category.Length > 80 || description.Length > 1000)
            return Redirect(id, "invalid-details");

        var document = id == Guid.Empty ? new ControlledDocument { Id = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow }
            : await db.ControlledDocuments.FindAsync([id], cancellationToken);
        if (document is null)
            return Results.NotFound();
        if (document.Revision != revision)
            return Redirect(id, "conflict");

        var before = DocumentDetails(document);
        var wasArchived = document.Archived;
        document.Title = title;
        document.Category = category;
        document.Description = description;
        document.Archived = archived ?? false;
        document.Revision++;
        if (id == Guid.Empty)
            db.ControlledDocuments.Add(document);

        var action = id == Guid.Empty ? "Document created" : (wasArchived, document.Archived) switch
        {
            (false, true) => "Document archived",
            (true, false) => "Document restored",
            _ => "Document updated"
        };
        AddEvent(db, http.User, document.Id, action,
            id == Guid.Empty ? DocumentDetails(document) : $"Previous: {before}\nUpdated: {DocumentDetails(document)}");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Redirect(id, "conflict");
        }

        return Redirect(document.Id, "saved");
    }

    private static async Task<IResult> UploadAsync(
        Guid documentId, IFormFile? file, [FromForm] string? notes, [FromForm] int revision,
        HttpContext http, ApplicationDbContext db, DocumentObjectStore storage, IConfiguration configuration,
        ILogger<DocumentObjectStore> logger, CancellationToken cancellationToken)
    {
        if (!storage.IsConfigured || string.IsNullOrWhiteSpace(configuration["DocumentControl:ConverterPath"]))
            return Redirect(documentId, "unavailable");

        var document = await db.ControlledDocuments.FindAsync([documentId], cancellationToken);
        if (document is null)
            return Results.NotFound();
        if (document.Archived || document.Revision != revision)
            return Redirect(documentId, "conflict");
        if (file is null)
            return Redirect(documentId, "invalid-file");

        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        notes = notes?.Trim() ?? "";
        if (name.Length > 200 || name.Any(char.IsControl) || notes.Length > 600)
            return Redirect(documentId, "invalid-file");

        MemoryStream source;
        try
        {
            source = await WordUpload.ReadAsync(file, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException)
        {
            return Redirect(documentId, "invalid-file");
        }

        await using (source)
        {
            document.Revision++;
            var version = new ControlledDocumentVersion
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                Number = ++document.LastVersion,
                FileName = name,
                Notes = notes,
                UploadedBy = ActorId(http.User),
                UploadedByName = ActorName(http.User),
                UploadedUtc = DateTime.UtcNow,
                Status = DocumentVersionStatus.Uploading
            };
            db.ControlledDocumentVersions.Add(version);
            AddEvent(db, http.User, documentId, "Word upload started", $"Version {version.Number}");
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Redirect(documentId, "conflict");
            }
            catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException
                { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                return Redirect(documentId, "conflict");
            }

            // Complete an accepted upload even if the browser disconnects.
            try
            {
                await storage.PutAsync(version.WordKey, source, DocumentObjectStore.WordContentType, CancellationToken.None);
                version.Status = DocumentVersionStatus.Queued;
                AddEvent(db, http.User, documentId, "Word uploaded", $"Version {version.Number} queued for PDF conversion");
            }
            catch (Exception exception)
            {
                logger.LogWarning("R2 write for document version {VersionId} failed ({ErrorType})", version.Id, exception.GetType().Name);
                version.Status = DocumentVersionStatus.UploadFailed;
                AddEvent(db, http.User, documentId, "Word upload failed", $"Version {version.Number}");
            }

            await db.SaveChangesAsync(CancellationToken.None);
            return Redirect(documentId, version.Status == DocumentVersionStatus.Queued ? "queued" : "upload-failed");
        }
    }

    private static async Task<IResult> RetryAsync(
        Guid documentId, Guid versionId, [FromForm] string? retry,
        HttpContext http, ApplicationDbContext db, DocumentObjectStore storage, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (retry != "true")
            return Results.BadRequest();
        if (!storage.IsConfigured || string.IsNullOrWhiteSpace(configuration["DocumentControl:ConverterPath"]))
            return Redirect(documentId, "unavailable");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var updated = await db.ControlledDocumentVersions.Where(version =>
            version.DocumentId == documentId && version.Id == versionId &&
            version.Status == DocumentVersionStatus.ConversionFailed)
            .ExecuteUpdateAsync(update => update.SetProperty(version => version.Status, DocumentVersionStatus.Queued), cancellationToken);
        if (updated == 0)
            return Redirect(documentId, "conflict");

        AddEvent(db, http.User, documentId, "PDF conversion retried", $"Version ID {versionId}");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Redirect(documentId, "queued");
    }

    private static async Task<IResult> DownloadAsync(
        Guid documentId, Guid versionId, bool word, HttpContext http, ApplicationDbContext db,
        DocumentObjectStore storage, ILogger<DocumentObjectStore> logger, CancellationToken cancellationToken)
    {
        var isAdmin = http.User.IsInRole(ApplicationRoles.Administrator);
        var version = await (from item in db.ControlledDocumentVersions.AsNoTracking()
            join document in db.ControlledDocuments on item.DocumentId equals document.Id
            where item.DocumentId == documentId && item.Id == versionId && (!document.Archived || isAdmin)
                && (isAdmin || !db.ControlledDocumentVersions.Any(newer =>
                    newer.DocumentId == documentId && newer.Status == DocumentVersionStatus.Published &&
                    newer.Number > item.Number))
            select item).SingleOrDefaultAsync(cancellationToken);
        if (version is null || (word
            ? version.Status is DocumentVersionStatus.UploadFailed or DocumentVersionStatus.Uploading
            : version.Status != DocumentVersionStatus.Published || version.ConversionAttemptId is null))
            return Results.NotFound();
        if (!storage.IsConfigured)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        try
        {
            var key = word ? version.WordKey : version.GetPdfKey(version.ConversionAttemptId!.Value);
            var response = await storage.GetAsync(key, cancellationToken);
            http.Response.RegisterForDispose(response);
            if (word)
            {
                AddEvent(db, http.User, documentId, "Word downloaded", $"Version {version.Number}");
                await db.SaveChangesAsync(cancellationToken);
            }

            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(response.ResponseStream, word ? DocumentObjectStore.WordContentType : "application/pdf",
                fileDownloadName: word ? version.FileName : null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Document version {VersionId} could not be served ({ErrorType})", versionId, exception.GetType().Name);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static string DocumentDetails(ControlledDocument document) =>
        $"{document.Title} | {document.Category} | {document.Description} | Archived: {document.Archived}";

    private static string ActorId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private static string ActorName(ClaimsPrincipal user) => user.Identity?.Name ?? ActorId(user);

    private static void AddEvent(ApplicationDbContext db, ClaimsPrincipal actor, Guid documentId, string action, string detail)
    {
        db.ControlledDocumentEvents.Add(new ControlledDocumentEvent
        {
            DocumentId = documentId,
            UserId = ActorId(actor),
            UserName = ActorName(actor),
            Action = action,
            Detail = detail,
            AtUtc = DateTime.UtcNow
        });
    }

    private static IResult Redirect(Guid id, string status) => Results.LocalRedirect(
        id == Guid.Empty ? $"/documents?status={status}" : $"/documents/{id}?status={status}");
}
