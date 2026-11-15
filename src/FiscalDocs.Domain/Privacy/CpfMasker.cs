namespace FiscalDocs.Domain.Privacy;

public static class CpfMasker
{
    private const string FullMask = "***";
    private const int CpfLength = 11;

    public static string Mask(string? cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf))
            return FullMask;

        Span<char> digits = stackalloc char[CpfLength];
        var count = 0;

        foreach (var c in cpf)
        {
            // Não usar char.IsDigit: aceita dígitos Unicode de outros alfabetos.
            if (c is < '0' or > '9')
                continue;

            if (count == CpfLength)
                return FullMask;

            digits[count++] = c;
        }

        if (count != CpfLength)
            return FullMask;

        return $"***.{digits.Slice(3, 3)}.{digits.Slice(6, 3)}-**";
    }
}
