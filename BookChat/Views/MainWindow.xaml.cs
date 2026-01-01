using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BookChat.ViewModels;

namespace BookChat.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Disconnect from IRC when window is closing
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Cleanup();
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Set initial password value from ViewModel
        if (DataContext is MainWindowViewModel viewModel)
        {
            CalibrePasswordBox.Password = viewModel.CalibrePassword;
        }
    }

    private void CalibrePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // Update ViewModel when password changes
        if (DataContext is MainWindowViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.CalibrePassword = passwordBox.Password;
        }
    }
}

/// <summary>
/// Attached property for auto-scrolling ListBox to bottom
/// </summary>
public static class AutoScrollBehavior
{
    public static readonly DependencyProperty AutoScrollProperty =
        DependencyProperty.RegisterAttached(
            "AutoScroll",
            typeof(bool),
            typeof(AutoScrollBehavior),
            new PropertyMetadata(false, AutoScrollPropertyChanged));

    public static bool GetAutoScroll(DependencyObject obj)
    {
        return (bool)obj.GetValue(AutoScrollProperty);
    }

    public static void SetAutoScroll(DependencyObject obj, bool value)
    {
        obj.SetValue(AutoScrollProperty, value);
    }

    private static void AutoScrollPropertyChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        if (obj is not ListBox listBox)
            return;

        if ((bool)e.NewValue)
        {
            listBox.Loaded += ListBox_Loaded;
            listBox.Unloaded += ListBox_Unloaded;
        }
        else
        {
            listBox.Loaded -= ListBox_Loaded;
            listBox.Unloaded -= ListBox_Unloaded;
        }
    }

    private static void ListBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.ItemsSource is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += (s, args) => OnCollectionChanged(listBox, args);
        }
    }

    private static void ListBox_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.ItemsSource is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged -= (s, args) => OnCollectionChanged(listBox, args);
        }
    }

    private static void OnCollectionChanged(ListBox listBox, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Reset)
        {
            listBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (listBox.Items.Count > 0)
                {
                    var lastItem = listBox.Items[listBox.Items.Count - 1];
                    listBox.ScrollIntoView(lastItem);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}

/// <summary>
/// Converts a boolean value to its inverse
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

/// <summary>
/// Converts a boolean value to Visibility (inverse of BooleanToVisibilityConverter)
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return true;
    }
}