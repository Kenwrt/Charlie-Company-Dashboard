using System.IO.Compression;
using CharleyCompany.Dashboard.Web.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

public sealed class DocumentControlTests
{
    [Fact]
    public async Task WordUploadAcceptsMacroFreeDocumentAndRewindsContent()
    {
        using var source = CreatePackage();
        using var upload = await WordUpload.ReadAsync(
            new FormFile(source, 0, source.Length, "file", "fixture.docx"), CancellationToken.None);

        Assert.Equal(0, upload.Position);
        Assert.Equal(source.ToArray(), upload.ToArray());
    }

    [Theory]
    [InlineData("fixture.docm", false)]
    [InlineData("fixture.docx", true)]
    public async Task WordUploadRejectsUnsupportedExtensionAndLinkedResources(string filename, bool linkResource)
    {
        using var source = CreatePackage(linkResource);
        await Assert.ThrowsAsync<InvalidDataException>(() => WordUpload.ReadAsync(
            new FormFile(source, 0, source.Length, "file", filename), CancellationToken.None));
    }

    private static MemoryStream CreatePackage(bool linkResource = false)
    {
        var source = new MemoryStream();
        using var archive = new ZipArchive(source, ZipArchiveMode.Create, leaveOpen: true);
        Write(archive, "[Content_Types].xml", """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        Write(archive, "_rels/.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        Write(archive, "word/document.xml", """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p><w:r><w:t>Document control conversion fixture</w:t></w:r></w:p></w:body>
            </w:document>
            """);
        if (linkResource)
            Write(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="https://example.org/image.png" TargetMode="External"/>
                </Relationships>
                """);
        return source;
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }
}
