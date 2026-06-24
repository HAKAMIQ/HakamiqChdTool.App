using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace HakamiqChdTool.App.Tests;

internal static class Program
{
    private const int UserSectorSize = 2048;

    private static int Main(string[] args)
    {
        string appAssemblyPath = ReadRequiredArgument(args, "--app-assembly");
        if (!File.Exists(appAssemblyPath))
        {
            Console.Error.WriteLine("App assembly was not found: " + appAssemblyPath);
            return 1;
        }

        string appDirectory = Path.GetDirectoryName(Path.GetFullPath(appAssemblyPath))
            ?? throw new InvalidOperationException("Unable to resolve app assembly directory.");

        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
        {
            string candidate = Path.Combine(appDirectory, assemblyName.Name + ".dll");
            return File.Exists(candidate)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate)
                : null;
        };

        Assembly appAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(appAssemblyPath));
        var app = new AppReflection(appAssembly);

        string workDirectory = Path.Combine(Path.GetTempPath(), "HakamiqChdTool.Ps2AdvisoryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);

        try
        {
            TestCase[] tests =
            [
                new("SYSTEM.CNF is read from ISO 2048", () => TestIsoSystemCnfScan(app, workDirectory)),
                new("SYSTEM.CNF is read from CUE referenced raw BIN", () => TestCueReferencedRawBinScan(app, workDirectory)),
                new("PS2 detector prefers SYSTEM.CNF over path hints", () => TestDetectorUsesSystemCnf(app, workDirectory)),
                new("PS2 advisory reports structure and emulator-profile notes", () => TestAdvisoryReasons(app, workDirectory)),
                new("Standalone BIN warns that CUE is recommended", () => TestStandaloneBinAdvisory(app, workDirectory)),
                new("BOOT-only SYSTEM.CNF is not treated as PS2", () => TestBootOnlyIsRejected(app, workDirectory)),
                new("CUE references cannot escape their directory", () => TestCueReferenceTraversalIsRejected(app, workDirectory))
            ];

            int passed = 0;
            foreach (TestCase test in tests)
            {
                try
                {
                    test.Run();
                    passed++;
                    Console.WriteLine("[PASS] " + test.Name);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[FAIL] " + test.Name);
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }

            Console.WriteLine("[PASS] PS2 advisory validation tests completed: " + passed + "/" + tests.Length);
            return 0;
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void TestIsoSystemCnfScan(AppReflection app, string workDirectory)
    {
        string isoPath = Path.Combine(workDirectory, "ps2-iso.iso");
        WriteIsoImage(isoPath, physicalSectorSize: 2048, dataOffset: 0, "BOOT2 = cdrom0:\\SLUS_203.12;1\r\nVER = 1.00\r\nVMODE = NTSC\r\n");

        ReflectedStructure structure = app.ScanStructure(isoPath);

        AssertTrue(structure.Success, "Expected SYSTEM.CNF scan to succeed.");
        AssertTrue(structure.GetBool("HasSystemCnf"), "Expected HasSystemCnf.");
        AssertTrue(structure.GetBool("IsPlayStation2"), "Expected IsPlayStation2.");
        AssertEqual("BOOT2", structure.GetString("BootDirective"), "Unexpected boot directive.");
        AssertEqual("SLUS_203.12", structure.GetString("BootExecutable"), "Unexpected boot executable.");
        AssertEqual("SLUS-20312", structure.GetString("Serial"), "Unexpected serial.");
        AssertEqual("USA", structure.GetString("Region"), "Unexpected region.");
        AssertEqual("ISO 2048", structure.GetString("SourceLayout"), "Unexpected source layout.");
    }

    private static void TestCueReferencedRawBinScan(AppReflection app, string workDirectory)
    {
        string binPath = Path.Combine(workDirectory, "ps2-track.bin");
        string cuePath = Path.Combine(workDirectory, "ps2-disc.cue");

        WriteIsoImage(binPath, physicalSectorSize: 2352, dataOffset: 16, "BOOT2 = cdrom0:\\SLES_500.00;1\r\nVER = 1.00\r\nVMODE = PAL\r\n");
        File.WriteAllText(cuePath, "FILE \"ps2-track.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n", Encoding.ASCII);

        ReflectedStructure structure = app.ScanStructure(cuePath);

        AssertTrue(structure.Success, "Expected CUE/BIN SYSTEM.CNF scan to succeed.");
        AssertEqual("SLES-50000", structure.GetString("Serial"), "Unexpected serial.");
        AssertEqual("Europe", structure.GetString("Region"), "Unexpected region.");
        AssertEqual("CD MODE1/2352", structure.GetString("SourceLayout"), "Unexpected source layout.");
    }

    private static void TestDetectorUsesSystemCnf(AppReflection app, string workDirectory)
    {
        string isoPath = Path.Combine(workDirectory, "ordinary-name.iso");
        WriteIsoImage(isoPath, physicalSectorSize: 2048, dataOffset: 0, "BOOT2 = cdrom0:\\SCES_503.61;1\r\nVER = 1.00\r\nVMODE = PAL\r\n");

        object identity = app.DetectIdentity(isoPath, detectedPlatform: null);

        AssertTrue(GetBool(identity, "IsPlayStation2"), "Expected PS2 identity.");
        AssertEqual(98, GetInt(identity, "Confidence"), "Expected structure-level confidence.");
        AssertEqual("SCES-50361", GetString(identity, "Serial"), "Unexpected serial.");
        AssertEqual("Europe", GetString(identity, "Region"), "Unexpected region.");
        AssertEqual("CompactIsoPossiblyCd", GetEnumName(identity, "MediaKind"), "Unexpected media kind for compact ISO.");
        AssertTrue(GetString(identity, "DetectionSource").Contains("SYSTEM", StringComparison.OrdinalIgnoreCase)
            || GetString(identity, "DetectionSource").Contains("BOOT2", StringComparison.OrdinalIgnoreCase),
            "Expected detection source to mention disc structure.");
    }

    private static void TestAdvisoryReasons(AppReflection app, string workDirectory)
    {
        string isoPath = Path.Combine(workDirectory, "ps2-advisory.iso");
        WriteIsoImage(isoPath, physicalSectorSize: 2048, dataOffset: 0, "BOOT2 = cdrom0:\\SLPM_650.01;1\r\nVER = 1.00\r\nVMODE = NTSC\r\n");

        object advisory = app.BuildAdvisory(isoPath, detectedPlatform: null)
            ?? throw new InvalidOperationException("Expected PS2 advisory.");

        AssertEqual("PlayStation 2", GetNullableString(advisory, "Platform"), "Unexpected platform.");
        AssertEqual("Japan", GetNullableString(advisory, "Region"), "Unexpected region.");
        AssertEqual("SLPM-65001", GetNullableString(advisory, "DiscGroupKey"), "Unexpected disc group key.");

        IReadOnlySet<string> codes = app.GetAdvisoryReasonCodes(advisory);
        AssertContains(codes, "PS2_DISC_IDENTITY_SUMMARY");
        AssertContains(codes, "PS2_DISC_STRUCTURE_SYSTEM_CNF_BOOT");
        AssertContains(codes, "PS2_CLASSICS_COMPACT_ISO_MAY_BE_PS2CD");
        AssertContains(codes, "PS2_CLASSICS_CONFIG_MAY_BE_REQUIRED");
        AssertContains(codes, "PS2_PS3_EMULATOR_PROFILE_DIFFERS");
    }

    private static void TestStandaloneBinAdvisory(AppReflection app, string workDirectory)
    {
        string binPath = Path.Combine(workDirectory, "standalone.bin");
        WriteIsoImage(binPath, physicalSectorSize: 2352, dataOffset: 16, "BOOT2 = cdrom0:\\SLUS_200.01;1\r\nVER = 1.00\r\nVMODE = NTSC\r\n");

        object advisory = app.BuildAdvisory(binPath, detectedPlatform: null)
            ?? throw new InvalidOperationException("Expected PS2 advisory.");

        IReadOnlySet<string> codes = app.GetAdvisoryReasonCodes(advisory);
        AssertContains(codes, "PS2_CLASSICS_CUE_FILE_RECOMMENDED");
        AssertContains(codes, "PS2_DISC_STRUCTURE_SYSTEM_CNF_BOOT");
    }

    private static void TestBootOnlyIsRejected(AppReflection app, string workDirectory)
    {
        string isoPath = Path.Combine(workDirectory, "boot-only.iso");
        WriteIsoImage(isoPath, physicalSectorSize: 2048, dataOffset: 0, "BOOT = cdrom:\\SCUS_944.26;1\r\nVER = 1.00\r\n");

        ReflectedStructure structure = app.ScanStructure(isoPath);
        AssertFalse(structure.Success, "BOOT-only SYSTEM.CNF should not be treated as PS2.");
    }

    private static void TestCueReferenceTraversalIsRejected(AppReflection app, string workDirectory)
    {
        string outsideDirectory = Path.Combine(workDirectory, "outside");
        string cueDirectory = Path.Combine(workDirectory, "cue");
        Directory.CreateDirectory(outsideDirectory);
        Directory.CreateDirectory(cueDirectory);

        string outsideBin = Path.Combine(outsideDirectory, "outside.bin");
        string cuePath = Path.Combine(cueDirectory, "escape.cue");

        WriteIsoImage(outsideBin, physicalSectorSize: 2352, dataOffset: 16, "BOOT2 = cdrom0:\\SLUS_999.99;1\r\nVER = 1.00\r\n");
        File.WriteAllText(cuePath, "FILE \"..\\outside\\outside.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n", Encoding.ASCII);

        ReflectedStructure structure = app.ScanStructure(cuePath);
        AssertFalse(structure.Success, "CUE path traversal should not be scanned.");
    }

    private static string ReadRequiredArgument(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        throw new ArgumentException("Missing required argument: " + name);
    }

    private static void WriteIsoImage(string path, int physicalSectorSize, int dataOffset, string systemCnf)
    {
        if (physicalSectorSize < UserSectorSize || dataOffset < 0 || dataOffset + UserSectorSize > physicalSectorSize)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalSectorSize));
        }

        const int sectorCount = 32;
        byte[] image = new byte[physicalSectorSize * sectorCount];
        byte[] pvd = new byte[UserSectorSize];
        byte[] rootDirectory = new byte[UserSectorSize];
        byte[] systemCnfBytes = Encoding.ASCII.GetBytes(systemCnf);

        pvd[0] = 1;
        Encoding.ASCII.GetBytes("CD001", pvd.AsSpan(1, 5));
        pvd[6] = 1;
        WriteDirectoryRecord(pvd, 156, extentLba: 20, dataLength: UserSectorSize, isDirectory: true, fileName: "\0");

        int rootOffset = 0;
        rootOffset += WriteDirectoryRecord(rootDirectory, rootOffset, extentLba: 20, dataLength: UserSectorSize, isDirectory: true, fileName: "\0");
        rootOffset += WriteDirectoryRecord(rootDirectory, rootOffset, extentLba: 20, dataLength: UserSectorSize, isDirectory: true, fileName: "\u0001");
        _ = WriteDirectoryRecord(rootDirectory, rootOffset, extentLba: 21, dataLength: systemCnfBytes.Length, isDirectory: false, fileName: "SYSTEM.CNF;1");

        WriteUserSector(image, physicalSectorSize, dataOffset, lba: 16, pvd);
        WriteUserSector(image, physicalSectorSize, dataOffset, lba: 20, rootDirectory);
        WriteUserSector(image, physicalSectorSize, dataOffset, lba: 21, systemCnfBytes);

        File.WriteAllBytes(path, image);
    }

    private static int WriteDirectoryRecord(byte[] sector, int offset, int extentLba, int dataLength, bool isDirectory, string fileName)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(fileName);
        int recordLength = 33 + nameBytes.Length;
        if ((recordLength & 1) != 0)
        {
            recordLength++;
        }

        if (offset < 0 || offset + recordLength > sector.Length)
        {
            throw new InvalidOperationException("Directory record does not fit in sector.");
        }

        sector[offset] = (byte)recordLength;
        sector[offset + 1] = 0;
        WriteInt32LittleEndian(sector, offset + 2, extentLba);
        WriteInt32BigEndian(sector, offset + 6, extentLba);
        WriteInt32LittleEndian(sector, offset + 10, dataLength);
        WriteInt32BigEndian(sector, offset + 14, dataLength);
        sector[offset + 25] = isDirectory ? (byte)0x02 : (byte)0x00;
        sector[offset + 28] = 1;
        sector[offset + 29] = 0;
        sector[offset + 30] = 0;
        sector[offset + 31] = 1;
        sector[offset + 32] = (byte)nameBytes.Length;
        Buffer.BlockCopy(nameBytes, 0, sector, offset + 33, nameBytes.Length);

        return recordLength;
    }

    private static void WriteUserSector(byte[] image, int physicalSectorSize, int dataOffset, int lba, byte[] userData)
    {
        int offset = (lba * physicalSectorSize) + dataOffset;
        Buffer.BlockCopy(userData, 0, image, offset, Math.Min(userData.Length, UserSectorSize));
    }

    private static void WriteInt32LittleEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static bool GetBool(object instance, string propertyName) =>
        (bool)(ReadProperty(instance, propertyName) ?? false);

    private static int GetInt(object instance, string propertyName) =>
        (int)(ReadProperty(instance, propertyName) ?? 0);

    private static string GetString(object instance, string propertyName) =>
        (string)(ReadProperty(instance, propertyName) ?? string.Empty);

    private static string? GetNullableString(object instance, string propertyName) =>
        (string?)ReadProperty(instance, propertyName);

    private static string GetEnumName(object instance, string propertyName) =>
        ReadProperty(instance, propertyName)?.ToString() ?? string.Empty;

    private static object? ReadProperty(object instance, string propertyName)
    {
        PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);

        return property.GetValue(instance);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message + $" Expected: {expected}. Actual: {actual}.");
        }
    }

    private static void AssertContains(IReadOnlySet<string> values, string expected)
    {
        if (!values.Contains(expected))
        {
            throw new InvalidOperationException("Expected advisory reason was not found: " + expected + ". Found: " + string.Join(", ", values));
        }
    }

    private sealed record TestCase(string Name, Action Run);

    private sealed class AppReflection
    {
        private readonly MethodInfo scannerTryScan;
        private readonly MethodInfo detectorDetect;
        private readonly MethodInfo advisoryBuild;

        public AppReflection(Assembly appAssembly)
        {
            Type scannerType = GetRequiredType(appAssembly, "HakamiqChdTool.App.Services.PlayStation.Ps2.Ps2DiscStructureScanner");
            Type detectorType = GetRequiredType(appAssembly, "HakamiqChdTool.App.Services.PlayStation.Ps2.Ps2DiscIdentityDetector");
            Type advisoryType = GetRequiredType(appAssembly, "HakamiqChdTool.App.Services.PlayStation.Ps2.Ps2CompatibilityAdvisoryService");

            scannerTryScan = GetRequiredMethod(scannerType, "TryScan");
            detectorDetect = GetRequiredMethod(detectorType, "Detect");
            advisoryBuild = GetRequiredMethod(advisoryType, "BuildQueueAdvisory");
        }

        public ReflectedStructure ScanStructure(string path)
        {
            object?[] arguments = [path, null];
            bool success = (bool)(scannerTryScan.Invoke(null, arguments) ?? false);
            return new ReflectedStructure(success, arguments[1]);
        }

        public object DetectIdentity(string path, string? detectedPlatform)
        {
            object? result = detectorDetect.Invoke(null, [path, detectedPlatform]);
            return result ?? throw new InvalidOperationException("PS2 identity detector returned null.");
        }

        public object? BuildAdvisory(string path, string? detectedPlatform) =>
            advisoryBuild.Invoke(null, [path, detectedPlatform]);

        public IReadOnlySet<string> GetAdvisoryReasonCodes(object advisory)
        {
            object? reasonsObject = ReadProperty(advisory, "Reasons");
            if (reasonsObject is not IEnumerable reasons)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (object reason in reasons)
            {
                string code = GetString(reason, "Code");
                if (!string.IsNullOrWhiteSpace(code))
                {
                    codes.Add(code);
                }
            }

            return codes;
        }

        private static Type GetRequiredType(Assembly assembly, string typeName) =>
            assembly.GetType(typeName, throwOnError: true, ignoreCase: false)
            ?? throw new TypeLoadException(typeName);

        private static MethodInfo GetRequiredMethod(Type type, string methodName) =>
            type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, methodName);
    }

    private sealed class ReflectedStructure(bool success, object? instance)
    {
        public bool Success { get; } = success;

        public string GetString(string propertyName) =>
            instance is null ? string.Empty : Program.GetString(instance, propertyName);

        public bool GetBool(string propertyName) =>
            instance is not null && Program.GetBool(instance, propertyName);
    }
}
