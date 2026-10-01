using CommandLine;

namespace ProteaseGuru.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        var exitCode = 1;
        Parser.Default.ParseArguments<CommandLineOptions>(args)
            .WithParsed(options => exitCode = CliRunner.Run(options))
            .WithNotParsed(_ => exitCode = 2);
        return exitCode;
    }
}
