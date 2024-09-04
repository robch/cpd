using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Azure.AI.Details.Common.CLI
{
    public class FileLineAndText
    {
        public FileLineAndText(string fileName, int lineNumber, string text)
        {
            FileName = fileName;
            LineNumber = lineNumber;
            Text = text;
        }

        public readonly string FileName;
        public readonly int LineNumber;
        public readonly string Text;

        public FileLineAndText? Prev = null;
        public FileLineAndText? Next = null;
    }

    public partial class Program
    {
        public static int Main(string[] args)
        {
            var files = FindFiles(args);
            if (files == null)
            {
                Console.WriteLine("CPD, Copy Paste Detective");
                Console.WriteLine();
                Console.WriteLine("  USAGE: cpd [-r] PATTERN");
                Console.WriteLine("     OR: cpd [-r] PATH\\PATTERN");
                Console.WriteLine();
                Console.WriteLine("  EXAMPLES");
                Console.WriteLine();
                Console.WriteLine("     cpd -r *.md");
                Console.WriteLine(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "     cpd -r d:\\src\\book-of-ai\\*.md"
                    : "     cpd -r ~/src/book-of-ai/*.md");
                return 1;
            }

            var fileLines = ReadFileLineAndTextFromFiles(files);
            var fileLineTextMap = CreateFileLineTextMap(fileLines);
            var fileNameMap = CreateFileNameMap(fileLines, fileLineTextMap);
            var line2Matches = Find2LineMatches(fileLineTextMap, fileNameMap);

            Print2LineMatches(fileLineTextMap, line2Matches);
            Print2LineSummary(fileLineTextMap, line2Matches);

            return 0;
        }

        private static void Print2LineSummary(Dictionary<string, List<FileLineAndText>> fileLineTextMap, Dictionary<FileLineAndText, List<FileLineAndText>> line2Matches)
        {
            var grouped = line2Matches
                .GroupBy(x => KeyFromText(x.Key.Text) + "\n" + KeyFromText(x.Key.Next?.Text))
                .OrderBy(g => g.First().Value.Count());
            foreach (var group in grouped)
            {
                var firstMatch = group.First();
                var count2Line = firstMatch.Value.Count();
                Console.WriteLine($"-----\n{count2Line + 1,4}");

                var line1 = firstMatch.Key;
                var countLine1 = fileLineTextMap[KeyFromText(line1.Text)].Count();
                Console.WriteLine($"[{countLine1,3}] {line1.Text}");

                var line2 = line1.Next;
                var countLine2 = fileLineTextMap[KeyFromText(line2?.Text)].Count();
                Console.WriteLine($"[{countLine2,3}] {line2?.Text}");
                Console.WriteLine($"-----");

                Console.WriteLine($"{firstMatch.Key.FileName}({firstMatch.Key.LineNumber})");
                foreach (var match in firstMatch.Value)
                {
                    Console.WriteLine($"{match.FileName}({match.LineNumber})");
                }
                Console.WriteLine($"\n");
            }
        }

        private static void Print2LineMatches(Dictionary<string, List<FileLineAndText>> fileLineTextMap, Dictionary<FileLineAndText, List<FileLineAndText>> line2Matches)
        {
            foreach (var line2Match in line2Matches)
            {
                var line1 = line2Match.Key;
                var line2 = line1.Next;

                var count2Line = line2Match.Value.Count();
                Console.WriteLine($"-----\n{count2Line + 1}");

                var countLine1 = fileLineTextMap[KeyFromText(line1.Text)].Count();
                Console.WriteLine($"[{countLine1,3}] {line1.FileName}({line1.LineNumber}): {line1.Text}");

                var countLine2 = fileLineTextMap[KeyFromText(line2?.Text)].Count();
                Console.WriteLine($"[{countLine2,3}] {line2?.FileName}({line2?.LineNumber}): {line2?.Text}");
            }
        }

        private static Dictionary<FileLineAndText, List<FileLineAndText>> Find2LineMatches(Dictionary<string, List<FileLineAndText>> fileLineTextMap, Dictionary<string, List<FileLineAndText>> fileNameMap)
        {
            var line2Matches = new Dictionary<FileLineAndText, List<FileLineAndText>>();
            foreach (var file in fileNameMap)
            {
                var lines = file.Value;
                foreach (var line in lines)
                {
                    if (line.LineNumber + 1 == line.Next?.LineNumber)
                    {
                        List<FileLineAndText>? list = null;

                        var key = KeyFromText(line.Text);
                        var matchesInOtherFiles = fileLineTextMap[key].Where(x =>
                            x.FileName != line.FileName ||
                            x.LineNumber != line.LineNumber);

                        foreach (var match in matchesInOtherFiles)
                        {
                            if (match.LineNumber + 1 == match.Next?.LineNumber)
                            {
                                var lineNextText = line.Next.Text;
                                var matchNextText = match.Next.Text;
                                if (KeyFromText(lineNextText) == KeyFromText(matchNextText) &&
                                    !string.IsNullOrWhiteSpace(line.Text) &&
                                    !string.IsNullOrWhiteSpace(lineNextText))
                                {
                                    if (list == null)
                                    {
                                        list = new List<FileLineAndText>();
                                    }
                                    list.Add(match);
                                }
                            }
                        }

                        if (list != null)
                        {
                            line2Matches.Add(line, list);
                        }
                    }
                }
            }

            return line2Matches;
        }

        private static string KeyFromText(string? text)
        {
            return text.Trim();
        }

        private static Dictionary<string, List<FileLineAndText>> CreateFileNameMap(IEnumerable<FileLineAndText> fileLines, Dictionary<string, List<FileLineAndText>> fileLineTextMap)
        {
            return fileLines
                .Where(x => fileLineTextMap[KeyFromText(x.Text)].Count() > 1)
                .GroupBy(x => x.FileName).Select(g => g.ToList()).ToDictionary(x => x.First().FileName);

        }

        private static Dictionary<string, List<FileLineAndText>> CreateFileLineTextMap(IEnumerable<FileLineAndText> items)
        {
            var map = new Dictionary<string, List<FileLineAndText>>();
            foreach (var item in items)
            {
                var key = KeyFromText(item.Text);
                if (!map.ContainsKey(key))
                {
                    map.Add(key, new List<FileLineAndText>());
                }
                map[key].Add(item);
            }

            return map;
        }

        private static IEnumerable<string>? FindFiles(string[] args)
        {
            var recursiveOptions = new EnumerationOptions() { RecurseSubdirectories = false };
            var triedToFindFiles = false;

            var list = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "/r" || arg == "-r")
                {
                    recursiveOptions.RecurseSubdirectories = true;
                    continue;
                }

                string path, pattern;
                if (arg.Contains(Path.DirectorySeparatorChar))
                {
                    var at = arg.LastIndexOf(Path.DirectorySeparatorChar);
                    path = arg.Substring(0, at);
                    pattern = arg.Substring(at + 1);
                }
                else
                {
                    path = ".";
                    pattern = arg;
                }

                path = Path.Combine(Directory.GetCurrentDirectory(), path);
                foreach (var file in Directory.EnumerateFiles(path, pattern, recursiveOptions))
                {
                    list.Add(Path.Combine(path, file));
                }

                triedToFindFiles = true;
            }

            return triedToFindFiles ? list : null;
        }

        public static IEnumerable<FileLineAndText> ReadFileLineAndTextFromFiles(IEnumerable<string> fileNames)
        {
            FileLineAndText? prev = null;
            foreach (var fileName in fileNames)
            {
                var lines = File.ReadAllLines(fileName);
                for (int i = 0; i < lines.Length; i++)
                {
                    var lineNumber = i + 1;
                    var lineText = lines[i];

                    var item = new FileLineAndText(fileName, lineNumber, lineText);
                    if (prev != null)
                    {
                        item.Prev = prev;
                        prev.Next = item;
                    }

                    prev = item;
                    yield return item;
                }
            }

            yield break;
        }
    }
}
