using System.IO.Compression;
class ZipCreator
{
    void CreateArchive()
    {
        using var archive = ZipFile.Open("data.zip", ZipArchiveMode.Create);
        archive.CreateEntry("レポート/月次.csv");  // Japanese path
        archive.CreateEntry("données/résumé.txt"); // French accented
    }
}
