public static class Log
{
    public static void Header(string message)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {message} ===");
    }

    public static void SubHeader(string message)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {message} ---");
    }

    public static void Info(string message)
    {
        Console.WriteLine($"  {message}");
    }

    public static void Success(string message)
    {
        Console.WriteLine($"  ✓ {message}");
    }

    public static void Warning(string message)
    {
        Console.WriteLine($"  ⚠ {message}");
    }
}