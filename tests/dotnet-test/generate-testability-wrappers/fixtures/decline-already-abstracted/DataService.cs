using System.IO.Abstractions;
namespace MyApp.Services;
public class DataService(IFileSystem fileSystem)
{
    public string ReadData(string path) => fileSystem.File.ReadAllText(path);
}
