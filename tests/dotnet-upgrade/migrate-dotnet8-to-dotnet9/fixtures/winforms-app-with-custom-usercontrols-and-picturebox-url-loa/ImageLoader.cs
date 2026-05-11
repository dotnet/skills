using System.Net;
using System.Windows.Forms;
class ImageLoader
{
    void LoadImage(PictureBox pictureBox, string url)
    {
        try { pictureBox.Load(url); }
        catch (WebException ex) { Console.WriteLine("Failed: " + ex.Message); }
    }
}
