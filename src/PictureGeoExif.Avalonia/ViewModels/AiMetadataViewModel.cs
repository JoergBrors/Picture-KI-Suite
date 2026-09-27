using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureGeoExif.Application;
using PictureGeoExif.Application.Ai;
using PictureGeoExif.Desktop.Services;
using PictureGeoExif.Metadata.Ai;

namespace PictureGeoExif.Desktop.ViewModels;

/// <summary>
/// AI metadata window. All logic (budget, privacy, provider calls, sidecar writing, undo, chat) lives in
/// <see cref="AiMetadataWorkflow"/>; this class only maps it to bindable state and asks the user via dialogs.
/// </summary>
public sealed partial class AiMetadataViewModel : ObservableObject, IAiWorkflowPrompts
{
    private readonly AiMetadataWorkflow workflow;
    private readonly IDialogService dialogs;
    private readonly IStorageService storage;
    private readonly AppSettings settings;
    private readonly AppPaths paths;
    private CancellationTokenSource? run;

    public AiMetadataViewModel(AiMetadataWorkflow workflow, IDialogService dialogs, IStorageService storage, AppSettings settings, AppPaths paths)
    {
        this.workflow = workflow;
        this.dialogs = dialogs;
        this.storage = storage;
        this.settings = settings;
        this.paths = paths;
        workflow.LoadInitialTemplate();
        AfterTemplateLoaded();
        foreach (var row in workflow.Rows) row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AiImageRow.Status) && FilterIndex != 0) Refilter(); };
        Refilter();
    }

    public ObservableCollection<AiImageRow> Rows => workflow.Rows;
    public ObservableCollection<AiImageRow> VisibleRows { get; } = [];
    public static IReadOnlyList<string> Filters { get; } = ["Alle", "Mit Vorschlägen", "Fehler/Unklar", "Entscheidung nötig"];
    public static IReadOnlyList<string> Seasons { get; } = AiMetadataWorkflow.Seasons;

    [ObservableProperty] private int filterIndex;
    [ObservableProperty] private AiImageRow? selectedRow;
    [ObservableProperty] private Bitmap? preview;
    [ObservableProperty] private string templateText = "";
    [ObservableProperty] private string templateStatus = "";
    [ObservableProperty] private IReadOnlyList<AiProviderProfile> providers = [];
    [ObservableProperty] private AiProviderProfile? selectedProvider;
    [ObservableProperty] private string keyInput = "";
    [ObservableProperty] private string keyStatus = "";
    [ObservableProperty] private string priceInput = "";
    [ObservableProperty] private string priceCached = "";
    [ObservableProperty] private string priceOutput = "";
    [ObservableProperty] private DateTimeOffset? priceDate;
    [ObservableProperty] private string tokenEstimate = "3000";
    [ObservableProperty] private string budgetText = "";
    [ObservableProperty] private string status = "Zuerst „Lokal prüfen“ (kostenlos), dann Test mit 3 Bildern oder Start.";
    [ObservableProperty] private double progress;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string chatInput = "";
    [ObservableProperty] private string chatLog = "";
    [ObservableProperty] private string manualSeason = AiMetadataWorkflow.Seasons[0];

    public string CredentialStoreName => workflow.Credentials.DisplayName;

    partial void OnFilterIndexChanged(int value) => Refilter();

    private void Refilter()
    {
        var selected = SelectedRow;
        VisibleRows.Clear();
        foreach (var row in Rows.Where(r => FilterIndex switch
        {
            1 => r.Changes.Count > 0,
            2 => r.Status.StartsWith("Fehler", StringComparison.Ordinal) || r.Status.StartsWith("Unklar", StringComparison.Ordinal),
            3 => r.Preference == PreferenceState.PresentUndecoded,
            _ => true
        })) VisibleRows.Add(row);
        if (selected != null && VisibleRows.Contains(selected)) SelectedRow = selected;
    }

    partial void OnSelectedRowChanged(AiImageRow? value) => _ = LoadPreviewAsync(value);

    private async Task LoadPreviewAsync(AiImageRow? row)
    {
        Preview = null;
        if (row == null) return;
        try { Preview = await Task.Run(() => { using var s = File.OpenRead(row.FilePath); return Bitmap.DecodeToWidth(s, 900); }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { Preview = null; }
    }

    // ---------- Template ----------

    private void AfterTemplateLoaded()
    {
        TemplateStatus = workflow.TemplateStatus;
        if (workflow.Template is not { } template) return;
        TemplateText = template.Json;
        string? previous = SelectedProvider?.Id;
        Providers = template.Providers;
        SelectedProvider = Providers.FirstOrDefault(p => p.Id == (previous ?? template.SelectedProvider)) ?? Providers.FirstOrDefault();
        BudgetText = workflow.BudgetText;
    }

    [RelayCommand]
    private void ApplyTemplate()
    {
        try { workflow.LoadTemplate(TemplateText, settings.AiTemplatePath); AfterTemplateLoaded(); }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException) { TemplateStatus = "Ungültig, nicht übernommen: " + ex.Message; }
    }

    [RelayCommand]
    private async Task LoadTemplateAsync()
    {
        string start = Directory.Exists(paths.UserTemplatesFolder) ? paths.UserTemplatesFolder : workflow.BaseTemplateFolder;
        var file = await storage.PickJsonAsync("KI-Vorlage laden", start);
        if (file == null) return;
        try { workflow.UseTemplateFile(file); AfterTemplateLoaded(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { TemplateStatus = "Nicht geladen: " + ex.Message; }
    }

    [RelayCommand]
    private void SaveTemplate()
    {
        try { workflow.SaveTemplateVersion(TemplateText); AfterTemplateLoaded(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { TemplateStatus = "Nicht gespeichert: " + ex.Message; }
    }

    [RelayCommand]
    private void DefaultTemplate()
    {
        try { workflow.UseDefaultTemplate(); AfterTemplateLoaded(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { TemplateStatus = ex.Message; }
    }

    // ---------- Provider, keys, prices ----------

    partial void OnSelectedProviderChanged(AiProviderProfile? value)
    {
        if (value == null) return;
        var price = workflow.StoredPrices(value);
        PriceInput = Format(price?.Input); PriceCached = Format(price?.Cached); PriceOutput = Format(price?.Output);
        PriceDate = price?.VerifiedDate is { } d ? new DateTimeOffset(d) : null;
        KeyStatus = workflow.KeyStatus(value);
        static string Format(decimal? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    [RelayCommand]
    private void SaveKey()
    {
        if (SelectedProvider is not { } profile) return;
        KeyStatus = workflow.SaveKey(profile, KeyInput);
        KeyInput = "";
    }

    [RelayCommand]
    private void DeleteKey()
    {
        if (SelectedProvider is { } profile) KeyStatus = workflow.DeleteKey(profile);
    }

    private AiPrices? Prices(out string reason)
    {
        reason = "Kein Anbieter gewählt.";
        return SelectedProvider is { } profile
            ? workflow.VerifyPrices(profile, PriceInput, PriceCached, PriceOutput, PriceDate?.Date, out reason)
            : null;
    }

    // ---------- Actions ----------

    [RelayCommand] private void SelectAll() { foreach (var r in Rows) r.Selected = true; }
    [RelayCommand] private void SelectNone() { foreach (var r in Rows) r.Selected = false; }

    [RelayCommand]
    private async Task CheckAsync()
    {
        IsRunning = true;
        try { Status = await workflow.CheckAsync(Rows.Where(r => r.Selected).ToList(), new Progress<double>(p => Progress = p)); }
        finally { IsRunning = false; Refilter(); }
    }

    [RelayCommand] private Task TestAsync() => AnalyzeAsync(3);
    [RelayCommand] private Task StartAsync() => AnalyzeAsync(int.MaxValue);

    private async Task AnalyzeAsync(int limit)
    {
        if (SelectedProvider is not { } profile) { Status = "Kein Anbieter gewählt."; return; }
        var prices = Prices(out string reason);
        if (prices == null) { Status = "Start blockiert: " + reason; return; }
        int tokens = int.TryParse(TokenEstimate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : 3000;
        run = new CancellationTokenSource();
        IsRunning = true;
        try { Status = await workflow.AnalyzeAsync(profile, prices, tokens, limit, this, new Progress<double>(p => Progress = p), run.Token); }
        finally
        {
            run.Dispose(); run = null;
            IsRunning = false;
            BudgetText = workflow.BudgetText;
            Refilter();
        }
    }

    [RelayCommand] private void Cancel() => run?.Cancel();

    [RelayCommand]
    private void ProposeSeason()
    {
        int count = workflow.ProposeSeason(ManualSeason);
        Status = $"Jahreszeit „{ManualSeason}“ für {count} Bild(er) vorgeschlagen – noch nicht gespeichert.";
        Refilter();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        int fields = Rows.Where(r => r.Selected).Sum(r => r.Changes.Count(c => c.Accept));
        int files = Rows.Count(r => r.Selected && r.Changes.Any(c => c.Accept));
        if (files == 0) { Status = "Keine angehakten Änderungen bei markierten Bildern."; return; }
        if (!await dialogs.ConfirmAsync("Änderungen speichern", $"{fields} Änderung(en) in {files} XMP-Sidecar-Datei(en) schreiben?\nDie Bilddateien selbst werden nicht verändert.")) return;
        var (written, _, text) = workflow.Apply();
        Status = text;
        Refilter();
        if (written > 0 && await dialogs.ConfirmAsync("Gespeichert", text + "\n\nJetzt rückgängig machen?", "Rückgängig", "Behalten"))
            Status = workflow.UndoLastWrite();
    }

    [RelayCommand]
    private void Undo()
    {
        if (workflow.CanUndo) Status = workflow.UndoLastWrite();
    }

    [RelayCommand]
    private async Task SendChatAsync()
    {
        string message = ChatInput.Trim();
        if (message.Length == 0 || SelectedProvider is not { } profile) return;
        var prices = Prices(out string reason);
        if (prices == null) { Status = "Chat blockiert: " + reason; return; }
        ChatInput = "";
        ChatLog += $"Sie: {message}\n";
        IsRunning = true;
        try
        {
            var result = await workflow.ChatAsync(message, profile, prices, CancellationToken.None);
            ChatLog += $"Assistent: {result.AssistantText}\n";
            foreach (var note in result.Notes) ChatLog += note + "\n";
            if (result.TemplateDraft != null) { TemplateText = result.TemplateDraft; TemplateStatus = result.TemplateDraftStatus ?? ""; }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ChatLog += $"Fehler: {ex.Message}\n"; }
        finally { IsRunning = false; BudgetText = workflow.BudgetText; Refilter(); }
    }

    public bool HasUnsavedProposals => workflow.HasUnsavedModelProposals;
    public bool IsAnalysisRunning => run != null;
    public void CancelRun() => run?.Cancel();
    public void SaveSettings() => settings.Save();

    Task<bool> IAiWorkflowPrompts.ConfirmAsync(string title, string message) => dialogs.ConfirmAsync(title, message);
}
