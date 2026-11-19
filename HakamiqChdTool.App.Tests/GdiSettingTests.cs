using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.ViewModels;
using System.Reflection;
using System.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The Options "Extract Dreamcast GD-ROM to CUE/BIN" checkbox loads, saves and marks unsaved changes.
    private static void TestGdRomCueBinSettingRoundTripsThroughOptions()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var options = new OptionsViewModel();
                options.Load(new AppSettings { ExtractGdRomAsCueBin = true });
                AssertTrue(options.ExtractGdRomAsCueBin, "The saved setting is loaded into Options.");

                options.ExtractGdRomAsCueBin = false;
                AssertFalse(options.BuildResultSettings(new AppSettings { ExtractGdRomAsCueBin = true }).ExtractGdRomAsCueBin, "Options saves the new value.");

                MethodInfo equal = typeof(OptionsViewModel).GetMethod("CurrentValuesEqual", BindingFlags.Instance | BindingFlags.NonPublic)!;
                AssertFalse((bool)equal.Invoke(options, [new AppSettings { ExtractGdRomAsCueBin = true }])!, "A changed value counts as an unsaved change.");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure is TargetInvocationException { InnerException: { } inner } ? inner : failure;
        }
    }
}
