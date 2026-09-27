using System.Text.Json.Nodes;
using PictureGeoExif.Application.Ai;
using PictureGeoExif.Application.Diagnostics;
using PictureGeoExif.Application.Platform;
using PictureGeoExif.Core.Photos;
using PictureGeoExif.Core.Security;
using PictureGeoExif.Metadata.Ai;
using PictureGeoExif.Metadata.Decoding;

namespace PictureGeoExif.Application.Tests;

public sealed class AiWorkflowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pge-aiwf-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths paths;
    private readonly AppSettings settings;

    public AiWorkflowTests()
    {
        paths = new AppPaths { SettingsFolder = Path.Combine(root, "s"), DataFolder = Path.Combine(root, "d"), CacheFolder = Path.Combine(root, "c"), LogFolder = Path.Combine(root, "l") };
        settings = new AppSettings { FilePath = Path.Combine(root, "settings.json") };
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private AiMetadataWorkflow Workflow()
    {
        var workflow = new AiMetadataWorkflow(settings, paths, new NoCredentialStore(), Path.Combine(AppContext.BaseDirectory, "Templates"));
        workflow.LoadInitialTemplate();
        return workflow;
    }

    private sealed class Prompts(bool answer) : IAiWorkflowPrompts
    {
        public int Asked { get; private set; }
        public Task<bool> ConfirmAsync(string title, string message) { Asked++; return Task.FromResult(answer); }
    }

    [Fact]
    public void DefaultTemplate_Loads_AndPricesNeedDate()
    {
        var workflow = Workflow();
        Assert.NotNull(workflow.Template);
        var profile = workflow.Template!.Providers[0];
        Assert.Null(workflow.VerifyPrices(profile, "1", "0.1", "10", null, out var reason));
        Assert.Contains("Prüfdatum", reason, StringComparison.Ordinal);
        Assert.Null(workflow.VerifyPrices(profile, "0", "0", "10", DateTime.Today, out _));
        Assert.NotNull(workflow.VerifyPrices(profile, "1,5", "0.1", "10", DateTime.Today, out _));
        Assert.Equal(1.5m, settings.AiPrices[profile.Id].Input);
    }

    [Fact]
    public async Task Check_ProposesLocalExifMappings_Apply_And_Undo()
    {
        var workflow = Workflow();
        string image = TestImages.Jpeg(root);
        workflow.AddImages([image]);
        await workflow.CheckAsync(workflow.Rows.ToList());
        var row = workflow.Rows.Single();
        Assert.Contains(row.Changes, c => c.Target == "XMP.exif:DateTimeOriginal");

        var (written, total, _) = workflow.Apply();
        Assert.Equal((1, 1), (written, total));
        Assert.True(File.Exists(AiMetadataService.SidecarPath(image)));
        Assert.True(File.Exists(Path.Combine(paths.AuditFolder, "audit.jsonl")));
        workflow.UndoLastWrite();
        Assert.False(File.Exists(AiMetadataService.SidecarPath(image)));
    }

    [Fact]
    public async Task Analyze_WithoutMissingFields_MakesNoCall_AndBudgetBlocksExpensiveRuns()
    {
        var workflow = Workflow();
        var profile = workflow.Template!.Providers.First(p => p.Id == workflow.Template.SelectedProvider);
        var prompts = new Prompts(false);
        // Worst case above the per-image budget blocks before anything else happens.
        var expensive = new AiPrices(1000, 1000, 1000);
        Assert.StartsWith("Start blockiert", await workflow.AnalyzeAsync(profile, expensive, 3000, 3, prompts, null, CancellationToken.None));
        Assert.Equal(0, prompts.Asked);

        // Nothing selected → no API call and no dialog.
        Assert.Contains("kein API-Aufruf", await workflow.AnalyzeAsync(profile, new AiPrices(0.1m, 0.01m, 0.4m), 3000, 3, prompts, null, CancellationToken.None));
        Assert.Equal(0, workflow.Spent);
    }

    [Fact]
    public async Task Analyze_UserDeclines_SendsNothing()
    {
        var workflow = Workflow();
        workflow.AddImages([TestImages.Jpeg(root)]);
        var profile = workflow.Template!.Providers.First(p => p.Id == workflow.Template.SelectedProvider);
        var prompts = new Prompts(false);
        string status = await workflow.AnalyzeAsync(profile, new AiPrices(0.1m, 0.01m, 0.4m), 3000, 3, prompts, null, CancellationToken.None);
        Assert.Equal("Abgebrochen.", status);
        Assert.Equal(1, prompts.Asked); // the cost/privacy summary is always confirmed first
        Assert.Equal(0, workflow.Spent);
    }

    [Fact]
    public void ChatReply_CreatesReviewableProposalsOnly()
    {
        var workflow = Workflow();
        workflow.AddImages([TestImages.Jpeg(root)]);
        var reply = new JsonObject
        {
            ["action"] = "proposeFieldValues", ["message"] = "ok", ["season"] = "Winter", ["title"] = "Schneeweg",
            ["addKeywords"] = new JsonArray("Schnee", "schnee", "Weg"), ["learningDefaults"] = new JsonObject()
        }.ToJsonString();
        var result = workflow.HandleChatReply(reply, workflow.Rows.ToList(), workflow.Template!);
        Assert.Equal("ok", result.AssistantText);
        var changes = workflow.Rows.Single().Changes;
        Assert.Equal("Schnee; Weg", changes.Single(c => c.Target == "XMP.dc:subject").Proposed);
        Assert.All(changes, c => Assert.Equal("Chat", c.Source));
        Assert.False(File.Exists(AiMetadataService.SidecarPath(workflow.Rows.Single().FilePath))); // nothing written without review
        Assert.Throws<InvalidDataException>(() => workflow.HandleChatReply(reply.Replace("Winter", "Monsun"), workflow.Rows.ToList(), workflow.Template!));
    }

    [Fact]
    public void ChatSchema_HasOnlyThreeActions() =>
        Assert.Equal(3, AiMetadataWorkflow.ChatSchema()["properties"]!["action"]!["enum"]!.AsArray().Count);

    [Fact]
    public async Task Diagnostics_ContainNoUserData()
    {
        var diagnostics = new DiagnosticsService(new DefaultPlatformInfo(), new UnavailablePhotoLibraryService(), new ImageSharpDecoder(), "Test UI", paths);
        var items = await diagnostics.CollectAsync();
        Assert.Contains(items, i => i.Name == "PhotoKit authorization" && i.Value == "Unavailable");
        Assert.Contains(items, i => i.Name == "EXIF capability");
        string text = DiagnosticsService.Format(items);
        Assert.Contains("Application Version: ", text, StringComparison.Ordinal);
    }
}
