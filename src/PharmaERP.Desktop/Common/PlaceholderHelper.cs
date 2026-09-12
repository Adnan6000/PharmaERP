using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PharmaERP.Desktop.Common;

/// <summary>
/// Attached property providing subtle, non-intrusive watermarks / placeholders
/// for TextBox and ComboBox controls via an AdornerLayer overlay.
/// Preserves native templates, focus, popup, selection, and keyboard navigation.
/// </summary>
public static class PlaceholderHelper
{
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached(
            "Placeholder",
            typeof(string),
            typeof(PlaceholderHelper),
            new FrameworkPropertyMetadata(string.Empty, OnPlaceholderChanged));

    public static string GetPlaceholder(DependencyObject obj) => (string)obj.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject obj, string value) => obj.SetValue(PlaceholderProperty, value);

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Control control)
        {
            control.Loaded -= OnControlLoaded;
            control.Loaded += OnControlLoaded;

            if (control.IsLoaded)
            {
                HookEvents(control);
                UpdateWatermark(control);
            }
        }
    }

    private static void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Control control)
        {
            HookEvents(control);
            UpdateWatermark(control);
        }
    }

    private static void HookEvents(Control control)
    {
        control.SizeChanged -= OnControlSizeChanged;
        control.SizeChanged += OnControlSizeChanged;
        control.DataContextChanged -= OnControlDataContextChanged;
        control.DataContextChanged += OnControlDataContextChanged;

        if (control is TextBox tb)
        {
            tb.TextChanged -= OnTextChanged;
            tb.TextChanged += OnTextChanged;
        }
        else if (control is ComboBox cb)
        {
            cb.SelectionChanged -= OnSelectionChanged;
            cb.SelectionChanged += OnSelectionChanged;
            cb.DropDownClosed -= OnDropDownClosed;
            cb.DropDownClosed += OnDropDownClosed;
        }
    }

    private static void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Control c) UpdateWatermark(c);
    }

    private static void OnControlDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is Control c) UpdateWatermark(c);
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is Control c) UpdateWatermark(c);
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is Control c) UpdateWatermark(c);
    }

    private static void OnDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is Control c) UpdateWatermark(c);
    }

    public static void UpdateWatermark(Control control)
    {
        var layer = AdornerLayer.GetAdornerLayer(control);
        if (layer == null)
        {
            control.Dispatcher?.BeginInvoke(new Action(() =>
            {
                var l = AdornerLayer.GetAdornerLayer(control);
                if (l != null) ApplyWatermark(control, l);
            }));
            return;
        }

        ApplyWatermark(control, layer);
    }

    private static void ApplyWatermark(Control control, AdornerLayer layer)
    {
        var adorners = layer.GetAdorners(control);
        if (adorners != null)
        {
            foreach (var a in adorners)
            {
                if (a is WatermarkAdorner)
                    layer.Remove(a);
            }
        }

        bool shouldShow = false;
        if (control is TextBox tb)
        {
            shouldShow = string.IsNullOrEmpty(tb.Text);
        }
        else if (control is ComboBox cb)
        {
            bool hasSelection = cb.SelectedItem != null || cb.SelectedValue != null || cb.SelectedIndex >= 0 || !string.IsNullOrWhiteSpace(cb.Text);
            shouldShow = !hasSelection;
        }

        string text = GetPlaceholder(control);
        if (shouldShow && !string.IsNullOrEmpty(text) && control.IsEnabled)
        {
            layer.Add(new WatermarkAdorner(control, text));
        }
    }

    private class WatermarkAdorner : Adorner
    {
        private readonly string _placeholder;
        private static readonly Brush PlaceholderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));

        static WatermarkAdorner()
        {
            PlaceholderBrush.Freeze();
        }

        public WatermarkAdorner(UIElement adornedElement, string placeholder)
            : base(adornedElement)
        {
            _placeholder = placeholder;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (AdornedElement is not Control control) return;

            var typeface = new Typeface(
                control.FontFamily,
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);

            var formattedText = new FormattedText(
                _placeholder,
                System.Globalization.CultureInfo.CurrentCulture,
                control.FlowDirection,
                typeface,
                control.FontSize,
                PlaceholderBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            double left = control is ComboBox ? 8 : (control.Padding.Left + 4);
            double top = Math.Max(0, (control.ActualHeight - formattedText.Height) / 2);

            if (control.FlowDirection == FlowDirection.RightToLeft)
            {
                if (control is ComboBox)
                {
                    left = Math.Max(0, control.ActualWidth - formattedText.Width - 28);
                }
                else
                {
                    left = Math.Max(0, control.ActualWidth - formattedText.Width - control.Padding.Right - 4);
                }
            }

            drawingContext.DrawText(formattedText, new Point(left, top));
        }
    }
}

