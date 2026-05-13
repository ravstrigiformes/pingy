using CommunityToolkit.Mvvm.ComponentModel;

namespace Pingy.Widget.ViewModels;

public sealed partial class TagChipViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isSelected;

    public TagChipViewModel(string name, bool selected = false)
    {
        Name = name;
        IsSelected = selected;
    }
}
