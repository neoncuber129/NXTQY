using Inventory.App.Helpers;
using Xunit;

namespace Inventory.Tests;

public class FileNameSanitizerTests
{
    [Theory]
    [InlineData("PX 01/2026", "PX 01_2026")]
    [InlineData("A\\B:C*D?E\"F<G>H|I", "A_B_C_D_E_F_G_H_I")]
    [InlineData("  bình thường  ", "bình thường")]
    [InlineData("", "untitled")]
    [InlineData("   ", "untitled")]
    public void Sanitize_Replaces_Invalid_File_Name_Chars(string input, string expected)
    {
        Assert.Equal(expected, FileNameSanitizer.Sanitize(input));
    }

    [Fact]
    public void Sanitize_Keeps_Extension_When_Sanitizing_Full_File_Name()
    {
        Assert.Equal("SoCanDoi_01_2026.docx", FileNameSanitizer.Sanitize("SoCanDoi_01/2026.docx"));
    }
}
