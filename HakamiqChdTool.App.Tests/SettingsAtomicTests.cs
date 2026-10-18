using System;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services.Configuration;
using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static void TestSettingsSaveReplacesFileAtomically(string workDirectory)
    {
        string directory = Path.Combine(workDirectory, "settings-atomic");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");

        using var service = new AppSettingsService(path);
        AppSettings first = AppSettings.CreateSafeDefaults();
        first.MaxConcurrentConversions = 2;
        service.Save(first);

        AssertEqual(2, service.Load().MaxConcurrentConversions, "A saved setting is read back.");
        AssertFalse(File.Exists(path + ".tmp"), "A successful save leaves no staged file behind.");

        // Block the staging path so the new content cannot be written: the previous complete
        // file must survive instead of being truncated in place.
        string before = File.ReadAllText(path);
        Directory.CreateDirectory(path + ".tmp");
        AppSettings second = AppSettings.CreateSafeDefaults();
        second.MaxConcurrentConversions = 4;
        service.Save(second);

        AssertEqual(before, File.ReadAllText(path), "A failed save keeps the previous settings file intact.");
        AssertEqual(2, service.Load().MaxConcurrentConversions, "The previous settings still load after a failed save.");
        Directory.Delete(path + ".tmp");

        service.Save(second);
        AssertEqual(4, service.Load().MaxConcurrentConversions, "Saving works again once the staging path is free.");
    }

    private static void TestSettingsSaveRecoversStaleStagingAndSerializesConcurrentSaves(string workDirectory)
    {
        string directory = Path.Combine(workDirectory, "settings-concurrent");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");

        // A crash mid-write leaves a truncated staged file; the real file stays loadable and the next save replaces the stale one.
        using var service = new AppSettingsService(path);
        AppSettings seed = AppSettings.CreateSafeDefaults();
        seed.MaxConcurrentConversions = 2;
        service.Save(seed);
        File.WriteAllText(path + ".tmp", "{ \"truncated\": ");
        AssertEqual(2, service.Load().MaxConcurrentConversions, "A stale staged file never affects loading.");

        System.Threading.Tasks.Parallel.For(1, 33, i =>
        {
            AppSettings s = AppSettings.CreateSafeDefaults();
            s.MaxConcurrentConversions = 1 + i % 4;
            service.Save(s);
        });

        AssertFalse(File.Exists(path + ".tmp"), "Concurrent saves leave no staged file behind.");
        int final = service.Load().MaxConcurrentConversions;
        AssertTrue(final is >= 1 and <= 4, "Concurrent saves leave one complete, parseable settings file.");
    }

    // A debounced snapshot still waiting out its delay must not overwrite a later explicit save.
    private static void TestExplicitSaveSupersedesPendingDebouncedSave(string workDirectory)
    {
        string directory = Path.Combine(workDirectory, "settings-debounce");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");

        using var service = new AppSettingsService(path);
        AppSettings stale = AppSettings.CreateSafeDefaults();
        stale.MaxConcurrentConversions = 1;
        System.Threading.Tasks.Task pending = service.SaveDebouncedAsync(stale);

        AppSettings current = AppSettings.CreateSafeDefaults();
        current.MaxConcurrentConversions = 3;
        service.Save(current);

        pending.GetAwaiter().GetResult();
        System.Threading.Thread.Sleep(900);
        AssertEqual(3, service.Load().MaxConcurrentConversions, "The older debounced snapshot did not overwrite the explicit save.");
    }
}
