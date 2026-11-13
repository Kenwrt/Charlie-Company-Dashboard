# Document control

Authenticated dashboard users can search the document library and open the latest successfully published PDF for each active document. Administrators can define documents, edit metadata, upload new Word versions, download prior Word versions, view prior PDFs, retry conversion and archive or restore documents. Archiving hides the document and its PDFs from readers. It preserves the version history and audit trail.

Uploads accept macro-free .docx files up to 20 MB. Embedded objects and linked resources are rejected. HTTP, HTTPS and email hyperlinks are supported. Each accepted upload is stored privately in R2 and queued for conversion. A failed conversion leaves the previous published PDF available. Version history shows the uploader, timestamps, change notes and conversion status.

## Configuration

1. Activate R2 in the selected Cloudflare account. Create a dedicated Standard bucket with public access disabled, such as `charlie-company-documents`. Keep both the public development URL and public custom domains disabled.
2. Provision an R2 S3-compatible Object Read & Write credential scoped to that bucket. Enter credentials directly through the approved protected settings mechanism. Avoid putting credentials in source, shell history, chat or command output.
3. Configure the following settings. The double underscores are for environment variables.

| Setting | Value |
| --- | --- |
| `DocumentControl__R2__AccountId` | Cloudflare account ID |
| `DocumentControl__R2__Bucket` | Dedicated private R2 bucket name |
| `DocumentControl__R2__AccessKeyId` | Protected R2 S3 access key ID |
| `DocumentControl__R2__SecretAccessKey` | Protected R2 S3 secret access key |
| `DocumentControl__ConverterPath` | Absolute path to the isolated converter wrapper, typically `/opt/wrightapps/charlie-company/shared/document-converter/soffice-container` on API01 |

Stage the files in `deploy/document-converter` on API01. Build the image with `docker build -t charlie-document-converter:1 <staged-directory>` and make `soffice-container` executable. The wrapper sends Word bytes through standard input and writes the returned PDF to the application’s temporary directory. The container runs without a network, host mounts, Linux capabilities or a writable root filesystem. Keep the image patched when updating the service.

The `AddDocumentControl` migration adds metadata, version and audit tables. PostgreSQL stores metadata only. Production installation, migration and application restart follow the dashboard's existing release procedures.

Retain the bucket and database backups together. Published versions and archived documents have no automatic expiration. Keep the object prefixes private and avoid blanket lifecycle expiration of repository objects. If a conversion is interrupted after writing a PDF but before publication, an unreferenced attempt PDF can remain in R2. Reconcile attempt objects against version metadata before applying any cleanup.
