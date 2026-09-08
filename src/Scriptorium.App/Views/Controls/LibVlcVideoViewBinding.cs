using System.Windows;
using LibVLCSharp.WPF;
using Scriptorium.App.Services;

namespace Scriptorium.App.Views.Controls;

/// <summary>Confines the LibVLCSharp WPF rendering type to the view layer.</summary>
public static class LibVlcVideoViewBinding
{
    public static readonly DependencyProperty OutputProperty = DependencyProperty.RegisterAttached(
        "Output",
        typeof(IVideoOutput),
        typeof(LibVlcVideoViewBinding),
        new PropertyMetadata(null, OnOutputChanged));

    public static IVideoOutput? GetOutput(DependencyObject target) =>
        (IVideoOutput?)target.GetValue(OutputProperty);

    public static void SetOutput(DependencyObject target, IVideoOutput? value) =>
        target.SetValue(OutputProperty, value);

    private static void OnOutputChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is VideoView videoView)
        {
            videoView.MediaPlayer = (args.NewValue as LibVlcVideoOutput)?.MediaPlayer;
        }
    }
}
