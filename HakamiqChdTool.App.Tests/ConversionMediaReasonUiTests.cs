using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models.Chd;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Xml.Linq;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    internal static void RunConversionMediaReasonIsolated() =>
        RunIsolatedUiChild("conversion-media-reason", Path.GetTempPath());
}

internal static class ConversionMediaReasonUiTests
{
    public static void RendersReasonsInArabicAndEnglish() => Program.RunConversionMediaReasonIsolated();

    // Called only by the test runner's isolated UI child-process dispatch.
    public static void RunIsolated()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                if (Application.Current is not null)
                    throw new InvalidOperationException("This test requires an isolated WPF Application.");

                Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                try
                {
                    foreach (bool arabic in new[] { false, true })
                    {
                        LoadResources(app, arabic);
                        CheckLanguage(arabic);
                    }
                }
                finally
                {
                    app.Shutdown();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
            throw new InvalidOperationException("Media reason UI test timed out.");
        if (failure is not null)
            throw new InvalidOperationException("Media reason UI test failed.", failure);
    }

    private static void CheckLanguage(bool arabic)
    {
        string Get(string suffix) => ArabicUi.Get("LocConversionPreview_" + suffix);
        string Render(ChdConversionDecisionPreview preview) => Invoke("PreviewMediaReason", preview);
        void Equal(string expected, string actual, string label)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidOperationException($"{(arabic ? "Arabic" : "English")} {label}: expected '{expected}', got '{actual}'.");
        }

        foreach ((DiscMediaStructure structure, DiscMediaClassification kind, string key) in new[]
        {
            (DiscMediaStructure.Ps1, DiscMediaClassification.CdRom, "ReasonPs1"),
            (DiscMediaStructure.Psp, DiscMediaClassification.DvdRom, "ReasonPsp"),
            (DiscMediaStructure.Ps3, DiscMediaClassification.DvdRom, "ReasonPs3")
        })
        {
            var decision = new DiscMediaDecision(kind, DiscMediaDecisionSource.Structure, structure);
            string classification = Get(kind == DiscMediaClassification.CdRom ? "ClassifiedCd" : "ClassifiedDvd");
            Equal(classification + " — " + Get("ReasonConfirmed") + " — " + Get(key), Render(Preview() with { IsoMediaDecision = decision }), key);
        }

        foreach (DiscMediaClassification kind in new[] { DiscMediaClassification.CdRom, DiscMediaClassification.DvdRom })
        {
            var decision = new DiscMediaDecision(kind, DiscMediaDecisionSource.SizeFallback, DiscMediaStructure.None);
            string classification = Get(kind == DiscMediaClassification.CdRom ? "ClassifiedCd" : "ClassifiedDvd");
            Equal(classification + " — " + Get("ReasonEstimated") + " — " + Get("ReasonPs2"),
                Render(Preview() with { IsoMediaDecision = decision, MediaEvidence = "LocDiscProbe_SystemCnfPs2Boot2" }), "PS2 size estimate");
            Equal(classification + " — " + Get("ReasonEstimated") + " — " + Get("ReasonSize"),
                Render(Preview() with { IsoMediaDecision = decision, MediaEvidence = "unrecognized evidence" }), "size estimate");
        }

        var unknown = new DiscMediaDecision(DiscMediaClassification.Unknown, DiscMediaDecisionSource.Unknown, DiscMediaStructure.None);
        foreach (string command in new[] { "createdvd", "createcd" })
            Equal(Get("ReasonUnknown"), Render(Preview() with { IsoMediaDecision = unknown, Command = command }), "unknown regardless of command");

        foreach ((DescriptorCreateCdEvidence descriptor, string key) in new[]
        {
            (DescriptorCreateCdEvidence.Cue, "ReasonCue"),
            (DescriptorCreateCdEvidence.Gdi, "ReasonGdi")
        })
            Equal(Get(key), Render(Preview() with { DescriptorEvidence = descriptor }), key);

        Equal(Get("NotAvailable"), Render(Preview() with { IsoMediaDecision = null }), "missing decision");
        Equal(Get("NotAvailable"), Render(Preview() with { IsSupported = false, IsoMediaDecision = unknown, DescriptorEvidence = DescriptorCreateCdEvidence.Cue }), "rejected input");

        // Exercise the same line formatter used by the archive, CSO and exception paths.
        foreach (string reason in new[] { Get("AtRun"), Get("NotAvailable"), Get("ReasonUnknown") })
            Equal("\n" + ArabicUi.Format("LocConversionPreview_MediaReasonLine", reason), Invoke("PreviewMediaReasonLine", reason), "reason line");

        string ps2 = Get("ReasonPs2");
        string unknownText = Get("ReasonUnknown");
        string gdi = Get("ReasonGdi");
        if (arabic)
        {
            Require(ps2.Contains("تقديري", StringComparison.Ordinal) && ps2.Contains("الحجم", StringComparison.Ordinal), "Arabic PS2 must state a size estimate.");
            Require(unknownText.Contains("غير معروف", StringComparison.Ordinal) && unknownText.Contains("افتراض آمن", StringComparison.Ordinal), "Arabic unknown must identify the safe default.");
            Require(gdi.Contains("مسار المعالجة", StringComparison.Ordinal) && gdi.Contains("وليس نوع القرص", StringComparison.Ordinal), "Arabic GDI must describe the processing path.");
        }
        else
        {
            Require(ps2.Contains("estimated by size", StringComparison.Ordinal), "English PS2 must state a size estimate.");
            Require(unknownText.Contains("Unknown", StringComparison.Ordinal) && unknownText.Contains("safe default", StringComparison.Ordinal), "English unknown must identify the safe default.");
            Require(gdi.Contains("processing path", StringComparison.Ordinal) && gdi.Contains("not physical disc type", StringComparison.Ordinal), "English GDI must describe the processing path.");
        }
    }

    private static ChdConversionDecisionPreview Preview() => new(
        true, "ISO", "", "createdvd", @"D:\CHDWork\Sample.iso", @"D:\CHDOut\Sample.chd", "",
        ChdmanCapabilitySnapshot.Unavailable("chdman.exe", "unavailable"), "", 0, Array.Empty<string>(), "", false, "", "");

    private static string Invoke(string name, object argument)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing preview formatter: " + name);
        return (string)(method.Invoke(null, new[] { argument })
            ?? throw new InvalidOperationException("Preview formatter returned null: " + name));
    }

    private static void LoadResources(Application app, bool arabic)
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "HakamiqChdTool.App.sln")))
            directory = Path.GetDirectoryName(directory);
        if (directory is null)
            throw new InvalidOperationException("Repository root not found from test output.");

        string path = Path.Combine(directory, "Resources", arabic ? "ArabicStrings.xaml" : "EnglishStrings.xaml");
        app.Resources.Clear();
        foreach (var element in XDocument.Load(path).Descendants().Where(e => e.Name.LocalName == "String"))
        {
            string? key = (string?)element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key");
            if (key is not null)
                app.Resources[key] = element.Value;
        }
        app.Resources["App.FlowDirection"] = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
