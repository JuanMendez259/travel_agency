using System.Collections.ObjectModel;

namespace TravelAgency.App.Modules.Client.ViewModels;

public class ObservableGroupCollection<TKey, TItem> : ObservableCollection<TItem>
{
    public TKey Key { get; }

    public ObservableGroupCollection(TKey key, IEnumerable<TItem> items)
        : base(items)
    {
        Key = key;
    }
}