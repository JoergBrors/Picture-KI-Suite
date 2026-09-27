using Avalonia.Controls;
using Avalonia.Input;
using PictureGeoExif.Desktop.Services;
using PictureGeoExif.Desktop.ViewModels;

namespace PictureGeoExif.Desktop.Views;

public partial class AiMetadataWindow : Window
{
    private bool confirmedClose;

    public AiMetadataWindow() => InitializeComponent();

    private AiMetadataViewModel? ViewModel => DataContext as AiMetadataViewModel;

    private void ChatInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel is { } vm) { vm.SendChatCommand.Execute(null); e.Handled = true; }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (confirmedClose || ViewModel is not { } vm) return;
        string? question = vm.IsAnalysisRunning
            ? "Die Analyse läuft noch. Laufende Anfragen abbrechen und Fenster schließen?\nBereits gesendete Anfragen können abgerechnet werden."
            : vm.HasUnsavedProposals ? "Ungespeicherte KI-/Chat-Vorschläge verwerfen?" : null;
        if (question == null) { vm.SaveSettings(); return; }
        e.Cancel = true;
        if (await new MessageDialog("KI-Metadaten", question, "Schließen", "Abbrechen").ShowDialog<bool>(this))
        {
            vm.CancelRun();
            vm.SaveSettings();
            confirmedClose = true;
            Close();
        }
    }
}
