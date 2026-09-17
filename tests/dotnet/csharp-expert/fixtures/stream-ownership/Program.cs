using System.Text;

using var stream = new MemoryStream();
stream.Write(Encoding.UTF8.GetBytes("header\nbody"));
stream.Position = 0;

if (HeaderReader.ReadFirstLine(stream) != "header")
{
    throw new InvalidOperationException("The header was not read.");
}

if (!stream.CanRead)
{
    throw new InvalidOperationException("The caller-owned stream was closed.");
}

stream.Position = stream.Length;
stream.WriteByte((byte)'!');
Console.WriteLine("ownership-ok");
