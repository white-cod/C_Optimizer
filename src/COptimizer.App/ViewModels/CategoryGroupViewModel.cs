using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using COptimizer.Core;
using COptimizer.Core.Model;
using Wpf.Ui.Controls;

namespace COptimizer.App.ViewModels;

/// <summary>A category section in the cleanup list (Windows, Browsers, Graphics drivers, ...).</summary>
public sealed partial class CategoryGroupViewModel : ObservableObject
{
    public CategoryGroupViewModel(CleanerCategory category, IEnumerable<CleanupItemViewModel> items)
    {
        Category = category;
        foreach (var item in items.OrderByDescending(i => i.TotalBytes))
            Items.Add(item);
    }

    public CleanerCategory Category { get; }

    public string Title => Category switch
    {
        CleanerCategory.System => "Windows",
        CleanerCategory.WindowsUpdate => "Windows Update",
        CleanerCategory.Browsers => "Browsers",
        CleanerCategory.Gpu => "Graphics drivers",
        CleanerCategory.Games => "Game launchers",
        CleanerCategory.Applications => "Applications",
        CleanerCategory.Developer => "Developer tools",
        _ => "Other",
    };

    public SymbolRegular Icon => Category switch
    {
        CleanerCategory.System => SymbolRegular.Window24,
        CleanerCategory.WindowsUpdate => SymbolRegular.ArrowSync24,
        CleanerCategory.Browsers => SymbolRegular.Globe24,
        CleanerCategory.Gpu => SymbolRegular.DeveloperBoard24,
        CleanerCategory.Games => SymbolRegular.Games24,
        CleanerCategory.Applications => SymbolRegular.Apps24,
        CleanerCategory.Developer => SymbolRegular.Code24,
        _ => SymbolRegular.Folder24,
    };

    /// <summary>Items sorted by size, largest first.</summary>
    public ObservableCollection<CleanupItemViewModel> Items { get; } = [];

    public long TotalBytes => Items.Sum(r => r.TotalBytes);

    public string SizeText => ByteSize.Format(TotalBytes);

    /// <summary>Tri-state: true = all selected, false = none, null = some.</summary>
    public bool? IsSelected
    {
        get
        {
            var selected = Items.Count(r => r.IsSelected);
            return selected == 0 ? false : selected == Items.Count ? true : null;
        }
        set
        {
            // Each item reports its change to the cleanup view model, which refreshes totals and group state.
            foreach (var item in Items)
                item.IsSelected = value == true;
        }
    }

    /// <summary>Inserts an item keeping the size order (used when a slow system scan finishes).</summary>
    internal void Insert(CleanupItemViewModel item)
    {
        var index = 0;
        while (index < Items.Count && Items[index].TotalBytes >= item.TotalBytes)
            index++;
        Items.Insert(index, item);
        OnPropertyChanged(nameof(TotalBytes));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(IsSelected));
    }

    internal void RaiseSelectionChanged() => OnPropertyChanged(nameof(IsSelected));
}
