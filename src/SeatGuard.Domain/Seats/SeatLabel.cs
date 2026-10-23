using System.Text;

namespace SeatGuard.Domain.Seats;

public static class SeatLabel
{
    /// <summary>
    /// Converts a zero-based row index into a spreadsheet-style label:
    /// 0 =&gt; A, 25 =&gt; Z, 26 =&gt; AA.
    /// </summary>
    public static string RowLabel(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var builder = new StringBuilder(2);
        var current = index;

        do
        {
            builder.Insert(0, (char)('A' + (current % 26)));
            current = (current / 26) - 1;
        }
        while (current >= 0);

        return builder.ToString();
    }

    public static string Compose(string section, string row, int number)
        => $"{section} · {row}{number}";
}
