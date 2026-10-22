using System.ComponentModel;
namespace CustomerBinding;

public sealed class CustomerViewModel : INotifyPropertyChanged
{
    private string _name = "Ada Lovelace";

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
