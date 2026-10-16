using System;
using System.Runtime.InteropServices;

bool valid =
    Marshal.SizeOf<PacketHeader>() == 8 &&
    Marshal.OffsetOf<PacketHeader>(nameof(PacketHeader.Kind)).ToInt32() == 0 &&
    Marshal.OffsetOf<PacketHeader>(nameof(PacketHeader.Length)).ToInt32() == 1 &&
    Marshal.OffsetOf<PacketHeader>(nameof(PacketHeader.Flags)).ToInt32() == 5 &&
    Marshal.OffsetOf<PacketHeader>(nameof(PacketHeader.Compressed)).ToInt32() == 7;

Console.WriteLine(valid ? "PASS" : "FAIL");
return valid ? 0 : 1;
