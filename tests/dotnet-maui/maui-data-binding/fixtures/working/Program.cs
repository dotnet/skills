using System.ComponentModel;
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private string _theme = "Light";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Theme
    {
        get => _theme;
        set
        {
            if (_theme == value) return;
            _theme = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Theme)));
        }
    }
}
