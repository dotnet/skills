using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal struct PacketHeader
{
    internal byte Kind;
    internal uint Length;
    internal ushort Flags;
    internal bool Compressed;
}
