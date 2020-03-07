using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Newtonsoft.Json;

namespace BuildTracer
{
    public class ProcessInfo
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

    public class RspFile
    {
        public String FileName { get; }
        public String Contents { get; }

        public RspFile(string fileName, string contents)
        {
            FileName = fileName;
            Contents = contents;
        }
    }

    public class Result
    {
        public List<ProcessInfo> InvokedProcesses { get; }
        public List<RspFile> RspFiles { get; }

        public Result(List<ProcessInfo> invokedProcesses, List<RspFile> rspFiles)
        {
            InvokedProcesses = invokedProcesses;
            RspFiles = rspFiles;
        }
    }

    class Program
    {
        private static Result TraceChildren(int rootPid)
        {
            Dictionary<int, string> subprocessCommandLines = new Dictionary<int, string>();
            subprocessCommandLines.Add(rootPid, "root");

            var fileWrites = new Dictionary<int, (HashSet<string> set, List<string> list)>();
            var fileReads = new Dictionary<int, (HashSet<string> set, List<string> list)>();
            fileWrites.Add(rootPid, (new HashSet<string>(), new List<string>()));
            fileReads.Add(rootPid, (new HashSet<string>(), new List<string>()));

            var openedFiles = new Dictionary<string, FileStream>();

            using var session = new TraceEventSession("BuildTracer");
            Console.CancelKeyPress += (sender, eventArgs) => session.Stop();

            Task _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                session.Stop();
            });

            session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.FileIOInit | KernelTraceEventParser.Keywords.FileIO | KernelTraceEventParser.Keywords.DiskFileIO);

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
                if (subprocessCommandLines.ContainsKey(createFile.ProcessID) && IsArgsFile(createFile.FileName))
                {
                    if (!openedFiles.ContainsKey(createFile.FileName))
                    {
                        try
                        {
                            var fileHandle = File.Open(
                                createFile.FileName, FileMode.Open, FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete);

                            openedFiles.Add(createFile.FileName, fileHandle);
                            
                            Console.WriteLine($"Create {createFile.FileName}, Share {createFile.ShareAccess}, Disposition: {createFile.CreateDispostion}, Options: {createFile.CreateOptions}");
                        }
                        catch (Exception e)
                        {
                        }
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

            foreach (var (path, stream) in openedFiles)
            {
                // This detects BOM/UTF8/UTF16
                var contents = new StreamReader(stream).ReadToEnd();
                rspFiles.Add(new RspFile(Path.GetFileName(path), contents));
                stream.Dispose();
            }

            return new Result(subprocessCommandLines.Select(kv =>
            {
                var pid = kv.Key;
                var cmdLine = kv.Value;
                return new ProcessInfo(cmdLine, fileReads[pid].list, fileWrites[pid].list);
            }).ToList(), 
                rspFiles);
        }

        static void Main(string[] args)
        {
            var result = TraceChildren(int.Parse(args[0]));
            var json = JsonConvert.SerializeObject(result, Formatting.Indented);
            File.WriteAllText("build_trace.json", json);
        }
    }
}
