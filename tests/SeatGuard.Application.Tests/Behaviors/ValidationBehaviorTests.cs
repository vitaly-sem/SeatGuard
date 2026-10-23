using FluentAssertions;
using FluentValidation;
using MediatR;
using SeatGuard.Application.Common.Behaviors;
using SeatGuard.Application.Common.Exceptions;

namespace SeatGuard.Application.Tests.Behaviors;

public sealed class ValidationBehaviorTests
{
    private sealed record SampleRequest(string Name);

    private sealed class SampleValidator : AbstractValidator<SampleRequest>
    {
        public SampleValidator()
        {
            RuleFor(request => request.Name).NotEmpty().MaximumLength(5);
        }
    }

    [Fact]
    public async Task Valid_request_calls_the_next_delegate()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([new SampleValidator()]);
        var called = false;

        var response = await behavior.Handle(
            new SampleRequest("ok"),
            _ =>
            {
                called = true;
                return Task.FromResult("handled");
            },
            CancellationToken.None);

        called.Should().BeTrue();
        response.Should().Be("handled");
    }

    [Fact]
    public async Task Invalid_request_throws_a_camel_cased_error_dictionary()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([new SampleValidator()]);

        var action = () => behavior.Handle(
            new SampleRequest(string.Empty),
            _ => Task.FromResult("handled"),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<RequestValidationException>();
        exception.Which.Errors.Should().ContainKey("name");
    }
}
