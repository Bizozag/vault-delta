using VaultDelta.Domain.Paths;

namespace VaultDelta.Domain.Tests.Paths;

public sealed class RelativePathTests
{
    [Theory]
    [InlineData("Notes/项目.md", "Notes/项目.md")]
    [InlineData("Notes\\项目.md", "Notes/项目.md")]
    [InlineData("资料/😀.md", "资料/😀.md")]
    [InlineData("folder/name with spaces.md", "folder/name with spaces.md")]
    public void Parse_normalizes_valid_relative_paths(string input, string expected)
    {
        RelativePath path = RelativePath.Parse(input);

        Assert.Equal(expected, path.Value);
        Assert.Equal(expected, path.ToString());
    }

    [Fact]
    public void Parse_normalizes_unicode_to_nfc()
    {
        const string decomposed = "Cafe\u0301/note.md";

        RelativePath path = RelativePath.Parse(decomposed);

        Assert.Equal("Caf\u00e9/note.md", path.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../note.md")]
    [InlineData("folder/../note.md")]
    [InlineData("folder/./note.md")]
    [InlineData("folder//note.md")]
    [InlineData("folder\\\\note.md")]
    [InlineData("/rooted.md")]
    [InlineData("\\rooted.md")]
    [InlineData("C:/vault/note.md")]
    [InlineData("C:\\vault\\note.md")]
    [InlineData("\\\\server\\share\\note.md")]
    [InlineData("folder/")]
    [InlineData("folder/\0note.md")]
    public void Parse_rejects_unsafe_or_non_canonical_paths(string? input)
    {
        Assert.ThrowsAny<ArgumentException>(() => RelativePath.Parse(input!));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("folder/AUX.md")]
    [InlineData("folder/name.")]
    [InlineData("folder/name ")]
    [InlineData("folder/name:stream")]
    public void Parse_rejects_paths_that_are_not_portable_to_windows(string input)
    {
        Assert.Throws<ArgumentException>(() => RelativePath.Parse(input));
    }

    [Fact]
    public void PortableComparer_is_case_insensitive_and_unicode_normalization_aware()
    {
        RelativePath composed = RelativePath.Parse("Notes/Caf\u00e9.md");
        RelativePath decomposedUppercase = RelativePath.Parse("notes/CAFE\u0301.md");

        Assert.Equal(composed, decomposedUppercase, RelativePath.PortableComparer);
        Assert.Equal(
            RelativePath.PortableComparer.GetHashCode(composed),
            RelativePath.PortableComparer.GetHashCode(decomposedUppercase));
    }

    [Fact]
    public void Default_equality_preserves_path_case()
    {
        RelativePath upper = RelativePath.Parse("Notes/Readme.md");
        RelativePath lower = RelativePath.Parse("notes/readme.md");

        Assert.NotEqual(upper, lower);
    }
}
