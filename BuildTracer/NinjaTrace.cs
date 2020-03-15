using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildTracer
{
    public sealed class NinjaTrace
    {
        public static void CommandToRule(NinjaSyntax ninja, Command command, String name)
        {
            if (command.RspFile == null)
            {
                ninja.Rule(name: name, command: command.CommandLine);
            }
            else
            {
                ninja.Rule(name: name, command: command.CommandLine, rspFile: command.RspFile.FileName, rspFileContent: command.RspFile.Contents);
            }

            ninja.Build(outputs: command.FileWrites, rule: name, inputs: command.FileReads,
                implicitInputs: NinjaSyntax.None, orderOnlyInputs: NinjaSyntax.None, variables: Enumerable.Empty<(String, String)>(),
                implicitOutputs: NinjaSyntax.None, pool: null);

            ninja.Newline();
        }
        
        public static String CommandsToNinja(List<Command> commands)
        {
            var ninja = new NinjaSyntax();
            foreach (var (command, idx) in commands.Select((c, idx) => (c, idx)))
            {
                CommandToRule(ninja, command, $"r{idx}");
            }

            return ninja.ToString();
        }
    }
}
