using System.Linq;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace Takeaway.Api.Extensions;

public static class ValidationExtensions
{
    public static ValidationProblemDetails ToProblemDetails(this ValidationResult result)
    {
        var problemDetails = new ValidationProblemDetails();
        foreach (var error in result.Errors)
        {
            if (!problemDetails.Errors.TryGetValue(error.PropertyName, out var existing))
            {
                problemDetails.Errors[error.PropertyName] = new[] { error.ErrorMessage };
            }
            else
            {
                problemDetails.Errors[error.PropertyName] = existing.Append(error.ErrorMessage).ToArray();
            }
        }

        problemDetails.Title = "Validation failed";
        problemDetails.Status = StatusCodes.Status400BadRequest;
        return problemDetails;
    }
}
