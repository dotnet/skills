using System.IO;
public static class Reader
{
    public static int Read(string path) {
        var stream = File.OpenRead(path);
        return stream.ReadByte();
    }
}
