using ErrorOr;
using Microsoft.AspNetCore.Mvc;

namespace Common;

public static class ErrorExtensions {

    public static ActionResult ToActionResult(this Error error) {
        return error.Type switch {
            ErrorType.NotFound     => new NotFoundObjectResult(error.Description),
            ErrorType.Conflict     => new ConflictObjectResult(error.Description),
            ErrorType.Forbidden    => new ForbidResult(error.Description),
            ErrorType.Unauthorized => new UnauthorizedObjectResult(error.Description),
            ErrorType.Failure      => new BadRequestObjectResult(error.Description),
            ErrorType.Unexpected   => new BadRequestObjectResult(error.Description),
            ErrorType.Validation   => new BadRequestObjectResult(error.Description),
            _                      => throw new ArgumentException($"Unknown error type {error.Type}")
        };
    }

}
