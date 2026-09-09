namespace App.Services;

public sealed class ReceiptArchive
{
    public void Save(string path, string receipt) => File.WriteAllText(path, receipt);

    public string Load(string path) => File.ReadAllText(path);
}
