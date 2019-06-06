using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SpaceChemTool
{
    class Program
    {
        static string Version = "0.14.2";

        static void CopyStream(Stream destination, Stream source)
        {
            byte[] buffer = new byte[1024];
            int length;
            while ((length = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                destination.Write(buffer, 0, length);
            }
            destination.Flush();
        }

        static string DecodeDefinition(string value)
        {
            GZipStream source = new GZipStream(new MemoryStream(Convert.FromBase64String(value)), CompressionMode.Decompress);
            MemoryStream result = new MemoryStream();
            CopyStream(result, source);
            return Encoding.ASCII.GetString(result.ToArray());
        }

        static string EncodeDefinition(string value)
        {
            byte[] source = Encoding.ASCII.GetBytes(value);
            MemoryStream destination = new MemoryStream();
            GZipStream compressor = new GZipStream(destination, CompressionMode.Compress);
            compressor.Write(source, 0, source.Length);
            compressor.Close();
            string result = Convert.ToBase64String(destination.ToArray());
            string test = DecodeDefinition(result);
            if (test != value) throw new Exception("Encode+Decode of puzzle definition did not return original definition");
            return result;
        }

        static string ReplaceAuthor(string puzzleCode, string newAuthor)
        {
            string tag = "\"author\":\"";
            int authorStart = puzzleCode.IndexOf(tag) + tag.Length;
            int authorEnd = puzzleCode.IndexOf('\"', authorStart);
            return string.Format("{0}{1}{2}", puzzleCode.Substring(0, authorStart), newAuthor, puzzleCode.Substring(authorEnd));
        }

        static string RemoveInputCounts(string puzzleCode)
        {
            string result = puzzleCode;
            while (result.IndexOf("\"count\":") >= 0 && result.IndexOf("\"count\":") < result.IndexOf("\"output-zones\":"))
            {
                int countStart = result.IndexOf("\"count\":");
                int countEnd = result.IndexOf('}', countStart);
                result = result.Substring(0, countStart) + result.Substring(countEnd);
            }
            return result;
        }

        // Determines whether two puzzle codes match sufficiently to be considered the same puzzle
        // * Different random input ratios is ignored
        // * Different authors is ignored (author changes from puzzle author to solution author in the export process)
        static bool DefinitionsMatch(string a, string b)
        {
            string decodedA = DecodeDefinition(a);
            string decodedB = DecodeDefinition(b);
            return ReplaceAuthor(RemoveInputCounts(decodedA), "Test") == ReplaceAuthor(RemoveInputCounts(decodedB), "Test");
        }

        static int WaldoPath(string[] solution)
        {
            List<List<string>> reactors = new List<List<string>>();
            foreach (string line in solution)
            {
                if (line.StartsWith("COMPONENT:")) reactors.Add(new List<string>());
                else if (line.StartsWith("MEMBER:")) reactors[reactors.Count - 1].Add(line);
            }

            int result = 0;
            foreach (List<string> reactor in reactors)
            {
                bool[,] waldopath = new bool[8, 10];
                for (int arrowLayer = 16; arrowLayer <= 64; arrowLayer *= 4)
                {
                    // Ignore unused waldos
                    bool waldoUsed = false;
                    foreach (string line in reactor)
                    {
                        if (line.StartsWith("MEMBER:'instr-") && !line.StartsWith("MEMBER:'instr-start'"))
                        {
                            int layer = int.Parse(line.Split(',')[3]);
                            if (layer == arrowLayer || layer == 2 * arrowLayer) waldoUsed = true;
                        }
                    }
                    if (!waldoUsed) continue;

                    int[,] arrowDirections = new int[8, 10];
                    int[,] switchDirections = new int[8, 10];
                    for (int y = 0; y < 8; ++y) for (int x = 0; x < 10; ++x) arrowDirections[y, x] = switchDirections[y, x] = -1;
                    Stack<int> paths = new Stack<int>();
                    foreach (string line in reactor)
                    {
                        int layer = int.Parse(line.Split(',')[3]);
                        int x = int.Parse(line.Split(',')[4]);
                        int y = int.Parse(line.Split(',')[5]);
                        int o = int.Parse(line.Split(',')[1]);
                        int d = o == 90 ? 1 : o == 180 ? 2 : o == -90 ? 3 : 0;
                        if (layer == arrowLayer)
                        {
                            if (line.StartsWith("MEMBER:'instr-arrow'")) arrowDirections[y, x] = d;
                        }
                        else if (layer == arrowLayer * 2)
                        {
                            if (line.StartsWith("MEMBER:'instr-sensor'") || line.StartsWith("MEMBER:'instr-toggle'")) switchDirections[y, x] = d;
                            else if (line.StartsWith("MEMBER:'instr-start'")) paths.Push(x + (y << 8) + (d << 16));
                        }
                    }
                    bool[, ,] visited = new bool[4, 8, 10];
                    while (paths.Count != 0)
                    {
                        int p = paths.Pop();
                        int x = p & 255;
                        int y = (p >> 8) & 255;
                        int d = p >> 16;
                        while (!visited[d, y, x])
                        {
                            waldopath[y, x] = true;
                            visited[d, y, x] = true;
                            if (switchDirections[y, x] == d && arrowDirections[y, x] != -1 && arrowDirections[y, x] != d)
                            {
                                paths.Push(x + (y << 8) + (arrowDirections[y, x] << 16));
                            }
                            else
                            {
                                if (switchDirections[y, x] != -1 && switchDirections[y, x] != d && switchDirections[y, x] != arrowDirections[y, x])
                                {
                                    paths.Push(x + (y << 8) + (switchDirections[y, x] << 16));
                                }
                                if (arrowDirections[y, x] != -1 && arrowDirections[y, x] != d)
                                {
                                    d = arrowDirections[y, x];
                                    continue;
                                }
                            }

                            if (d == 0 && x < 9) ++x;
                            else if (d == 1 && y < 7) ++y;
                            else if (d == 2 && x > 0) --x;
                            else if (d == 3 && y > 0) --y;
                        }
                    }
                }
                foreach (bool included in waldopath) result += included ? 1 : 0;
            }
            return result;
        }

        static void DisplayExtendedStats(string[] solution)
        {
            string[] instructions = { "instr-arrow", "instr-grab", "instr-fuse", "instr-split", "instr-input", "instr-output", "instr-swap", "instr-sync", "instr-toggle", "instr-rotate", "instr-bond", "instr-sensor" };
            foreach (string instruction in instructions)
            {
                int count = 0;
                foreach (string line in solution) if (line.StartsWith(string.Format("MEMBER:'{0}'", instruction))) ++count;
                Console.WriteLine("      {0,-15} {1}", instruction, count);
            }
            Console.WriteLine("      {0,-15} {1}", "waldopath", WaldoPath(solution));
        }

        static void AddPuzzles(string saveFilename, string puzzleDirectory)
        {
            Console.WriteLine("Adding puzzles from {0} to {1}:", puzzleDirectory, saveFilename);
            SQLiteConnection connection = new SQLiteConnection(string.Format("Data Source={0};", saveFilename));
            connection.Open();
            long levelIdTicks = DateTime.UtcNow.Ticks;
            foreach (string puzzleFilename in Directory.GetFiles(puzzleDirectory, "*.puzzle"))
            {
                string puzzleName = Path.GetFileNameWithoutExtension(puzzleFilename);
                bool hasImages = File.Exists(Path.Combine(puzzleDirectory, string.Format("{0}.images", puzzleName)));
                Console.WriteLine(hasImages ? "  Adding '{0}' (reactor images exist for this puzzle)" : "  Adding '{0}'", puzzleName);
                string puzzleCode = File.ReadAllText(puzzleFilename);
                new SQLiteCommand(string.Format("INSERT INTO ResearchNet VALUES ('custom-{0}',datetime('now'),'{1}')", levelIdTicks++, puzzleCode), connection).ExecuteNonQuery();
            }
        }

        static string FindRoundFromPuzzle(string tournamentPath, string puzzleName)
        {
            Console.WriteLine("Searching for round containing {0}", puzzleName);
            foreach (string puzzlePath in Directory.GetDirectories(tournamentPath))
            {
                bool found = File.Exists(Path.Combine(puzzlePath, puzzleName + ".puzzle"));
                Console.WriteLine("  Round {0} - {1}", Path.GetFileName(puzzlePath), found ? "found" : "no");
                if (found)
                {
                    return Path.GetFileName(puzzlePath);
                }
            }
            throw new Exception("Unable to find image definition");
        }

        static int ReadSolutions(List<string> solutions, SQLiteConnection connection, string puzzleName, string puzzleCode, string filterString, string solverName, bool swapWaldos, bool extendedStats)
        {
            string[] filter = filterString.Split('-');
            if (filter.Length != 3) throw new Exception(string.Format("Invalid filter '{0}' - should be of form [cycles]-[reactors]-[symbols]", filterString));
            SQLiteDataReader levels = new SQLiteCommand("SELECT id, passed, cycles, symbols, reactors, definition FROM Level INNER JOIN ResearchNet ON id=level_id", connection).ExecuteReader();
            int numFound = 0;
            while (levels.Read())
            {
                if (DefinitionsMatch(puzzleCode, (string)levels["definition"]))
                {
                    SQLiteDataReader components = new SQLiteCommand(string.Format("SELECT rowid,type,x,y,name FROM component WHERE level_id='{0}' ORDER BY rowid", levels["id"]), connection).ExecuteReader();
                    List<string> solution = new List<string>();
                    int numReactors = 0;
                    int numSymbols = 0;
                    while (components.Read())
                    {
                        solution.Add(string.Format("COMPONENT:'{0}',{1},{2},'{3}'", components["type"], components["x"], components["y"], components["name"]));
                        if (((string)components["type"]).EndsWith("-reactor")) ++numReactors;
                        SQLiteDataReader members = new SQLiteCommand(string.Format("SELECT type,arrow_dir,choice,layer,x,y,element_type,element FROM Member WHERE component_id={0} ORDER BY rowid", components["rowid"]), connection).ExecuteReader();
                        while (members.Read())
                        {
                            long layer = (long)members["layer"];
                            long swappedLayer = (15 & layer) | ((48 & layer) << 2) | ((192 & layer) >> 2);
                            solution.Add(string.Format("MEMBER:'{0}',{1},{2},{3},{4},{5},{6},{7}", members["type"], members["arrow_dir"], members["choice"], swapWaldos ? swappedLayer : layer, members["x"], members["y"], members["element_type"], members["element"]));
                            if (layer > 15 && "instr-start" != (string)members["type"]) ++numSymbols;
                        }
                        SQLiteDataReader pipes = new SQLiteCommand(string.Format("SELECT output_id,x,y FROM Pipe WHERE component_id={0} ORDER BY rowid", components["rowid"]), connection).ExecuteReader();
                        while (pipes.Read())
                        {
                            solution.Add(string.Format("PIPE:{0},{1},{2}", pipes["output_id"], pipes["x"], pipes["y"]));
                        }
                        SQLiteDataReader annotations = new SQLiteCommand(string.Format("SELECT output_id,expanded,x,y,annotation FROM Annotation  WHERE component_id={0}", components["rowid"]), connection).ExecuteReader();
                        while (annotations.Read())
                        {
                            string annotation = ((string)annotations["annotation"]).Replace("\n", "\\n").Replace("\r", "\\r");
                            solution.Add(string.Format("ANNOTATION:{0},{1},{2},{3},'{4}'", annotations["output_id"], (bool)annotations["expanded"] ? 1 : 0, annotations["x"], annotations["y"], annotation));
                        }
                    }
                    bool complete = (bool)levels["passed"] && (int)levels["reactors"] == numReactors && (int)levels["symbols"] == numSymbols;
                    string stats = string.Format("{0}-{1}-{2}", complete ? levels["cycles"] : "Incomplete", numReactors, numSymbols);
                    if ((bool)levels["passed"] && (int)levels["reactors"] == numReactors && (int)levels["symbols"] == numSymbols)
                    {
                        if (filter[0].ToUpper() != "INCOMPLETE" &&
                            (filter[0] == "*" || int.Parse(filter[0]) == (int)levels["cycles"]) &&
                            (filter[1] == "*" || int.Parse(filter[1]) == (int)levels["reactors"]) &&
                            (filter[2] == "*" || int.Parse(filter[2]) == (int)levels["symbols"]))
                        {
                            Console.WriteLine("    Exporting {0}", stats);
                            if (extendedStats) DisplayExtendedStats(solution.ToArray());
                            solutions.Add(string.Format("SOLUTION:{0},{1},{2}", puzzleName, solverName, stats));
                            solutions.AddRange(solution);
                            ++numFound;
                        }
                        else
                        {
                            Console.WriteLine("    Skipping {0}", stats);
                        }
                    }
                    else
                    {
                        if (filter[0].ToUpper() == "INCOMPLETE" && (filter[1] == "*" || int.Parse(filter[1]) == numReactors) &&
                                (filter[2] == "*" || int.Parse(filter[2]) == numSymbols))
                        {
                            Console.WriteLine("    Exporting {0}", stats);
                            if (extendedStats) DisplayExtendedStats(solution.ToArray());
                            solutions.Add(string.Format("SOLUTION:{0},{1},{2}", puzzleName, solverName, stats));
                            solutions.AddRange(solution);
                            ++numFound;
                        }
                        else if (filter[0].ToUpper() != "INCOMPLETE" && (bool)levels["passed"] &&
                            (filter[0] == "*" || int.Parse(filter[0]) == (int)levels["cycles"]) &&
                            (filter[1] == "*" || int.Parse(filter[1]) == (int)levels["reactors"]) &&
                            (filter[2] == "*" || int.Parse(filter[2]) == (int)levels["symbols"]))
                        {
                            Console.WriteLine("    Skipping {0} (last completion stats {1}-{2}-{3})",
                                stats, levels["cycles"], levels["reactors"], levels["symbols"]);
                        }
                        else
                        {
                            Console.WriteLine("    Skipping {0}", stats);
                        }
                    }
                }
            }
            return numFound;
        }

        static void WriteSolutions(IEnumerable<string> solutions, SQLiteConnection connection, string puzzleDirectory, bool setSolver)
        {
            SQLiteTransaction transaction = null;
            string currentLevel = null;
            long levelIdTicks = DateTime.UtcNow.Ticks;
            try
            {
                foreach (string line in solutions)
                {
                    int separator = line.IndexOf(':');
                    if (separator < 0) throw new Exception(string.Format("Invalid solution line '{0}'", line));
                    string command = line.Substring(0, separator);
                    string arguments = line.Substring(1 + separator);

                    switch (command)
                    {
                        case "SOLUTION":
                            {
                                // Attempt at working around bug seen on MacOS
                                // Reduce chance of garbage collection occuring during a call to SQLite
                                GC.Collect();
                                GC.WaitForPendingFinalizers();

                                string puzzleName = arguments.Split(',')[0];
                                string solver = arguments.Split(',')[1];
                                string originalPuzzleCode = File.ReadAllText(Path.Combine(puzzleDirectory, string.Format("{0}.puzzle", puzzleName)));
                                string newPuzzleCode = setSolver ? EncodeDefinition(ReplaceAuthor(DecodeDefinition(originalPuzzleCode), solver)) : originalPuzzleCode;
                                currentLevel = string.Format("custom-{0}", levelIdTicks++);
                                Console.WriteLine("  Importing solution to {0} by {1} ({2})", arguments.Split(',')[0], arguments.Split(',')[1], arguments.Split(',')[2]);
                                if (transaction != null) transaction.Commit();
                                transaction = connection.BeginTransaction();
                                new SQLiteCommand(string.Format("INSERT INTO ResearchNet VALUES ('{0}',datetime('now'),'{1}')", currentLevel, newPuzzleCode), connection, transaction).ExecuteNonQuery();
                                new SQLiteCommand(string.Format("INSERT INTO Level VALUES ('{0}',0,0,0,0,0,2147483647,2147483647,2147483647)", currentLevel), connection, transaction).ExecuteNonQuery();
                            }
                            break;
                        case "COMPONENT":
                            new SQLiteCommand(string.Format("INSERT INTO Component VALUES (NULL,'{0}',{1},200,255,0)", currentLevel, arguments), connection, transaction).ExecuteNonQuery();
                            break;
                        case "MEMBER":
                            new SQLiteCommand(string.Format("INSERT INTO Member SELECT NULL,seq,{0} FROM sqlite_sequence WHERE name='Component'", arguments), connection, transaction).ExecuteNonQuery();
                            break;
                        case "PIPE":
                            new SQLiteCommand(string.Format("INSERT INTO Pipe SELECT seq,{0} FROM sqlite_sequence WHERE name='Component'", arguments), connection, transaction).ExecuteNonQuery();
                            break;
                        case "ANNOTATION":
                            new SQLiteCommand(string.Format("INSERT INTO Annotation SELECT seq,{0} FROM sqlite_sequence WHERE name='Component'", arguments.Replace("\\r", "\r").Replace("\\n", "\n")), connection, transaction).ExecuteNonQuery();
                            break;
                    }
                }
                if (transaction != null) transaction.Commit();
            }
            catch (Exception e)
            {
                if (transaction != null) transaction.Rollback();
                throw e;
            }
        }

        static void ExportSolutions(string saveFilename, string tournamentPath, string puzzleOrRound, string filterString, string solverName)
        {
            Console.WriteLine("Extracting solutions from {0} for puzzle or round {1} in {2} with filter {3}", saveFilename, puzzleOrRound, tournamentPath, filterString);
            string puzzleDirectory, puzzleSearch, exportName = null;
            if (Directory.Exists(Path.Combine(tournamentPath, puzzleOrRound)))
            {
                puzzleDirectory = Path.Combine(tournamentPath, puzzleOrRound);
                puzzleSearch = "*.puzzle";
                int suffix = 0;
                while (suffix == 0 || File.Exists(Path.Combine(puzzleDirectory, exportName)))
                {
                    exportName = string.Format("exported{0:d3}.txt", suffix++);
                }
            }
            else
            {
                puzzleDirectory = Path.Combine(tournamentPath, FindRoundFromPuzzle(tournamentPath, puzzleOrRound));
                puzzleSearch = puzzleOrRound + ".puzzle";
                int suffix = 0;
                while (suffix == 0 || File.Exists(Path.Combine(puzzleDirectory, exportName)))
                {
                    exportName = string.Format("exported_{0}{1:d3}.txt", puzzleOrRound, suffix++);
                }
            }
            SQLiteConnection connection = new SQLiteConnection(string.Format("Data Source={0};", saveFilename));
            connection.Open();
            System.Console.WriteLine("Exporting to {0}", Path.Combine(puzzleDirectory, exportName));
            StreamWriter export = new StreamWriter(Path.Combine(puzzleDirectory, exportName));
            foreach (string puzzleFilename in Directory.GetFiles(puzzleDirectory, puzzleSearch))
            {
                string puzzleName = Path.GetFileNameWithoutExtension(puzzleFilename);
                Console.WriteLine("  Searching for solutions to '{0}'", puzzleName);
                string puzzleCode = File.ReadAllText(puzzleFilename);
                List<string> solutions = new List<string>();
                ReadSolutions(solutions, connection, puzzleName, puzzleCode, filterString, solverName, false, false);
                foreach (string line in solutions) export.WriteLine(line);
            }
            export.Close();
        }

        static void ImportSolutions(string saveFilename, string puzzleDirectory)
        {
            Console.WriteLine("Importing solutions for puzzles in {0} to {1}", puzzleDirectory, saveFilename);
            SQLiteConnection connection = new SQLiteConnection(string.Format("Data Source={0};", saveFilename));
            connection.Open();
            WriteSolutions(File.ReadAllLines(Path.Combine(puzzleDirectory, "solutions.txt")), connection, puzzleDirectory, true);
        }

        static void CopyPuzzle(string saveFilename, string tournamentPath, string puzzleName, string filterString, bool swapWaldos)
        {
            Console.WriteLine("Copying solution for puzzle {0} with filter {1}", puzzleName, filterString);
            string puzzleDirectory = Path.Combine(tournamentPath, FindRoundFromPuzzle(tournamentPath, puzzleName));
            string puzzleFilename = Path.Combine(puzzleDirectory, puzzleName + ".puzzle");
            SQLiteConnection connection = new SQLiteConnection(string.Format("Data Source={0};", saveFilename));
            connection.Open();
            List<string> solutions = new List<string>();
            int numFound = ReadSolutions(solutions, connection, puzzleName, File.ReadAllText(puzzleFilename), filterString, "Copying", swapWaldos, false);
            if (numFound == 1)
            {
                WriteSolutions(solutions, connection, puzzleDirectory, false);
            }
            else
            {
                Console.WriteLine("Puzzle not copied - {0} solutions found", numFound);
            }
        }

        static void ExtendedStats(string saveFilename, string tournamentPath, string puzzleName, string filterString)
        {
            Console.WriteLine("Calculating extended stats of solution(s) for puzzle {0} with filter {1}", puzzleName, filterString);
            string puzzleDirectory = Path.Combine(tournamentPath, FindRoundFromPuzzle(tournamentPath, puzzleName));
            string puzzleFilename = Path.Combine(puzzleDirectory, puzzleName + ".puzzle");
            SQLiteConnection connection = new SQLiteConnection(string.Format("Data Source={0};", saveFilename));
            connection.Open();
            List<string> solutions = new List<string>();
            ReadSolutions(solutions, connection, puzzleName, File.ReadAllText(puzzleFilename), filterString, "Stats", false, true);
        }

        static void AddUsers(string savePath, string[] users)
        {
            string localFilename = Path.Combine(savePath, ".locals");
            Console.WriteLine("Checking for users in {0}", localFilename);
            SQLiteConnection connection = new SQLiteConnection(string.Format("Data Source={0};", localFilename));
            connection.Open();
            foreach (string user in users)
            {
                int numAdded = new SQLiteCommand(string.Format("INSERT OR IGNORE INTO User VALUES ('{0}',0,'{0}.user',datetime('now','localtime'))", user), connection).ExecuteNonQuery();
                Console.WriteLine(numAdded == 1 ? "  Added {0}" : "  {0} already present", user);
                if (numAdded != 1)
                {
                    string saveFile = (string)(new SQLiteCommand(string.Format("SELECT save_file FROM User WHERE name='{0}'", user), connection).ExecuteScalar());
                    if (saveFile != string.Format("{0}.user", user)) throw new Exception(string.Format(
                        "User {0} should reference save file {0}.user but references save file {1} ", user, saveFile));
                }

                // Check save file and create/replace from new.user file if not present/valid
                string saveFilename = Path.Combine(Path.Combine(savePath, "save"), user + ".user");
                FileInfo saveFileinfo = new FileInfo(saveFilename);
                long minLength = new FileInfo("new.user").Length;
                if (saveFileinfo.Exists)
                {
                    if (saveFileinfo.Length < minLength)
                    {
                        Console.WriteLine("    Save file {0} too small ({1}) to be valid. Replacing with new save file ({2} bytes)", saveFilename, saveFileinfo.Length, minLength);
                        File.Copy("new.user", saveFilename, true);
                    }
                    else
                    {
                        Console.WriteLine("    Existing save file {0} looks ok ({1} bytes)", saveFilename, saveFileinfo.Length);
                    }
                }
                else
                {
                    Console.WriteLine("    Creating new save file {0} ({1} bytes)", saveFilename, minLength);
                    File.Copy("new.user", saveFilename);
                }
            }
        }

        static void RestoreUsers(string savePath, string[] users)
        {
            string localFilename = Path.Combine(savePath, ".locals");
            Console.WriteLine("Removing tournament users from {0}", localFilename);
            SQLiteConnection connection = new SQLiteConnection(string.Format("Data Source={0};", localFilename));
            connection.Open();
            foreach (string user in users)
            {
                int numDeleted = new SQLiteCommand(string.Format("DELETE From User WHERE name='{0}' and save_file='{0}.user'", user), connection).ExecuteNonQuery();
                Console.WriteLine(numDeleted == 1 ? "  Deleted {0}" : "  {0} not present", user);
            }
        }

        static void ResetImages(string imagePath)
        {
            Console.WriteLine("Reverting changes to {0}", imagePath);
            foreach (string originalImageFilename in Directory.GetFiles(imagePath, "*.tex.original"))
            {
                string imageFilename = Path.Combine(imagePath, Path.GetFileNameWithoutExtension(originalImageFilename));
                Console.WriteLine("  Reverting {0} to {1}", imageFilename, originalImageFilename);
                File.Delete(imageFilename);
                File.Move(originalImageFilename, imageFilename);
            }
        }

        static string[] FindImageDefinition(string tournamentPath, string puzzleName)
        {
            string imageFilename = Path.Combine(Path.Combine(tournamentPath, FindRoundFromPuzzle(tournamentPath, puzzleName)), puzzleName + ".images");
            Console.WriteLine("Loading image definition from {0}", imageFilename);
            return File.ReadAllLines(imageFilename);
        }

        static void Draw(byte[] image, int x, int y, int w, byte[] feature)
        {
            for (int dy = 0; dy < feature.Length / (4 * w); ++dy)
                for (int dx = 0; dx < w; ++dx)
                {
                    float a = feature[4 * w * dy + 4 * dx + 3] / 255.0f;
                    image[4096 * (y + dy) + 4 * (x + dx) + 0] = (byte)((1.0f - a) * image[4096 * (y + dy) + 4 * (x + dx) + 0] + a * feature[4 * w * dy + 4 * dx + 0]);
                    image[4096 * (y + dy) + 4 * (x + dx) + 1] = (byte)((1.0f - a) * image[4096 * (y + dy) + 4 * (x + dx) + 1] + a * feature[4 * w * dy + 4 * dx + 1]);
                    image[4096 * (y + dy) + 4 * (x + dx) + 2] = (byte)((1.0f - a) * image[4096 * (y + dy) + 4 * (x + dx) + 2] + a * feature[4 * w * dy + 4 * dx + 2]);
                }
        }

        static void SetImages(string imagePath, string[] imageDefinition)
        {
            Console.WriteLine("  Loading reactor feature images from {0}", Environment.CurrentDirectory);
            byte[] nowaldo = File.ReadAllBytes(Path.Combine(Environment.CurrentDirectory, "nowaldo.tex"));
            byte[] hbarrier = File.ReadAllBytes(Path.Combine(Environment.CurrentDirectory, "hbarrier.tex"));
            byte[] vbarrier = File.ReadAllBytes(Path.Combine(Environment.CurrentDirectory, "vbarrier.tex"));

            string[] images = { "024.tex", "041.tex" };
            foreach (string imageName in images)
            {
                if (imageDefinition[0].ToUpper() == "NORMAL" && imageName != "024.tex") continue;
                const int cellSize = 79;
                int ox = imageName == "024.tex" ? 4 : -145;
                int minx = imageName == "024.tex" ? 0 : 6;
                int maxx = imageName == "024.tex" && imageDefinition[0].ToUpper() == "LARGE" ? 5 : 9;
                string imageFilename = Path.Combine(imagePath, imageName);

                if (!File.Exists(imageFilename + ".original"))
                {
                    Console.WriteLine("  Copying {0} to {0}.original", imageFilename);
                    File.Copy(imageFilename, imageFilename + ".original");
                }

                Console.WriteLine("  Using image definition to create {0} from {0}.original", imageFilename);
                byte[] image = File.ReadAllBytes(imageFilename + ".original");
                foreach (string feature in imageDefinition)
                {
                    string[] arguments = feature.Split(',');
                    switch (arguments[0].ToUpper())
                    {
                        case "NOWALDO":
                            for (int cy = int.Parse(arguments[2]); cy <= int.Parse(arguments[4]); ++cy)
                                for (int cx = Math.Max(minx, int.Parse(arguments[1])); cx <= Math.Min(maxx, int.Parse(arguments[3])); ++cx)
                                    Draw(image, ox + cellSize * cx, cellSize * cy + 1, cellSize, nowaldo);
                            break;
                        case "HBARRIER":
                            for (int cx = Math.Max(minx, int.Parse(arguments[2])); cx <= Math.Min(maxx, int.Parse(arguments[3])); ++cx)
                                Draw(image, ox + cellSize * cx, cellSize * int.Parse(arguments[1]), cellSize, hbarrier);
                            break;
                        case "VBARRIER":
                            for (int cy = int.Parse(arguments[2]); cy <= int.Parse(arguments[3]); ++cy)
                                if (int.Parse(arguments[1]) >= minx && int.Parse(arguments[1]) <= maxx)
                                    Draw(image, ox + cellSize * int.Parse(arguments[1]) - 1, cellSize * cy + 1, 3, vbarrier);
                            break;
                    }
                }
                File.WriteAllBytes(imageFilename, image);
            }
        }

        static void CheckDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Console.WriteLine("  {0} found", path);
            }
            else
            {
                Console.WriteLine("  {0} NOT FOUND", path);
            }
        }

        static bool CheckFile(string path)
        {
            if (File.Exists(path))
            {
                Console.WriteLine("  {0} found (attributes: {1})", path, File.GetAttributes(path));
                return true;
            }
            else
            {
                Console.WriteLine("  {0} NOT FOUND", path);
                return false;
            }
        }

        static void CheckSQLite(string path, bool listUsers)
        {
            if (CheckFile(path))
            {
                Console.WriteLine("  {0} found (attributes: {1})", path, File.GetAttributes(path));
                try
                {
                    byte[] test = File.ReadAllBytes(path);
                    Console.WriteLine("  {0} read {1} bytes", path, test.Length);
                    try
                    {
                        SQLiteConnection connection = new SQLiteConnection(string.Format("Data Source={0};", path));
                        connection.Open();
                        Console.WriteLine("  {0} connected", path);
                        if (listUsers)
                        {
                            SQLiteDataReader users = new SQLiteCommand(string.Format("SELECT name,save_file,last_played FROM User ORDER BY rowid"), connection).ExecuteReader();
                            while (users.Read())
                            {
                                Console.WriteLine("    User:{0} Save:{1} Last played:{2}", users["name"], users["save_file"], users["last_played"]);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("  {0} CANNOT CONNECT: {1}", path, e);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("  {0} CANNOT READ: {1}", path, e);
                }
            }
        }

        static int Main(string[] args)
        {
            try
            {
                string configFilename = Path.Combine(Environment.CurrentDirectory, "config.txt");
                string defaultConfigFilename = Path.Combine(Environment.CurrentDirectory, "default_config.txt");
                if (!File.Exists(configFilename))
                {
                    Console.WriteLine("Copying default config from {0} to {1}", defaultConfigFilename, configFilename);
                    File.Copy(defaultConfigFilename, configFilename);
                }
                Console.WriteLine("Reading config from {0}", configFilename);
                Dictionary<string, string> config = new Dictionary<string, string>();
                foreach (string line in File.ReadAllLines(configFilename))
                {
                    config[line.Split('=')[0].ToUpper()] = line.Split('=')[1];
                }
                if (config["USER"] == "Solver") throw new Exception(string.Format("Please set your user name in {0}", configFilename));

                string savePath;
                string imagePath;
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.MacOSX:
                    case PlatformID.Unix:
                        // Some versions of Mono don't use PlatformID.MacOSX so these cases can't be separated
                        savePath = Environment.GetEnvironmentVariable("HOME") + "/.local/share/Zachtronics Industries/SpaceChem";
                        imagePath = Environment.GetEnvironmentVariable("HOME") + "/Library/Application Support/Steam/SteamApps/common/SpaceChem/SpaceChem.app/Contents/Resources/images";
                        if (!Directory.Exists(imagePath))
                        {
                            // MacOSX location not found so use Unix location
                            imagePath = Environment.GetEnvironmentVariable("HOME") + "/.local/share/Steam/SteamApps/common/SpaceChem/images";
                        }
                        break;
                    default:
                        savePath = Environment.GetEnvironmentVariable("LOCALAPPDATA") + "\\Zachtronics Industries\\SpaceChem";
                        imagePath = Environment.GetEnvironmentVariable("ProgramFiles") + "\\Steam\\steamapps\\common\\spacechem\\images";
                        break;
                }
                if (config.ContainsKey("IMAGEPATH"))
                {
                    imagePath = config["IMAGEPATH"];
                }

                if (args.Length == 2 && args[0].ToUpper() == "PLAY")
                {
                    AddUsers(savePath, new string[] { config["PLAYSAVE"] });
                    AddPuzzles(Path.Combine(Path.Combine(savePath, "save"), config["PLAYSAVE"] + ".user"), Path.Combine(Environment.CurrentDirectory, args[1]));
                }
                else if (args.Length > 0 && args.Length < 3 && args[0].ToUpper() == "IMAGES")
                {
                    if (args.Length == 1)
                    {
                        ResetImages(imagePath);
                    }
                    else
                    {
                        string[] imageDefinition = FindImageDefinition(Environment.CurrentDirectory, args[1]);
                        SetImages(imagePath, imageDefinition);
                    }
                }
                else if (args.Length >= 2 && args.Length <= 3 && args[0].ToUpper() == "EXPORT")
                {
                    ExportSolutions(Path.Combine(Path.Combine(savePath, "save"), config["PLAYSAVE"] + ".user"),
                        Environment.CurrentDirectory, args[1], args.Length > 2 ? args[2] : "*-*-*", config["USER"]);
                }
                else if (args.Length == 2 && args[0].ToUpper() == "IMPORT")
                {
                    AddUsers(savePath, new string[] { config["IMPORTSAVE"] });
                    ImportSolutions(Path.Combine(Path.Combine(savePath, "save"), config["IMPORTSAVE"] + ".user"), Path.Combine(Environment.CurrentDirectory, args[1]));
                }
                else if (args.Length >= 2 && args.Length <= 3 && args[0].ToUpper() == "COPY")
                {
                    CopyPuzzle(Path.Combine(Path.Combine(savePath, "save"), config["PLAYSAVE"] + ".user"),
                        Environment.CurrentDirectory, args[1], args.Length > 2 ? args[2] : "*-*-*", false);
                }
                else if (args.Length >= 2 && args.Length <= 3 && args[0].ToUpper() == "COPYSWAPPED")
                {
                    CopyPuzzle(Path.Combine(Path.Combine(savePath, "save"), config["PLAYSAVE"] + ".user"),
                        Environment.CurrentDirectory, args[1], args.Length > 2 ? args[2] : "*-*-*", true);
                }
                else if (args.Length >= 2 && args.Length <= 3 && args[0].ToUpper() == "STATS")
                {
                    ExtendedStats(Path.Combine(Path.Combine(savePath, "save"), config["PLAYSAVE"] + ".user"),
                        Environment.CurrentDirectory, args[1], args.Length > 2 ? args[2] : "*-*-*");
                }
                else if (args.Length == 1 && args[0].ToUpper() == "ADDUSERS")
                {
                    AddUsers(savePath, new string[] { config["PLAYSAVE"], config["IMPORTSAVE"] });
                }
                else if (args.Length == 1 && args[0].ToUpper() == "REMOVEUSERS")
                {
                    RestoreUsers(savePath, new string[] { config["PLAYSAVE"], config["IMPORTSAVE"] });
                }
                else if (args.Length == 1 && args[0].ToUpper() == "DIAGNOSE")
                {
                    Console.WriteLine("Diagnostic checks:");
                    CheckDirectory(savePath);
                    CheckSQLite(Path.Combine(savePath, ".locals"), true);
                    CheckDirectory(Path.Combine(savePath, "save"));
                    foreach (string filename in Directory.GetFiles(Path.Combine(savePath, "save"), "*.user"))
                    {
                        CheckSQLite(filename, false);
                    }
                    CheckDirectory(imagePath);
                    CheckFile(Path.Combine(imagePath, "024.tex"));
                    CheckFile(Path.Combine(imagePath, "041.tex"));
                }
                else
                {
                    throw new Exception(string.Format(
                        "{0} {1}. Usage:{2}" +
                        "{2}" +
                        "{0} play [round name]{2}" +
                        "{0} images [puzzle name]{2}" +
                        "{0} export [round or puzzle name] [optional stats filter]{2}" +
                        "{0} import [round name]{2}" +
                        "{0} copy [puzzle name] [optional stats filter]{2}" +
                        "{0} copyswapped [puzzle name] [optional stats filter]{2}" +
                        "{0} stats [puzzle name] [optional stats filter]{2}" +
                        "{0} addusers{2}" +
                        "{0} removeusers{2}" +
                        "{0} diagnose{2}" +
                        "Filters are of the form cycles-reactors-symbols with * matching any value. " +
                        "Copy commands also accept Incomplete for the cycles filter which matches incomplete solutions. ",
                        "SpaceChemTool", Version, Environment.NewLine));
                }
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return 1;
            }
        }
    }
}
