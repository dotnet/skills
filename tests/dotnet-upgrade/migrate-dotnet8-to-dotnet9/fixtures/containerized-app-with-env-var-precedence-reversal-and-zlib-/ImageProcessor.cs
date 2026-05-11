class ImageProcessor
{
    void Process()
    {
        // Uses SkiaSharp which depends on system libz/zlib
        var image = LoadImage("input.png");
        var compressed = Compress(image);
    }
    object LoadImage(string path) => null;
    byte[] Compress(object img) => null;
}
