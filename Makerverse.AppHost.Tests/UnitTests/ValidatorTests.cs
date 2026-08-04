using ActivityService.Validators;
using LiveService.Validators;

namespace Makerverse.AppHost.Tests.UnitTests;

/// <summary>TESTING.md §9 U1/U2：TagSlugValidatorAttribute 与 UpdateStatusValidatorAttribute 规则。</summary>
public sealed class ValidatorTests {
    [Fact]
    public void TagSlug_AcceptsValidSlug() {
        Assert.True(new TagSlugValidatorAttribute().IsValid("abc-123"));
    }

    [Theory]
    [InlineData("Abc")]      // 大写
    [InlineData("a")]        // <3
    [InlineData("bad slug")] // 空格
    [InlineData("带中文")]      // 非 ASCII
    public void TagSlug_RejectsInvalidSlug(string slug) {
        Assert.False(new TagSlugValidatorAttribute().IsValid(slug));
    }

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    public void UpdateStatus_AcceptsValidStatus(string status) {
        Assert.True(new UpdateStatusValidatorAttribute().IsValid(status));
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("")]
    public void UpdateStatus_RejectsInvalidStatus(string status) {
        Assert.False(new UpdateStatusValidatorAttribute().IsValid(status));
    }
}