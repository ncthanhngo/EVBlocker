using System.Text;
using EVBlocker.Core.Usb;

namespace EVBlocker.Core.Tests;

/// <summary>
/// The argument string is what gives a shortcut worm away, so these fix the two things a
/// hand-rolled reader gets wrong: a count of characters read as a count of bytes, and the
/// optional blocks between the header and the strings.
/// </summary>
public sealed class ShellLinkTests
{
    private const uint HasLinkTargetIdList = 1 << 0;
    private const uint HasLinkInfo = 1 << 1;
    private const uint HasRelativePath = 1 << 3;
    private const uint HasArguments = 1 << 5;
    private const uint IsUnicode = 1 << 7;

    [Fact]
    public void Arguments_AreRead()
    {
        byte[] link = Build(
            flags: HasArguments | IsUnicode,
            arguments: @"/c start wscript.exe x.vbs & explorer Photos");

        Assert.Equal(
            @"/c start wscript.exe x.vbs & explorer Photos",
            ShellLink.TryParse(link)!.Arguments);
    }

    /// <summary>
    /// The string blocks come in a fixed order, so a relative path present before the arguments
    /// has to be consumed to land on them.
    /// </summary>
    [Fact]
    public void RelativePathBeforeArguments_DoesNotShiftTheArguments()
    {
        byte[] link = Build(
            flags: HasRelativePath | HasArguments | IsUnicode,
            relativePath: @"..\..\Windows\System32\cmd.exe",
            arguments: "/c payload.exe");

        ShellLinkTarget parsed = ShellLink.TryParse(link)!;

        Assert.Equal(@"..\..\Windows\System32\cmd.exe", parsed.RelativePath);
        Assert.Equal("/c payload.exe", parsed.Arguments);
    }

    /// <summary>
    /// A real shortcut carries both optional blocks. They are skipped by their own sizes, and
    /// the two sizes are not counted the same way: one includes itself, the other does not.
    /// </summary>
    [Fact]
    public void OptionalBlocks_AreSkippedByTheirOwnSizes()
    {
        byte[] link = Build(
            flags: HasLinkTargetIdList | HasLinkInfo | HasArguments | IsUnicode,
            arguments: "/c calc",
            targetIdListPayload: 40,
            linkInfoPayload: 60);

        Assert.Equal("/c calc", ShellLink.TryParse(link)!.Arguments);
    }

    [Fact]
    public void AnsiShortcut_IsReadAsAnsi()
    {
        byte[] link = Build(flags: HasArguments, arguments: "/c go.bat", unicode: false);

        Assert.Equal("/c go.bat", ShellLink.TryParse(link)!.Arguments);
    }

    [Fact]
    public void ShortcutWithoutArguments_ReadsAsNone()
    {
        Assert.Null(ShellLink.TryParse(Build(flags: IsUnicode))!.Arguments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(0x4B)]
    public void TooShortToBeALink_ReturnsNull(int length)
    {
        Assert.Null(ShellLink.TryParse(new byte[length]));
    }

    [Fact]
    public void WrongSignature_ReturnsNull()
    {
        byte[] link = Build(flags: IsUnicode);
        link[4] = 0xFF;

        Assert.Null(ShellLink.TryParse(link));
    }

    /// <summary>
    /// These files arrive on media written by anything at all, so a truncated one has to read as
    /// "nothing to see", never as a crash in the middle of a scan.
    /// </summary>
    [Fact]
    public void TruncatedInTheMiddleOfAString_ReturnsNoArguments()
    {
        byte[] link = Build(flags: HasArguments | IsUnicode, arguments: "/c payload.exe");
        byte[] cut = link[..(link.Length - 6)];

        Assert.Null(ShellLink.TryParse(cut)!.Arguments);
    }

    private static byte[] Build(
        uint flags,
        string? relativePath = null,
        string? arguments = null,
        bool unicode = true,
        int targetIdListPayload = 0,
        int linkInfoPayload = 0)
    {
        var bytes = new List<byte>();

        bytes.AddRange(BitConverter.GetBytes(0x4C));
        bytes.AddRange(new byte[]
        {
            0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46,
        });
        bytes.AddRange(BitConverter.GetBytes(flags));
        bytes.AddRange(new byte[0x4C - 0x18]);

        if ((flags & HasLinkTargetIdList) != 0)
        {
            // The recorded size excludes these two bytes.
            bytes.AddRange(BitConverter.GetBytes((ushort)targetIdListPayload));
            bytes.AddRange(new byte[targetIdListPayload]);
        }

        if ((flags & HasLinkInfo) != 0)
        {
            // This one includes them.
            bytes.AddRange(BitConverter.GetBytes((uint)(linkInfoPayload + 4)));
            bytes.AddRange(new byte[linkInfoPayload]);
        }

        void AddString(string value)
        {
            bytes.AddRange(BitConverter.GetBytes((ushort)value.Length));
            bytes.AddRange(unicode ? Encoding.Unicode.GetBytes(value) : Encoding.Default.GetBytes(value));
        }

        if (relativePath is not null)
        {
            AddString(relativePath);
        }

        if (arguments is not null)
        {
            AddString(arguments);
        }

        return bytes.ToArray();
    }
}
