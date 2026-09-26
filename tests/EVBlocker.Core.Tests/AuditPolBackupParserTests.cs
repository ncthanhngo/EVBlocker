using EVBlocker.Core.Audit;

namespace EVBlocker.Core.Tests;

public sealed class AuditPolBackupParserTests
{
    private const string TargetGuid = AuditPolicyManager.FilteringPlatformConnectionGuid;

    private const string Header =
        "Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value";

    private static string Csv(params string[] rows) => string.Join("\n", new[] { Header }.Concat(rows));

    [Theory]
    [InlineData("0", AuditSetting.None)]
    [InlineData("1", AuditSetting.Success)]
    [InlineData("2", AuditSetting.Failure)]
    [InlineData("3", AuditSetting.Success | AuditSetting.Failure)]
    public void FindSetting_ReadsNumericSettingValue(string settingValue, AuditSetting expected)
    {
        string csv = Csv($"PC,System,Filtering Platform Connection,{TargetGuid},Failure,,{settingValue}");

        Assert.Equal(expected, AuditPolBackupParser.FindSetting(csv, TargetGuid));
    }

    [Fact]
    public void FindSetting_IgnoresOtherSubcategories()
    {
        // Filtering Platform Packet Drop sits next to the target in real output and must not match.
        string csv = Csv(
            "PC,System,Filtering Platform Packet Drop,{0CCE9225-69AE-11D9-BED3-505054503030},Failure,,2",
            $"PC,System,Filtering Platform Connection,{TargetGuid},Success,,1");

        Assert.Equal(AuditSetting.Success, AuditPolBackupParser.FindSetting(csv, TargetGuid));
    }

    [Fact]
    public void FindSetting_SubcategoryAbsent_ReturnsNull()
    {
        // auditpol omits a subcategory that has never been configured; that is "not recorded",
        // which the caller maps to None, not an error.
        string csv = Csv("PC,System,File Share,{0CCE9224-69AE-11D9-BED3-505054503030},Success,,1");

        Assert.Null(AuditPolBackupParser.FindSetting(csv, TargetGuid));
    }

    [Fact]
    public void FindSetting_LocalisedHeader_FallsBackToColumnPositions()
    {
        // On non-English Windows the header text is translated, so matching by name fails and the
        // parser must fall back to the stable column layout.
        string localisedHeader = "Tên máy,Đối tượng,Phân loại,GUID,Bao gồm,Loại trừ,Giá trị";
        string csv = localisedHeader
                     + "\n"
                     + $"PC,System,Filtering Platform Connection,{TargetGuid},Failure,,2";

        Assert.Equal(AuditSetting.Failure, AuditPolBackupParser.FindSetting(csv, TargetGuid));
    }

    [Fact]
    public void FindSetting_HandlesCrLfAndSurroundingWhitespace()
    {
        string csv = Header + "\r\n" + $" PC , System , Filtering Platform Connection , {TargetGuid} , Failure , , 2 \r\n";

        Assert.Equal(AuditSetting.Failure, AuditPolBackupParser.FindSetting(csv, TargetGuid));
    }

    [Fact]
    public void FindSetting_UnexpectedHighBits_AreMaskedOff()
    {
        // Guards against a future setting value producing an out-of-range enum.
        string csv = Csv($"PC,System,Filtering Platform Connection,{TargetGuid},Failure,,255");

        Assert.Equal(AuditSetting.Success | AuditSetting.Failure, AuditPolBackupParser.FindSetting(csv, TargetGuid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("only-a-header-row")]
    public void FindSetting_UnusableCsv_ReturnsNull(string csv)
    {
        Assert.Null(AuditPolBackupParser.FindSetting(csv, TargetGuid));
    }

    [Fact]
    public void FindSetting_NonNumericSettingValue_ReturnsNull()
    {
        // Defends against accidentally parsing the localised /get output instead of the CSV.
        string csv = Csv($"PC,System,Filtering Platform Connection,{TargetGuid},Failure,,Failure");

        Assert.Null(AuditPolBackupParser.FindSetting(csv, TargetGuid));
    }

    [Fact]
    public void FindSetting_BlankSubcategoryGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => AuditPolBackupParser.FindSetting(Csv(), "  "));
    }
}
