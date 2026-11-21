using System.IO;
using System.Text;
using HakamiqChdTool.App.Core.Input;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // An Xbox or Xbox 360 ISO (XDVDFS signature at any known offset) is refused with its own reason instead of being
    // converted to CHD; an ordinary ISO is still a convertible disc image.
    private static void TestXboxDiscIsoIsRefused(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "xbox-disc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] magic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");

        string WriteIso(string name, long size, long? magicOffset, byte[]? isoHeaderAt8001 = null)
        {
            string path = Path.Combine(root, name);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            stream.SetLength(size);
            if (isoHeaderAt8001 is not null)
            {
                stream.Position = 0x8001;
                stream.Write(isoHeaderAt8001);
            }

            if (magicOffset is long offset)
            {
                stream.Position = offset;
                stream.Write(magic);
            }

            return path;
        }

        string xiso = WriteIso("Game (Xbox).iso", 0x10000 + 0x10000, 0x10000);
        string xgd3 = WriteIso("Game (Xbox 360).iso", 0x2090000 + 0x10000, 0x2090000);
        string normal = WriteIso("Game (PSP).iso", 0x40000, null, Encoding.ASCII.GetBytes("CD001"));
        string tooSmall = WriteIso("Tiny.iso", 0x8000, null);

        var classifier = new MediaInputClassifier();
        foreach (string path in new[] { xiso, xgd3 })
        {
            MediaInputDescriptor descriptor = classifier.Classify(path);
            AssertEqual(MediaInputProbeStatus.XboxDisc, descriptor.ProbeStatus, $"{Path.GetFileName(path)} is recognized as an Xbox disc.");
            MediaInputPipelineDecision decision = MediaInputPipeline.Decide(descriptor);
            AssertTrue(decision.IsBlocked, $"{Path.GetFileName(path)} is not converted to CHD.");
            AssertEqual(MediaInputPipelineDecisionReasons.XboxDisc, decision.Reason, "The reason says it is an Xbox disc.");
            AssertEqual(MediaInputPipeline.XboxDiscMessageKey, decision.MessageKey, "The message explains why.");
        }

        foreach (string path in new[] { normal, tooSmall })
        {
            MediaInputDescriptor descriptor = classifier.Classify(path);
            AssertTrue(descriptor.ProbeStatus != MediaInputProbeStatus.XboxDisc, $"{Path.GetFileName(path)} is not taken for an Xbox disc.");
            AssertEqual(QueueInputRole.ConvertibleDiscImage, MediaInputPipeline.Decide(descriptor).QueueRole, $"{Path.GetFileName(path)} is still a convertible disc image.");
        }
    }

    // An added Xbox ISO shows a row that says why it is not converted, in both languages.
    private static void TestXboxDiscRowExplainsWhy(string workDirectory)
    {
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");
        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        AssertTrue(arabic.TryGetValue(MediaInputPipeline.XboxDiscMessageKey, out string? ar) && ar.Contains("Xbox", StringComparison.Ordinal), "The Arabic reason names Xbox.");
        AssertTrue(english.TryGetValue(MediaInputPipeline.XboxDiscMessageKey, out string? en) && en.Contains("Xbox", StringComparison.Ordinal), "The English reason names Xbox.");

        string root = Path.Combine(workDirectory, "xbox-row-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string xiso = Path.Combine(root, "Game (Xbox).iso");
        using (var stream = new FileStream(xiso, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(0x20000);
            stream.Position = 0x10000;
            stream.Write(Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA"));
        }

        string? detail = null;
        string? state = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            System.Windows.Threading.Dispatcher dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var queue = new HakamiqChdTool.App.Core.Queue.QueueManager(new QuickTestOrchestrator(), HakamiqChdTool.App.Models.AppSettings.CreateSafeDefaults, static () => string.Empty, 1);
                    await using var host = new HakamiqChdTool.App.QueueRun.QuickRunHost(queue, HakamiqChdTool.App.Models.AppSettings.CreateSafeDefaults, new HakamiqChdTool.App.Services.PostProcessing.PostConversionArtifactService(), dispatcher, static () => null, autoStart: false);
                    await host.AddAsync(HakamiqChdTool.App.Models.QueueExecutionProfile.QuickConvert, [xiso]);
                    detail = host.Items.FirstOrDefault()?.StatusDetail;
                    state = host.Items.FirstOrDefault()?.CurrentState;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                }
            });
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        AssertTrue(thread.Join(TimeSpan.FromSeconds(60)), "The intake thread finished.");
        AssertTrue(failure is null, "Intake did not throw: " + failure);
        AssertEqual(MediaInputPipeline.XboxDiscMessageKey, detail, "The row says the file is an Xbox disc.");
        AssertTrue(state is not null, "The Xbox ISO gets a row instead of disappearing.");
    }
}