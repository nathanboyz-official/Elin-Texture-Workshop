using ElinTextureManager.Core.Model;
using Xunit;

namespace ElinTextureManager.Tests;

public class TextureIdentityTests
{
    [Theory]
    [InlineData("objC_2115.png", "objC_2115", "objC", 2115)]
    [InlineData("objC_1532.png", "objC_1532", "objC", 1532)]
    [InlineData("obj_324.png", "obj_324", "obj", 324)]
    [InlineData("objS_123.png", "objS_123", "objS", 123)]
    [InlineData("chara_456.png", "chara_456", "chara", 456)]
    [InlineData("objCL_9.png", "objCL_9", "objCL", 9)]
    public void ParsesStandardNames(string file, string id, string prefix, int number)
    {
        var identity = TextureIdentity.Parse(file);

        Assert.Equal(id, identity.TextureId);
        Assert.Equal(prefix, identity.Prefix);
        Assert.Equal(number, identity.NumericId);
    }

    [Fact]
    public void TextureIdExcludesExtension()
    {
        Assert.Equal("objC_2115", TextureIdentity.Parse("objC_2115.png").TextureId);
    }

    [Theory]
    [InlineData("objs_S_snow.png", "objs_S_snow")]   // trailing part is not numeric
    [InlineData("world.png", "world")]               // no underscore at all
    [InlineData("objCL_111b.png", "objCL_111b")]     // number with a suffix letter
    public void KeepsWholeNameWhenThereIsNoTrailingNumber(string file, string expectedId)
    {
        var identity = TextureIdentity.Parse(file);

        Assert.Equal(expectedId, identity.TextureId);
        Assert.Equal(expectedId, identity.Prefix);
        Assert.Null(identity.NumericId);
    }

    [Fact]
    public void HandlesRealWorldCopySuffix()
    {
        // Seen in the wild: "objc_1003 - Copy.png"
        var identity = TextureIdentity.Parse("objc_1003 - Copy.png");

        Assert.Equal("objc_1003 - Copy", identity.TextureId);
        Assert.Null(identity.NumericId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".png")]
    public void MalformedNamesDoNotThrow(string file)
    {
        var identity = TextureIdentity.Parse(file);
        Assert.Equal(string.Empty, identity.TextureId);
    }

    [Fact]
    public void TrailingUnderscoreIsNotTreatedAsANumber()
    {
        var identity = TextureIdentity.Parse("objC_.png");

        Assert.Equal("objC_", identity.TextureId);
        Assert.Null(identity.NumericId);
    }

    [Fact]
    public void TextureIdIsCaseSensitiveButCategoryLookupIsNot()
    {
        Assert.Equal(TextureCategory.Characters, TextureCategory.ForPrefix("objC"));
        Assert.Equal(TextureCategory.Characters, TextureCategory.ForPrefix("objc"));
    }

    [Fact]
    public void UnknownPrefixesFallIntoOtherRatherThanBeingDropped()
    {
        Assert.Equal(TextureCategory.Other, TextureCategory.ForPrefix("zzz_custom"));
        Assert.Equal(TextureCategory.Other, TextureCategory.ForPrefix(null));
    }
}
