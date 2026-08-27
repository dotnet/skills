namespace App.Abstractions;

public interface ITextFileStore
{
    void WriteAllText(string path, string contents);

    string ReadAllText(string path);
}
