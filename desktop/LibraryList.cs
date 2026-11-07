using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ritmo;

// Keep data for the whole page, but create track controls only around the viewport.
// The list owns scrolling: wrapping this in another ScrollViewer disables virtualization.
internal sealed class LibraryList : ListBox
{
    public Func<Track, FrameworkElement>? CreateRow { get; set; }
    public ItemCollection Children => Items;
    public LibraryList()
    {
        Background = Brushes.Transparent; BorderThickness = new Thickness(0);
        Padding = new Thickness(0, 0, 12, 20);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetCanContentScroll(this, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Auto);
        VirtualizingPanel.SetIsVirtualizing(this, true);
        VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(this, ScrollUnit.Pixel);
        VirtualizingPanel.SetCacheLength(this, new VirtualizationCacheLength(1));
        VirtualizingPanel.SetCacheLengthUnit(this, VirtualizationCacheLengthUnit.Page);
        ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        ItemContainerStyle = new Style(typeof(ListBoxItem));
        ItemContainerStyle.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = presenter }));
        ItemContainerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        ItemContainerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        ItemContainerStyle.Setters.Add(new Setter(FocusableProperty, false));
    }
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is ListBoxItem container && item is Track track && CreateRow != null)
            container.Content = CreateRow(track);
    }
    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is ListBoxItem container) container.Content = null;
        base.ClearContainerForItemOverride(element, item);
    }
}
