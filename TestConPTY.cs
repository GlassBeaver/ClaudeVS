using System;
using System.Threading;

namespace ClaudeVS
{
    class TestConPTY
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Testing ConPTY terminal...");

            var terminal = new ConPtyTerminal(30, 120);

            terminal.OutputReceived += (sender, output) =>
            {
                Console.Write($"[OUTPUT] {output}");
            };

            terminal.ProcessExited += (sender, exitCode) =>
            {
                Console.WriteLine($"\n[PROCESS EXITED] Code: {exitCode}");
            };

            bool success = terminal.Initialize();
            Console.WriteLine($"Initialize returned: {success}");

            if (success)
            {
                Console.WriteLine("Terminal initialized successfully! Waiting 5 seconds...");
                Thread.Sleep(5000);

                Console.WriteLine("Sending 'dir' command...");
                terminal.WriteInput("dir\r\n");

                Thread.Sleep(2000);

                Console.WriteLine("Test complete.");
            }
            else
            {
                Console.WriteLine("FAILED to initialize terminal!");
            }

            terminal.Dispose();
        }
    }
}
