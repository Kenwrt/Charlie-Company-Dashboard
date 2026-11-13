using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace CharleyCompany.Dashboard.Web.Services;

public static class WordUpload
{
    public const int MaxBytes = 20 * 1024 * 1024;
    private const long MaxExpandedBytes = 100 * 1024 * 1024;

    public static async Task<MemoryStream> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaxBytes ||
            !Path.GetExtension(file.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a Word .docx file up to 20 MB.");

        var buffer = new MemoryStream((int)file.Length);
        try
        {
            await file.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (archive.Entries.Count > 2000 ||
                    archive.Entries.Sum(entry => (decimal)entry.Length) > MaxExpandedBytes ||
                    archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != archive.Entries.Count ||
                    archive.Entries.Any(entry => entry.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) ||
                        entry.FullName.StartsWith("word/embeddings/", StringComparison.OrdinalIgnoreCase) ||
                        entry.FullName.StartsWith("word/activeX/", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("The Word file exceeds package limits or contains active or embedded objects.");

                XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
                var manifest = ReadXml(archive.GetEntry("[Content_Types].xml"));
                if (!manifest.Elements(types + "Override").Any(part =>
                        (string?)part.Attribute("PartName") == "/word/document.xml" &&
                        (string?)part.Attribute("ContentType") == "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))
                    throw new InvalidDataException("The file must be a macro-free Word document.");

                var document = ReadXml(archive.GetEntry("word/document.xml"));
                if (document.Name != XName.Get("document", "http://schemas.openxmlformats.org/wordprocessingml/2006/main") &&
                    document.Name != XName.Get("document", "http://purl.oclc.org/ooxml/wordprocessingml/main"))
                    throw new InvalidDataException("The Word file is invalid.");

                foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
                {
                    if (ReadXml(entry).Elements().Any(IsUnsupportedExternalRelationship))
                        throw new InvalidDataException("Remove linked resources from the Word file before uploading.");
                }
            }

            buffer.Position = 0;
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private static bool IsUnsupportedExternalRelationship(XElement link)
    {
        if (!string.Equals((string?)link.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
            return false;

        var isHyperlink = (string?)link.Attribute("Type") is
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" or
            "http://purl.oclc.org/ooxml/officeDocument/relationships/hyperlink";
        return !isHyperlink || !Uri.TryCreate((string?)link.Attribute("Target"), UriKind.Absolute, out var target) ||
            target.Scheme is not ("https" or "http" or "mailto");
    }

    private static XElement ReadXml(ZipArchiveEntry? entry)
    {
        if (entry is null)
            throw new InvalidDataException("The Word package is incomplete.");

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = 20 * 1024 * 1024
        });
        return XElement.Load(reader);
    }
}
