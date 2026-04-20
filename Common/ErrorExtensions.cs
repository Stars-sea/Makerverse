using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Common;

public static class ErrorExtensions {

    public static ActionResult ToActionResult(this Error error) {
        return error.Type switch {
            ErrorType.NotFound     => new NotFoundObjectResult(error.Description),
            ErrorType.Conflict     => new ConflictObjectResult(error.Description),
            ErrorType.Forbidden    => new ObjectResult(error.Description) { StatusCode = StatusCodes.Status403Forbidden },
            ErrorType.Unauthorized => new UnauthorizedObjectResult(error.Description),
            ErrorType.Failure      => new ObjectResult(error.Description) { StatusCode = StatusCodes.Status500InternalServerError },
            ErrorType.Unexpected   => new ObjectResult(error.Description) { StatusCode = StatusCodes.Status500InternalServerError },
            ErrorType.Validation   => new BadRequestObjectResult(error.Description),
            _                      => throw new ArgumentException($"Unknown error type {error.Type}")
        };
    }

}
