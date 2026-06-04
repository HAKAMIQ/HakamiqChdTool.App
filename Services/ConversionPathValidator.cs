using System.IO;

namespace HakamiqChdTool.App.Services;

public static class ConversionPathValidator
{
    private static readonly char[] UnsupportedChdmanPathChars =
    [
        '\u0000', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005', '\u0006', '\u0007',
        '\u0008', '\u0009', '\u000A', '\u000B', '\u000C', '\u000D', '\u000E', '\u000F',
        '\u0010', '\u0011', '\u0012', '\u0013', '\u0014', '\u0015', '\u0016', '\u0017',
        '\u0018', '\u0019', '\u001A', '\u001B', '\u001C', '\u001D', '\u001E', '\u001F'
    ];

    public static void ThrowIfUnsafeForChdman(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("مسار الملف غير صالح.", parameterName);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("مسار الملف يحتوي على صيغة غير صالحة.", parameterName, ex);
        }

        if (fullPath.IndexOfAny(UnsupportedChdmanPathChars) >= 0)
        {
            throw new ArgumentException("مسار الملف يحتوي على رموز تحكم غير مدعومة وقد تفشل مع chdman.", parameterName);
        }

        foreach (char invalid in Path.GetInvalidPathChars())
        {
            if (fullPath.Contains(invalid, StringComparison.Ordinal))
            {
                throw new ArgumentException($"مسار الملف يحتوي على رمز غير صالح: U+{(int)invalid:X4}.", parameterName);
            }
        }

        string fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("مسار الملف لا يحتوي على اسم ملف صالح.", parameterName);
        }

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            if (fileName.Contains(invalid, StringComparison.Ordinal))
            {
                throw new ArgumentException($"اسم الملف يحتوي على رمز غير صالح: U+{(int)invalid:X4}.", parameterName);
            }
        }
    }
}