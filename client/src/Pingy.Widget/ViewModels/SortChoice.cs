using System.ComponentModel;

namespace Pingy.Widget.ViewModels;

public sealed record SortChoice(string Label, string PropertyName, ListSortDirection Direction)
{
    public override string ToString() => Label;
    public bool IsDefault => string.IsNullOrEmpty(PropertyName);
}
