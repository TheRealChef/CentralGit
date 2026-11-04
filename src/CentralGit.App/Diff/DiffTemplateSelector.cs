using CentralGit.Core.Diff;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CentralGit.App.Diff;

public sealed partial class DiffTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SplitTemplate { get; set; }
    public DataTemplate? UnifiedTemplate { get; set; }
    public DataTemplate? HunkTemplate { get; set; }
    public DataTemplate? GapTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        DiffRowViewModel { Kind: DiffRowKind.HunkHeader } => HunkTemplate,
        DiffRowViewModel { Kind: DiffRowKind.Gap } => GapTemplate,
        DiffRowViewModel { IsSplit: true } => SplitTemplate,
        _ => UnifiedTemplate,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
