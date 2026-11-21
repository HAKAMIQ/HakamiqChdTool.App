using System.Text.Json;
using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Files unpacked from archives are deleted when the task ends unless the user turned that off; a saved choice
    // in an existing settings file is kept.
    private static void TestTemporaryExtractionIsDeletedByDefault()
    {
        AssertTrue(new AppSettings().DeleteTemporaryExtraction, "New settings delete what was unpacked from archives.");
        AssertTrue(AppSettings.CreateSafeDefaults().DeleteTemporaryExtraction, "Restore defaults turns it on.");

        var saved = JsonSerializer.Deserialize<AppSettings>("{\"DeleteTemporaryExtraction\":false}")!;
        AssertFalse(saved.DeleteTemporaryExtraction, "A saved 'off' is kept.");
        var older = JsonSerializer.Deserialize<AppSettings>("{\"VerifyAfterConversion\":true}")!;
        AssertTrue(older.DeleteTemporaryExtraction, "A settings file without the value gets the new default.");
    }
}