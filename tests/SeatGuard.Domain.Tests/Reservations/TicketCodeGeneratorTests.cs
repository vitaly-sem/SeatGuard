using FluentAssertions;
using SeatGuard.Domain.Reservations;

namespace SeatGuard.Domain.Tests.Reservations;

public sealed class TicketCodeGeneratorTests
{
    [Fact]
    public void Generate_produces_prefixed_unique_codes()
    {
        var codes = Enumerable.Range(0, 1_000).Select(_ => TicketCodeGenerator.Generate()).ToList();

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(code => code.StartsWith("SG-", StringComparison.Ordinal) && code.Length == 23);
    }
}
