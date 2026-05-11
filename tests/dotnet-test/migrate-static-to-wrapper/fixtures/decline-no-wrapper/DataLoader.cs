namespace MyProject;
public class DataLoader
{
    public string Load(string path) => File.ReadAllText(path);
}
