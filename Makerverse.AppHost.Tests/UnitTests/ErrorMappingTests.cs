using Common;
using ErrorOr;
using Microsoft.AspNetCore.Mvc;

namespace Makerverse.AppHost.Tests.UnitTests;

/// <summary>TESTING.md §9 U3：ErrorExtensions.ToActionResult 错误类型 → HTTP 状态码映射。</summary>
public sealed class ErrorMappingTests {
    [Fact]
    public void NotFound_MapsTo404() {
        ObjectResult result = Assert.IsType<NotFoundObjectResult>(Error.NotFound("X", "desc").ToActionResult());
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public void Conflict_MapsTo409() {
        ObjectResult result = Assert.IsType<ConflictObjectResult>(Error.Conflict("X", "desc").ToActionResult());
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public void Forbidden_MapsTo403() {
        var result = Assert.IsType<ObjectResult>(Error.Forbidden("X", "desc").ToActionResult());
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public void Unauthorized_MapsTo401() {
        ObjectResult result = Assert.IsType<UnauthorizedObjectResult>(Error.Unauthorized("X", "desc").ToActionResult());
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public void Validation_MapsTo400() {
        ObjectResult result = Assert.IsType<BadRequestObjectResult>(Error.Validation("X", "desc").ToActionResult());
        Assert.Equal(400, result.StatusCode);
    }
}