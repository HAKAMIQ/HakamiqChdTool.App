using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The queue row activity spinner starts only while it is shown and Windows animations are on, and
    // stops when it hides; rows are recycled, so a Loaded trigger kept spinning for hidden rows.
    private static void TestQueueSpinnerRunsOnlyWhileShown()
    {
        XDocument template = XDocument.Load(FindRepoFile(Path.Combine("Resources", "Style", "Queue", "QIHeaderTpl.xaml")));
        XElement spinner = template.Descendants().Single(element =>
            element.Name.LocalName == "Ellipse"
            && ((string?)element.Attribute("Visibility") ?? string.Empty).Contains("ShowRuntimeActivitySpinner", System.StringComparison.Ordinal));

        AssertFalse(spinner.Descendants().Any(element => element.Name.LocalName == "EventTrigger"), "The spinner no longer starts on Loaded.");

        XElement trigger = spinner.Descendants().Single(element => element.Name.LocalName == "MultiDataTrigger");
        string[] conditions = [.. trigger.Descendants().Where(element => element.Name.LocalName == "Condition").Select(element => (string?)element.Attribute("Binding") ?? string.Empty)];
        AssertTrue(conditions.Any(binding => binding.Contains("ShowRuntimeActivitySpinner", System.StringComparison.Ordinal)), "The spin depends on the spinner being shown.");
        AssertTrue(conditions.Any(binding => binding.Contains("ClientAreaAnimation", System.StringComparison.Ordinal)), "The spin respects the Windows animation setting.");

        string? started = (string?)trigger.Descendants().Single(element => element.Name.LocalName == "BeginStoryboard").Attributes().Single(attribute => attribute.Name.LocalName == "Name");
        XElement stop = trigger.Descendants().Single(element => element.Name.LocalName == "RemoveStoryboard");
        AssertEqual(started, (string?)stop.Attribute("BeginStoryboardName"), "Leaving the condition removes the running animation.");
    }
}
