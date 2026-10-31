using System.Windows;
using BulkPrint.Core.Infrastructure;
using BulkPrint.Core.Models;
using BulkPrint.Core.Services;
using BulkPrint.Core.ViewModels;

namespace BulkPrint.App.Views;

public sealed class DocumentSettingsModel
{
    public DocumentSettingsModel(DocumentPrintOptions options, bool supportsDuplex, bool supportsColor)
    {
        Options = options;
        SupportsDuplex = supportsDuplex;
        SupportsColor = supportsColor;
    }

    public DocumentPrintOptions Options { get; }
    public bool SupportsDuplex { get; }
    public bool SupportsColor { get; }
    public string? DuplexTip => SupportsDuplex ? null : "The selected printer does not support double-sided printing.";
    public string? ColorTip => SupportsColor ? null : "The selected printer prints in black and white only.";

    public IReadOnlyList<Choice<PageSelectionMode>> PageSelectionChoices { get; } = MainViewModel.Choices<PageSelectionMode>(EnumLabels.Of);
    public IReadOnlyList<Choice<OrientationMode>> OrientationChoices { get; } = MainViewModel.Choices<OrientationMode>(EnumLabels.Of);
    public IReadOnlyList<Choice<DuplexMode>> DuplexChoices { get; } = MainViewModel.Choices<DuplexMode>(EnumLabels.Of);
    public IReadOnlyList<Choice<ColorMode>> ColorChoices { get; } = MainViewModel.Choices<ColorMode>(EnumLabels.Of);
    public IReadOnlyList<Choice<ScalingMode>> ScalingChoices { get; } = MainViewModel.Choices<ScalingMode>(EnumLabels.Of);
}

public partial class DocumentSettingsWindow : Window
{
    private readonly DocumentSettingsModel _model;

    public DocumentSettingsWindow(int documentCount, DocumentPrintOptions initial, bool supportsDuplex, bool supportsColor)
    {
        InitializeComponent();
        _model = new DocumentSettingsModel(initial.CloneOptions(), supportsDuplex, supportsColor);
        DataContext = _model;
        HeaderText.Text = documentCount == 1 ? "Settings for this document" : $"Settings for {documentCount} selected documents";
        ApplyButton.Content = documentCount == 1 ? "Apply" : $"Apply to {documentCount} documents";
    }

    internal DocumentSettingsResult? Result { get; private set; }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        var error = _model.Options.Validate();
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Result = new DocumentSettingsResult(_model.Options.CloneOptions(), false);
        DialogResult = true;
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        Result = new DocumentSettingsResult(null, true);
        DialogResult = true;
    }
}
