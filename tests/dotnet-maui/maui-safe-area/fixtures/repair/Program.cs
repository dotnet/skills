using Microsoft.Maui;
using Microsoft.Maui.Controls;
public static class ChatLayout
{
    public static Grid Create() => new Grid
    {
        Spacing = 10,
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container, SafeAreaRegions.Container,
            SafeAreaRegions.Container, SafeAreaRegions.SoftInput)
    };
}
