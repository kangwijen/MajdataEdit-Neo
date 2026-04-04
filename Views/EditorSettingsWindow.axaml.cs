using Avalonia.Controls;
using Avalonia.Interactivity;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.ViewModels;

namespace MajdataEdit_Neo.Views;

public partial class EditorSettingsWindow : Window
{
    public EditorSettingsWindow()
    {
        InitializeComponent();
    }

    private void ResetToDefaultButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.EditorFontSize = new EditorSetting().FontSize;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
