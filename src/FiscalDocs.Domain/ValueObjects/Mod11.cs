namespace FiscalDocs.Domain.ValueObjects;

internal static class Mod11
{
    public static int ComputeCheckDigit(ReadOnlySpan<char> chars)
    {
        var sum = 0;
        var weight = 2;

        for (var i = chars.Length - 1; i >= 0; i--)
        {
            sum += (chars[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}