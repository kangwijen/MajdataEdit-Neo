using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MajdataEdit_Neo.ViewModels;

namespace MajdataEdit_Neo.Views;

public partial class SoundSettingWindow : Window
{
    public SoundSettingWindow()
    {
        InitializeComponent();
    }

    private void ResetToDefaultsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.BgmLevel = 0.7f;
            viewModel.AnswerLevel = 0.7f;
            viewModel.JudgeLevel = 0.7f;
            viewModel.BreakLevel = 0.7f;
            viewModel.BreakSlideLevel = 0.7f;
            viewModel.SlideLevel = 0.7f;
            viewModel.ExLevel = 0.7f;
            viewModel.TouchLevel = 0.7f;
            viewModel.HanabiLevel = 0.7f;
            viewModel.SfxLatencyCompensation = 0.0545;
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}