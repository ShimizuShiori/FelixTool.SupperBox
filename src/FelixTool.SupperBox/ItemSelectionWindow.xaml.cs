using System.Windows;
using System.Windows.Input;

namespace FelixTool.SupperBox;

public partial class ItemSelectionWindow : Window
{
    public ItemSelectionWindow(string title, IReadOnlyList<ItemSelectionEntry> items)
    {
        InitializeComponent();
        TitleText.Text = title;
        ItemList.ItemsSource = items;
        ItemList.SelectedIndex = 0;
        Loaded += (_, _) => ItemList.Focus();
    }

    public object? SelectedValue { get; private set; }

    public void Cancel()
    {
        if (IsVisible)
        {
            DialogResult = false;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ConfirmSelection();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private void ItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (ItemList.SelectedItem is ItemSelectionEntry entry)
        {
            SelectedValue = entry.Value;
            DialogResult = true;
        }
    }
}
