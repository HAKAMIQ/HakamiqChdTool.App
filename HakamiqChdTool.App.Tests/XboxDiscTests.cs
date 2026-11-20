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
}
