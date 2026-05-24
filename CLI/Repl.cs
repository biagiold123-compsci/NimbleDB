using NimbleDB.Exceptions;
using NimbleDB.Observability;

namespace NimbleDB.CLI;

public static class Repl
{
    public static void Run(Database db)
    {
        PrintBanner();
        PrintHelp();
        var history = new List<string>();
        bool logging = false;
        ConsoleLogger? logger = null;

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("\n  NimbleDB » ");
            Console.ResetColor();
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) continue;
            history.Add(input);

            if (input.StartsWith('.'))
            {
                HandleMeta(input, db, history, ref logging, ref logger);
                continue;
            }

            try
            {
                db.Query(input).Print();
            }
            catch (NimbleDbException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ERROR: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    private static void HandleMeta(string cmd, Database db, List<string> history,
        ref bool logging, ref ConsoleLogger? logger)
    {
        var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts[0].ToLower())
        {
            case ".quit":
            case ".exit":
                Console.WriteLine("\n  Goodbye.\n");
                Environment.Exit(0);
                break;
            case ".help":
                PrintHelp();
                break;
            case ".tables":
                foreach (var t in db.TableNames.OrderBy(x => x))
                    Console.WriteLine($"  {t}");
                break;
            case ".stats":
                db.PrintStats();
                break;
            case ".log":
                if (!logging) { logger = new ConsoleLogger(); EventBus.Instance.Subscribe(logger); logging = true; Console.WriteLine("  Logging ON"); }
                else { if (logger != null) EventBus.Instance.Unsubscribe(logger); logging = false; Console.WriteLine("  Logging OFF"); }
                break;
            case ".save":
                if (parts.Length < 2) { Console.WriteLine("  Usage: .save <path>"); break; }
                db.SaveTo(parts[1]); Console.WriteLine($"  Saved to '{parts[1]}'");
                break;
            case ".load":
                if (parts.Length < 2) { Console.WriteLine("  Usage: .load <path>"); break; }
                db.LoadFrom(parts[1]); Console.WriteLine($"  Loaded from '{parts[1]}'");
                break;
            case ".history":
                for (int i = 0; i < history.Count; i++)
                    Console.WriteLine($"  {i + 1,3}. {history[i]}");
                break;
            default:
                Console.WriteLine($"  Unknown command '{cmd}'.  Try .help");
                break;
        }
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
  ███╗   ██╗██╗███╗   ███╗██████╗ ██╗     ███████╗██████╗ ██████╗
  ████╗  ██║██║████╗ ████║██╔══██╗██║     ██╔════╝██╔══██╗██╔══██╗
  ██╔██╗ ██║██║██╔████╔██║██████╔╝██║     █████╗  ██║  ██║██████╔╝
  ██║╚██╗██║██║██║╚██╔╝██║██╔══██╗██║     ██╔══╝  ██║  ██║██╔══██╗
  ██║ ╚████║██║██║ ╚═╝ ██║██████╔╝███████╗███████╗██████╔╝██████╔╝
  ╚═╝  ╚═══╝╚═╝╚═╝     ╚═╝╚═════╝ ╚══════╝╚══════╝╚═════╝ ╚═════╝");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  In-Memory Relational Database Engine  v1.0.0\n");
        Console.ResetColor();
    }

    private static void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  ──────────────────────────────────────────────────────");
        Console.WriteLine("  SQL:  SELECT [cols|*] FROM <t> [WHERE ..] [ORDER BY ..] [LIMIT n]");
        Console.WriteLine("        INSERT INTO <t> (cols) VALUES (vals), ...");
        Console.WriteLine("        UPDATE <t> SET col=val [WHERE ..]");
        Console.WriteLine("        DELETE FROM <t> [WHERE ..]");
        Console.WriteLine("  Meta: .tables  .stats  .log  .save <path>  .load <path>");
        Console.WriteLine("        .history  .help  .quit");
        Console.WriteLine("  ──────────────────────────────────────────────────────");
        Console.ResetColor();
    }
}
