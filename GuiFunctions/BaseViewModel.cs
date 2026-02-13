using System.ComponentModel;

namespace GuiFunctions;

/// <summary>
/// A base view model that fires Property Changed events as needed
/// </summary>
public class BaseViewModel : INotifyPropertyChanged
{
    public BaseViewModel() { }
    public BaseViewModel(BaseViewModel? root)
    {
        Root = root;
    }

    protected BaseViewModel? Root { get; init; }

    /// <summary>
    /// The event that is fired when any child property changes its value
    /// </summary>
    public event PropertyChangedEventHandler PropertyChanged = (sender, e) => { };

    /// <summary>
    /// Call this to fire a <see cref="PropertyChanged"/> event
    /// </summary>
    /// <param name="name"></param>
    public void OnPropertyChanged(string name)
    {
        PropertyChanged(this, new PropertyChangedEventArgs(name));
    }
}
