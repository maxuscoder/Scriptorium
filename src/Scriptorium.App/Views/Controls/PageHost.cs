using System.Windows;
using System.Windows.Controls;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Controls;

/// <summary>Retains the virtualized library view across navigation, including its viewport and decoded artwork.</summary>
public sealed class PageHost : Grid
{
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(PageViewModel), typeof(PageHost), new PropertyMetadata(null, OnContentChanged));
    public static readonly DependencyProperty ContentTemplateProperty = DependencyProperty.Register(
        nameof(ContentTemplate), typeof(DataTemplate), typeof(PageHost), new PropertyMetadata(null, OnTemplateChanged));
    private ContentPresenter? _library;
    private ContentPresenter? _active;

    public PageViewModel? Content
    {
        get => (PageViewModel?)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }
    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    private static void OnContentChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) =>
        ((PageHost)target).ShowPage((PageViewModel?)args.NewValue);

    private static void OnTemplateChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        foreach (ContentPresenter presenter in ((PageHost)target).Children)
            presenter.ContentTemplate = (DataTemplate?)args.NewValue;
    }

    private void ShowPage(PageViewModel? page)
    {
        if (_active is not null)
        {
            if (ReferenceEquals(_active, _library))
                // Preserve viewport geometry as well as controls, so return does not remeasure the browser.
                _active.Visibility = Visibility.Hidden;
            else Children.Remove(_active); // Details views must unload and release playback.
            _active = null;
        }
        if (page is null)
        {
            Children.Clear();
            _library = null;
            return;
        }
        if (page is LibraryPageViewModel && _library is not null && !ReferenceEquals(_library.Content, page))
        {
            Children.Remove(_library);
            _library = null;
        }
        _active = page is LibraryPageViewModel && _library is not null
            ? _library : new ContentPresenter { Content = page, ContentTemplate = ContentTemplate };
        if (page is LibraryPageViewModel) _library = _active;
        if (!Children.Contains(_active)) Children.Add(_active);
        _active.Visibility = Visibility.Visible;
        SetZIndex(_active, 1);
    }
}
