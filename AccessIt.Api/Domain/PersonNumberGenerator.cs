namespace AccessIt.Api.Domain;

public static class PersonNumberGenerator
{
    public static string Create(PersonKind kind, long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        var prefix = kind == PersonKind.Employee ? 'E' : 'V';
        return $"{prefix}{sequence:D8}";
    }
}
