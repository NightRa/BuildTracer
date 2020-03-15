using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Newtonsoft.Json;

namespace BuildTracer
{
    public sealed class ProcessInfo
    {
        public string CommandLine { get; }
        public List<string> FileReads { get; }
        public List<string> FileWrites { get; }

        public ProcessInfo(string commandLine, List<string> fileReads, List<string> fileWrites)
        {
            CommandLine = commandLine;
            FileReads = fileReads;
            FileWrites = fileWrites;
        }
    }

    public sealed class RspFile
    {
        public String FileName { get; }
        public String Contents { get; }

        public RspFile(string fileName, string contents)
        {
            FileName = fileName;
            Contents = contents;
        }
    }

    public class BuildTrace
    {
        public List<ProcessInfo> InvokedProcesses { get; }
        public List<RspFile> RspFiles { get; }

        public BuildTrace(List<ProcessInfo> invokedProcesses, List<RspFile> rspFiles)
        {
            InvokedProcesses = invokedProcesses;
            RspFiles = rspFiles;
        }
    }

    public class BuildCommands
    {
        public List<Command> Commands { get; }

        public BuildCommands(List<Command> commands)
        {
            Commands = commands;
        }
    }

    public sealed class Command
    {
        public String CommandLine { get; }
        public List<String> FileReads { get; }
        public List<String> FileWrites { get; }
        public RspFile? RspFile { get; }

        public Command(string commandLine, List<string> fileReads, List<string> fileWrites, RspFile? rspFile)
        {
            CommandLine = commandLine;
            FileReads = fileReads;
            FileWrites = fileWrites;
            RspFile = rspFile;
        }
    }


    public enum CreateFileType
    {
        Read,
        Write
    }

    class Program
    {
        private static BuildTrace TraceChildren(int rootPid)
        {
            Dictionary<int, string> subprocessCommandLines = new Dictionary<int, string>();
            subprocessCommandLines.Add(rootPid, "root");

            var fileWrites = new Dictionary<int, (HashSet<string> set, List<string> list)>();
            var fileReads = new Dictionary<int, (HashSet<string> set, List<string> list)>();
            fileWrites.Add(rootPid, (new HashSet<string>(), new List<string>()));
            fileReads.Add(rootPid, (new HashSet<string>(), new List<string>()));

            var argsFiles = new Dictionary<string, FileStream>();
            var lastCreateFileType = new Dictionary<(int pid, string path), CreateFileType>();

            using var session = new TraceEventSession("BuildTracer");
            Console.CancelKeyPress += (sender, eventArgs) => session.Stop();

            Task _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                session.Stop();
            });

            session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.FileIOInit | KernelTraceEventParser.Keywords.FileIO | KernelTraceEventParser.Keywords.DiskFileIO | KernelTraceEventParser.Keywords.VAMap);

            session.Source.Kernel.ProcessStart += processData =>
            {
                if (subprocessCommandLines.ContainsKey(processData.ParentID))
                {
                    subprocessCommandLines.Add(processData.ProcessID, processData.CommandLine);
                    fileReads.Add(processData.ProcessID, (new HashSet<string>(), new List<string>()));
                    fileWrites.Add(processData.ProcessID, (new HashSet<string>(), new List<string>()));

                    Console.WriteLine($"Start: PID {processData.ProcessID}, Process {processData.ProcessName}, Parent: {processData.ParentID}, Command line: {processData.CommandLine}");
                }
            };

            session.Source.Kernel.ProcessStop += processData =>
            {
                if (subprocessCommandLines.ContainsKey(processData.ParentID))
                {
                    // subprocessCommandLines.Remove(processData.ProcessID);
                    Console.WriteLine($"Stop: - PID {processData.ProcessID}, Process {processData.ProcessName}");
                }
            };

            static bool IsArgsFile(string path)
            {
                var fileName = Path.GetFileName(path);
                if (fileName == null)
                {
                    return false;
                }

                bool rspFile = fileName.StartsWith("tmp") && fileName.EndsWith("rsp");
                return rspFile;
            }

            session.Source.Kernel.FileIOCreate += createFile =>
            {
                if (subprocessCommandLines.ContainsKey(createFile.ProcessID))
                {
                    if (IsArgsFile(createFile.FileName) && !argsFiles.ContainsKey(createFile.FileName))
                    {
                        try
                        {
                            var fileHandle = File.Open(
                                createFile.FileName, FileMode.Open, FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete);

                            argsFiles.Add(createFile.FileName, fileHandle);
                            
                            Console.WriteLine($"Create {createFile.FileName}, Share {createFile.ShareAccess}, Disposition: {createFile.CreateDispostion}, Options: {createFile.CreateOptions}");
                        }
                        catch (Exception e)
                        {
                        }
                    }

                    if (createFile.FileName.EndsWith("obj"))
                    {
                        Console.WriteLine($"Create: {createFile}");
                    }

                    lastCreateFileType[(createFile.ProcessID, createFile.FileName)] =
                        (int) createFile.CreateDispostion == 1 /* CreateDisposition.OPEN_EXISING */ // Bug in TraceEvent - Open is 1.
                            ? CreateFileType.Read
                            : CreateFileType.Write;
                }
            };

            session.Source.Kernel.FileIOMapFile += fileMap =>
            {
                var fileName = fileMap.FileName;
                if (subprocessCommandLines.ContainsKey(fileMap.ProcessID) && fileName.Length > 0)
                {
                    Console.WriteLine($"Mapping {fileName}");

                    if (fileName.EndsWith("obj"))
                    {
                        Console.WriteLine(fileMap);
                    }

                    var dict = lastCreateFileType[(fileMap.ProcessID, fileName)] == CreateFileType.Read ? fileReads : fileWrites;

                    var (set, list) = dict[fileMap.ProcessID];
                    if (!set.Contains(fileName))
                    {
                        set.Add(fileName);
                        list.Add(fileName);
                    }
                }
            };

            session.Source.Kernel.FileIORead += fileRead =>
            {
                if (subprocessCommandLines.ContainsKey(fileRead.ProcessID))
                {
                    var (set, list) = fileReads[fileRead.ProcessID];
                    if (!set.Contains(fileRead.FileName))
                    {
                        set.Add(fileRead.FileName);
                        list.Add(fileRead.FileName);
                    }
                }
            };

            session.Source.Kernel.FileIOWrite += fileWrite =>
            {
                if (subprocessCommandLines.ContainsKey(fileWrite.ProcessID))
                {
                    var (set, list) = fileWrites[fileWrite.ProcessID];
                    if (!set.Contains(fileWrite.FileName))
                    {
                        set.Add(fileWrite.FileName);
                        list.Add(fileWrite.FileName);
                    }
                }
            };

            session.Source.Process();

            var rspFiles = new List<RspFile>();

            foreach (var (path, stream) in argsFiles)
            {
                // This detects BOM/UTF8/UTF16
                var contents = new StreamReader(stream).ReadToEnd();
                rspFiles.Add(new RspFile(path, contents));
                stream.Dispose();
            }

            return new BuildTrace(subprocessCommandLines.Select(kv =>
            {
                var pid = kv.Key;
                var cmdLine = kv.Value;
                return new ProcessInfo(cmdLine, fileReads[pid].list, fileWrites[pid].list);
            }).ToList(), 
                rspFiles);
        }

        public static BuildCommands PostProcess(BuildTrace buildTrace)
        {
            var rspFiles =
                buildTrace.RspFiles.ToDictionary(r => r.FileName, r => r);

            var commands = buildTrace.InvokedProcesses
                .Select(p => PostProcessPaths(p, rspFiles))
                .Where(p => p.FileWrites.Count > 0)
                .ToList();

            return new BuildCommands(commands);
        }

        public static bool FilterPath(String path)
        {
            return path.Length > 0 && !path.Contains(@"\Temp\") && !path.Contains(".tlog") && !path.EndsWith(".pf");
        }

        public static Command PostProcessPaths(ProcessInfo info, Dictionary<String, RspFile> rspFiles)
        {
            var writesSet = info.FileWrites.ToHashSet();
            var reads = info.FileReads.Where(input => !writesSet.Contains(input));
            var rspPath = info.FileReads.Find(path => path.EndsWith(".rsp"));

            RspFile? rspFile = null;
            if (rspPath != null)
            {
                rspFiles.TryGetValue(rspPath, out rspFile);
            }

            return new Command(info.CommandLine, reads.Where(FilterPath).ToList(), info.FileWrites.Where(FilterPath).ToList(), rspFile);
        }

        static void Main(string[] args)
        {
            Console.WriteLine($"Tracing PID {args[0]}");
            var result = TraceChildren(int.Parse(args[0]));
            var postProcessedResult = PostProcess(result);
            var json = JsonConvert.SerializeObject(postProcessedResult, Formatting.Indented);
            File.WriteAllText("build_trace.json", json);

            var ninja = NinjaTrace.CommandsToNinja(postProcessedResult.Commands);
            File.WriteAllText("build.ninja", ninja);
        }
    }
}
