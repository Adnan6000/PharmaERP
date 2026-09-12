using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PharmaERP.Desktop.Common;

/// <summary>
/// Attached behavior providing safe, binding-preserving clear capability for ComboBox controls.
/// Supports keyboard (Delete/Escape when dropdown closed), an optional overlay clear button,
/// and preserves the underlying BindingExpression via SetCurrentValue.
/// </summary>
public static class ClearableHelper
{
    public static readonly DependencyProperty IsClearableProperty =
        DependencyProperty.RegisterAttached(
            "IsClearable",
            typeof(bool),
            typeof(ClearableHelper),
            new PropertyMetadata(false, OnIsClearableChanged));

    public static bool GetIsClearable(DependencyObject obj) => (bool)obj.GetValue(IsClearableProperty);
    public static void SetIsClearable(DependencyObject obj, bool value) => obj.SetValue(IsClearableProperty, value);

    private static void OnIsClearableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox cb)
        {
            cb.Loaded -= OnComboBoxLoaded;
            cb.PreviewKeyDown -= OnComboBoxPreviewKeyDown;

            if ((bool)e.NewValue)
            {
                cb.Loaded += OnComboBoxLoaded;
                cb.PreviewKeyDown += OnComboBoxPreviewKeyDown;
                if (cb.IsLoaded)
                {
                    AttachClearAdorner(cb);
                }
            }
            else
            {
                RemoveClearAdorner(cb);
            }
        }
    }

    private static void OnComboBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb && GetIsClearable(cb))
        {
            AttachClearAdorner(cb);
        }
    }

    private static void OnComboBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is ComboBox cb && GetIsClearable(cb))
        {
            if (!cb.IsDropDownOpen && (e.Key == Key.Delete || e.Key == Key.Escape))
            {
                if (HasSelection(cb))
                {
                    ClearSelection(cb);
                    e.Handled = true;
                }
            }
        }
    }

    public static bool HasSelection(ComboBox cb)
    {
        if (cb.SelectedItem != null) return true;
        if (cb.SelectedValue != null) return true;
        if (cb.SelectedIndex >= 0) return true;
        if (!string.IsNullOrWhiteSpace(cb.Text)) return true;
        return false;
    }

    public static void ClearSelection(ComboBox cb)
    {
        // Use SetCurrentValue to preserve TwoWay BindingExpressions
        var itemBinding = BindingOperations.GetBindingExpression(cb, ComboBox.SelectedItemProperty);
        if (itemBinding != null)
        {
            cb.SetCurrentValue(ComboBox.SelectedItemProperty, null);
        }

        var valueBinding = BindingOperations.GetBindingExpression(cb, ComboBox.SelectedValueProperty);
        if (valueBinding != null)
        {
            cb.SetCurrentValue(ComboBox.SelectedValueProperty, null);
        }

        if (itemBinding == null && valueBinding == null)
        {
            cb.SetCurrentValue(ComboBox.SelectedIndexProperty, -1);
            cb.SetCurrentValue(ComboBox.SelectedItemProperty, null);
            cb.SetCurrentValue(ComboBox.SelectedValueProperty, null);
        }

        if (cb.IsEditable)
        {
            cb.SetCurrentValue(ComboBox.TextProperty, string.Empty);
        }

        PlaceholderHelper.UpdateWatermark(cb);
        UpdateAdorner(cb);
    }

    private static void AttachClearAdorner(ComboBox cb)
    {
        cb.SelectionChanged -= OnSelectionChanged;
        cb.SelectionChanged += OnSelectionChanged;
        cb.SizeChanged -= OnSizeChanged;
        cb.SizeChanged += OnSizeChanged;

        UpdateAdorner(cb);
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb) UpdateAdorner(cb);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ComboBox cb) UpdateAdorner(cb);
    }

    public static void UpdateAdorner(ComboBox cb)
    {
        var layer = AdornerLayer.GetAdornerLayer(cb);
        if (layer == null)
        {
            cb.Dispatcher?.BeginInvoke(new Action(() =>
            {
                var l = AdornerLayer.GetAdornerLayer(cb);
                if (l != null) ApplyAdorner(cb, l);
            }));
            return;
        }

        ApplyAdorner(cb, layer);
    }

    private static void ApplyAdorner(ComboBox cb, AdornerLayer layer)
    {
        var adorners = layer.GetAdorners(cb);
        if (adorners != null)
        {
            foreach (var a in adorners)
            {
                if (a is ClearButtonAdorner)
                    layer.Remove(a);
            }
        }

        if (GetIsClearable(cb) && HasSelection(cb) && cb.IsEnabled)
        {
            layer.Add(new ClearButtonAdorner(cb));
        }
    }

    private static void RemoveClearAdorner(ComboBox cb)
    {
        var layer = AdornerLayer.GetAdornerLayer(cb);
        if (layer != null)
        {
            var adorners = layer.GetAdorners(cb);
            if (adorners != null)
            {
                foreach (var a in adorners)
                {
                    if (a is ClearButtonAdorner)
                        layer.Remove(a);
                }
            }
        }
    }

    private class ClearButtonAdorner : Adorner
    {
        private readonly ComboBox _comboBox;
        private readonly Button _clearButton;
        private readonly VisualCollection _visualChildren;

        public ClearButtonAdorner(ComboBox adornedElement) : base(adornedElement)
        {
            _comboBox = adornedElement;
            _visualChildren = new VisualCollection(this);

            _clearButton = new Button
            {
                Content = "\u2715",
                Width = 16,
                Height = 16,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                BorderThickness = new Thickness(0),
                Focusable = false,
                ToolTip = "Clear selection (Delete)"
            };

            // Accessibility: screen readers will announce this as "Clear selection"
            System.Windows.Automation.AutomationProperties.SetName(_clearButton, "Clear selection");
            System.Windows.Automation.AutomationProperties.SetHelpText(_clearButton, "Clears the current ComboBox selection. Keyboard shortcut: Delete or Escape when dropdown is closed.");

            _clearButton.Click += (s, e) =>
            {
                e.Handled = true;
                ClearSelection(_comboBox);
            };

            _visualChildren.Add(_clearButton);
        }

        protected override int VisualChildrenCount => _visualChildren.Count;
        protected override Visual GetVisualChild(int index) => _visualChildren[index];

        protected override Size ArrangeOverride(Size finalSize)
        {
            double btnWidth = 16;
            double btnHeight = 16;
            double y = (finalSize.Height - btnHeight) / 2;

            double x;
            if (_comboBox.FlowDirection == FlowDirection.RightToLeft)
            {
                x = 22; // In RTL, dropdown toggle button is on left
            }
            else
            {
                x = Math.Max(0, finalSize.Width - 36); // Left of the native dropdown arrow
            }

            _clearButton.Arrange(new Rect(new Point(x, y), new Size(btnWidth, btnHeight)));
            return finalSize;
        }
    }
}

