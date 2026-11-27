using Bunit;
using Notebook.Components;
using Xunit;

public sealed class NoteBoxTests : BunitContext
{
    [Fact]
    public void Text_area_has_a_label()
    {
        var editor = Render<NoteBox>();

        Assert.Equal("Draft note", editor.Find("label[for=draft]").TextContent);
        Assert.Equal("draft", editor.Find("textarea").Id);
    }
}
