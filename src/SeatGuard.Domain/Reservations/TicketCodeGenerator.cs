using System.Security.Cryptography;

namespace SeatGuard.Domain.Reservations;

public static class TicketCodeGenerator
{
    private const int EntropyBytes = 10;

    /// <summary>
    /// Generates a 20-character hexadecimal code with 80 bits of entropy. The
    /// database enforces uniqueness, this only produces the candidate value.
    /// </summary>
    public static string Generate()
        => $"SG-{Convert.ToHexString(RandomNumberGenerator.GetBytes(EntropyBytes))}";
}
