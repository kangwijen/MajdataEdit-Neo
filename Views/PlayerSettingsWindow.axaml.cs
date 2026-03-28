using Avalonia.Controls;
using Avalonia.Interactivity;
using MajdataEdit_Neo.Models;
using MajdataEdit_Neo.ViewModels;

namespace MajdataEdit_Neo.Views;

public partial class PlayerSettingsWindow : Window
{
    public PlayerSettingsWindow()
    {
        InitializeComponent();
    }

    private void ResetToDefaultsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var d = new EditorSetting();
        vm.NoteSpeed = d.playSpeed;
        vm.TouchSpeed = d.touchSpeed;
        vm.CenterDisplayMode = d.comboStatusType == EditorComboIndicator.Combo ? 1 : 0;
        vm.PlayModeIndex = d.editorPlayMethod == EditorPlayMethod.DJAuto ? 1 : 0;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
