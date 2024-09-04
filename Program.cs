using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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
        private static Dictionary<int, Regex> _regexMap = new Dictionary<int, Regex>();
        private static int _linesToCompare = 2;

        public static int Main(string[] args)
        {
            var files = FindFiles(args);
            if (files == null)
            {
                Console.WriteLine("CPD, Copy Paste Detective");
                Console.WriteLine();
                Console.WriteLine("  USAGE: cpd [-n LINES] [-# REGEX] [-r] PATTERN");
                Console.WriteLine("     OR: cpd [-n LINES] [-# REGEX] [-r] PATH\\PATTERN");
                Console.WriteLine();
                Console.WriteLine("  EXAMPLES");
                Console.WriteLine();
                Console.WriteLine("     cpd -r *.md -n 3 -1 \"```.*bash\"");
                Console.WriteLine(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "     cpd -r d:\\src\\book-of-ai\\*.md"
                    : "     cpd -r ~/src/book-of-ai/*.md");
                return 1;
            }

            var fileLines = ReadFileLineAndTextFromFiles(files);
            var fileLineTextMap = CreateFileLineTextMap(fileLines);
            var fileNameMap = CreateFileNameMap(fileLines, fileLineTextMap);
            var lineMatches = FindLineMatches(fileLineTextMap, fileNameMap, _linesToCompare);

            // PrintLineMatches(fileLineTextMap, lineMatches, _linesToCompare);
            PrintLineSummary(fileLineTextMap, lineMatches, _linesToCompare);

            return 0;
        }

        private static void PrintLineSummary(
            Dictionary<string, List<FileLineAndText>> fileLineTextMap, 
            Dictionary<FileLineAndText, List<FileLineAndText>> lineMatches, 
            int linesToCompare)
        {
            var grouped = lineMatches
                .GroupBy(x =>
                    string.Join("\n", Enumerable.Range(0, linesToCompare)
                        .Select(i => KeyFromText(GetNthLine(x.Key, i)?.Text))))
                .OrderBy(g => g.First().Value.Count());
            foreach (var group in grouped)
            {
                var firstMatch = group.First();
                var countMatches = firstMatch.Value.Count();
                Console.WriteLine($"-----\n{countMatches + 1,5}");

                for (int i = 0; i < linesToCompare; i++)
                {
                    var line = GetNthLine(firstMatch.Key, i);
                    var countLine = fileLineTextMap[KeyFromText(line?.Text)].Count();
                    Console.WriteLine($"[{countLine,4}] {line?.Text}");
                }

                Console.WriteLine($"-----");
                Console.WriteLine($"{firstMatch.Key.FileName}({firstMatch.Key.LineNumber})");
                foreach (var match in firstMatch.Value)
                {
                    Console.WriteLine($"{match.FileName}({match.LineNumber})");
                }
                Console.WriteLine($"\n");
            }
        }

        private static FileLineAndText? GetNthLine(FileLineAndText line, int n)
        {
            var current = line;
            for (int i = 0; i < n; i++)
            {
                current = current?.Next;
            }
            return current;
        }

        private static void PrintLineMatches(
            Dictionary<string, List<FileLineAndText>> fileLineTextMap, 
            Dictionary<FileLineAndText, List<FileLineAndText>> lineMatches, 
            int linesToCompare)
        {
            foreach (var lineMatch in lineMatches)
            {
                var countMatches = lineMatch.Value.Count();
                Console.WriteLine($"-----\n{countMatches + 1}");

                for (int i = 0; i < linesToCompare; i++)
                {
                    var line = GetNthLine(lineMatch.Key, i);
                    var countLine = fileLineTextMap[KeyFromText(line?.Text)].Count();
                    Console.WriteLine($"[{countLine,4}] {line?.FileName}({line?.LineNumber}): {line?.Text}");
                }
            }
        }

        private static Dictionary<FileLineAndText, List<FileLineAndText>> FindLineMatches(
            Dictionary<string, List<FileLineAndText>> fileLineTextMap, 
            Dictionary<string, List<FileLineAndText>> fileNameMap, 
            int linesToCompare)
        {
            var lineMatches = new Dictionary<FileLineAndText, List<FileLineAndText>>();
            foreach (var file in fileNameMap)
            {
                var lines = file.Value;
                foreach (var line in lines)
                {
                    if (IsNthLineValid(line, linesToCompare))
                    {
                        List<FileLineAndText>? list = null;
                        var key = KeyFromText(line.Text);
                        var matchesInOtherFiles = fileLineTextMap[key].Where(x =>
                            x.FileName != line.FileName ||
                            x.LineNumber != line.LineNumber);

                        foreach (var match in matchesInOtherFiles)
                        {
                            if (IsNthLineMatch(line, match, linesToCompare))
                            {
                                if (list == null)
                                {
                                    list = new List<FileLineAndText>();
                                }
                                list.Add(match);
                            }
                        }

                        if (list != null)
                        {
                            lineMatches.Add(line, list);
                        }
                    }
                }
            }

            return lineMatches;
        }

        private static bool IsNthLineValid(FileLineAndText line, int n)
        {
            var current = line;
            for (int i = 0; i < n; i++)
            {
                if (current?.Next == null) return false;
                current = current.Next;
            }
            return true;
        }

        private static bool IsNthLineMatch(FileLineAndText? line1, FileLineAndText? line2, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (line1 == null || line2 == null)
                {
                    return false;
                }
                if (KeyFromText(line1.Text) != KeyFromText(line2.Text))
                {
                    return false;
                }
                if (_regexMap.TryGetValue(i + 1, out var regex))
                {
                    if (!regex.IsMatch(line1.Text) || !regex.IsMatch(line2.Text))
                    {
                        return false;
                    }
                }
                line1 = line1.Next;
                line2 = line2.Next;
            }
            return true;
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

                if ((arg == "/n" || arg == "-n") && i + 1 < args.Length)
                {
                    _linesToCompare = int.Parse(args[++i]);
                    continue;
                }

                if ((arg.StartsWith("-") || arg.StartsWith("/")))
                {
                    if (int.TryParse(arg.Substring(1), out var n) && i + 1 < args.Length)
                    {
                        _regexMap[n] = new Regex(args[++i]);
                        continue;
                    }
                    Console.WriteLine($"Unknown option: {arg}");
                    return null;
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