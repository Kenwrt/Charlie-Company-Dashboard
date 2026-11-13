using System.Diagnostics;
using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Services;

public sealed class DocumentConversionWorker(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    DocumentObjectStore storage,
    IConfiguration configuration,
    ILogger<DocumentConversionWorker> logger) : BackgroundService
{
    private readonly string? converter = configuration["DocumentControl:ConverterPath"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!storage.IsConfigured || string.IsNullOrWhiteSpace(converter))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await ProcessOneAsync(stoppingToken))
                    continue;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError("Document conversion queue operation failed ({ErrorType})", exception.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task<bool> ProcessOneAsync(CancellationToken stoppingToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
        var now = DateTime.UtcNow;
        var versions = db.ControlledDocumentVersions;
        await RecoverUploadsAsync(db, now, stoppingToken);
        var available = versions.Where(version => version.Status == DocumentVersionStatus.Queued ||
            (version.Status == DocumentVersionStatus.Converting && version.LeaseUntilUtc < now));
        var version = await available.AsNoTracking().OrderBy(version => version.UploadedUtc)
            .FirstOrDefaultAsync(stoppingToken);
        if (version is null)
            return false;

        var attempt = Guid.NewGuid();
        var claimed = await available.Where(candidate => candidate.Id == version.Id)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.Status, DocumentVersionStatus.Converting)
                .SetProperty(row => row.LeaseUntilUtc, now.AddMinutes(5))
                .SetProperty(row => row.ConversionAttemptId, attempt), stoppingToken);
        if (claimed == 0)
            return true;

        var owned = versions.Where(candidate => candidate.Id == version.Id &&
            candidate.Status == DocumentVersionStatus.Converting && candidate.ConversionAttemptId == attempt);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        try
        {
            await ConvertAsync(version, attempt, timeout.Token);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Document version {VersionId} conversion failed ({ErrorType})", version.Id, exception.GetType().Name);
            await FinishAttemptAsync(db, owned, version, DocumentVersionStatus.ConversionFailed, stoppingToken);
            return true;
        }

        if (await FinishAttemptAsync(db, owned, version, DocumentVersionStatus.Published, stoppingToken))
            logger.LogInformation("PDF generated for document version {VersionId}", version.Id);
        return true;
    }

    private static async Task<bool> FinishAttemptAsync(
        ApplicationDbContext db, IQueryable<ControlledDocumentVersion> owned, ControlledDocumentVersion version,
        DocumentVersionStatus status, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var published = status == DocumentVersionStatus.Published;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var updated = await owned.ExecuteUpdateAsync(update => update
            .SetProperty(row => row.Status, status)
            .SetProperty(row => row.PublishedUtc, published ? now : (DateTime?)null)
            .SetProperty(row => row.LeaseUntilUtc, (DateTime?)null), cancellationToken);
        if (updated == 0)
            return false;

        db.ControlledDocumentEvents.Add(new ControlledDocumentEvent
        {
            DocumentId = version.DocumentId,
            UserId = "system",
            UserName = "PDF converter",
            Action = published ? "PDF published" : "PDF conversion failed",
            Detail = $"Version {version.Number}",
            AtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task RecoverUploadsAsync(ApplicationDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddMinutes(-5);
        var expired = db.ControlledDocumentVersions.Where(version =>
            version.Status == DocumentVersionStatus.Uploading && version.UploadedUtc < cutoff);
        var interrupted = await expired.AsNoTracking().OrderBy(version => version.UploadedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (interrupted is null)
            return;

        var status = DocumentVersionStatus.Queued;
        try
        {
            using var source = await storage.GetAsync(interrupted.WordKey, cancellationToken);
        }
        catch (Amazon.S3.AmazonS3Exception exception) when (
            exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            status = DocumentVersionStatus.UploadFailed;
        }

        await expired.Where(version => version.Id == interrupted.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(version => version.Status, status), cancellationToken);
    }

    private async Task ConvertAsync(ControlledDocumentVersion version, Guid attempt, CancellationToken cancellationToken)
    {
        var work = Directory.CreateTempSubdirectory("cc-doc-");
        try
        {
            var sourcePath = Path.Combine(work.FullName, "source.docx");
            using (var source = await storage.GetAsync(version.WordKey, cancellationToken))
            await using (var file = File.Create(sourcePath))
            {
                await source.ResponseStream.CopyToAsync(file, cancellationToken);
            }

            var start = new ProcessStartInfo(converter!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = work.FullName
            };
            // Pass only the environment needed to start the converter. Application secrets stay in the parent.
            start.Environment.TryGetValue("PATH", out var path);
            start.Environment.TryGetValue("SystemRoot", out var systemRoot);
            start.Environment.Clear();
            if (path is not null)
                start.Environment["PATH"] = path;
            if (systemRoot is not null)
                start.Environment["SystemRoot"] = systemRoot;
            start.Environment["HOME"] = work.FullName;
            start.Environment["TMPDIR"] = work.FullName;
            start.Environment["TEMP"] = work.FullName;

            var profile = new Uri(Path.Combine(work.FullName, "profile")).AbsoluteUri;
            foreach (var argument in new[]
            {
                $"-env:UserInstallation={profile}", "--headless", "--nologo", "--nodefault",
                "--norestore", "--convert-to",
                "pdf:writer_pdf_Export:{\"ExportNotes\":{\"type\":\"boolean\",\"value\":\"false\"},\"ExportBookmarks\":{\"type\":\"boolean\",\"value\":\"false\"}}",
                "--outdir", work.FullName, sourcePath
            })
                start.ArgumentList.Add(argument);

            using var process = Process.Start(start) ?? throw new InvalidOperationException("Converter could not start.");
            try
            {
                await Task.WhenAll(
                    process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cancellationToken),
                    process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken),
                    process.WaitForExitAsync(cancellationToken));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }

            if (process.ExitCode != 0)
                throw new InvalidDataException("PDF conversion failed.");

            await using var pdf = File.OpenRead(Path.Combine(work.FullName, "source.pdf"));
            var signature = new byte[5];
            await pdf.ReadExactlyAsync(signature, cancellationToken);
            if (!signature.AsSpan().SequenceEqual("%PDF-"u8) || pdf.Length > 60 * 1024 * 1024)
                throw new InvalidDataException("Converter returned an invalid or oversized PDF.");
            pdf.Position = 0;
            await storage.PutAsync(version.GetPdfKey(attempt), pdf, "application/pdf", cancellationToken);
        }
        finally
        {
            try
            {
                work.Delete(recursive: true);
            }
            catch (IOException)
            {
                logger.LogWarning("Could not remove temporary conversion files for document version {VersionId}", version.Id);
            }
            catch (UnauthorizedAccessException)
            {
                logger.LogWarning("Could not remove temporary conversion files for document version {VersionId}", version.Id);
            }
        }
    }
}
