namespace FileSplitter
{
    using System.Text.RegularExpressions;
    using System.Text;
    using System.Linq;
    using System.Security.Cryptography.X509Certificates;

    public class Launcher
    {
        static
            private readonly string
                CHUNKED_DATA_FOLDER_SUFFIX = "_chunks",
                SECTOR_EXTENSION = ".chunk";

        static private  int MAX_SECTOR_SIZE_MIB = 5;

        private delegate void FileTask(string file);

        public static void Main(params string[] args)
        {

            try
            {

                string command = args[0];
                ReadOnlySpan<string> files = args.AsSpan(1);
                foreach (var file in files)
                {
                    FileTask task =
                    command.Equals("MERGE", StringComparison.OrdinalIgnoreCase) ? MergeBackFile :
                    command.Equals("SPLIT", StringComparison.OrdinalIgnoreCase) ? StartFileSplit :
                    Noop;

                    task.Invoke(file);
                }
            }
            catch (IndexOutOfRangeException)
            {

                Console.WriteLine("Invalid  command.\nsplit <filepath>\t\tsplit file into 5 MiB chunks\nmerge <source>  \t\tmerges chunks into a single file from a source directory.");
            }
            catch (Exception ex)
            {

                Console.WriteLine(ex.Message);
                Console.WriteLine($"\n\n[StackTrace]{ex.StackTrace}");
            }
        }

        private static void Noop(string instruction) => Console.WriteLine("Invalid command");

        private static void StartFileSplit(string sourceFile)
        {

            FileInfo fileInfo = new(sourceFile);

            if (!fileInfo.Exists)
            {
                Console.WriteLine($"File cannot be located.");
                return;
            }
            if (fileInfo.Length <= MAX_SECTOR_SIZE_MIB * 1024 * 1024)
            {
                Console.WriteLine("File is too small for this operation. ");
                return;
            }

            long fileSize = fileInfo.Length;

            int sectSize = MAX_SECTOR_SIZE_MIB;

            Console.WriteLine
            ($"Filepath:\t{fileInfo.FullName}\nSize In Bytes:\t{fileSize}\nPossible {MAX_SECTOR_SIZE_MIB} MiB sectors:\t{fileSize / (sectSize * 1024 * 1024)}");
            SplitFile(fileInfo);

        }


        public static void SplitFile(FileInfo file)
        {
            long fileSize = file.Length;
            using FileStream reader = new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using BinaryReader binReader = new(reader);

            if (!(file.Exists && file.Directory != null)){
                Console.WriteLine($"Cannot access source directory for file '{file.Name}'");
                return;
            }

            DirectoryInfo sourceDirectory = Directory.CreateDirectory
            (Path.GetFileNameWithoutExtension(file.FullName) + CHUNKED_DATA_FOLDER_SUFFIX);

            while (reader.Position < fileSize){

                int chunkReadLength = 1024 * MAX_SECTOR_SIZE_MIB * 1024;

                byte[] buffer = binReader.ReadBytes(chunkReadLength);

                string sectionPath = Path.Combine(sourceDirectory.FullName, $"{file.Name}_{reader.Position}{SECTOR_EXTENSION}");

                if (File.Exists(sectionPath))
                    continue;

                using FileStream buffWriter = File.Open(sectionPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
                buffWriter.Write(buffer);
            }
        }
        public static void MergeBackFile(string source)
        {

            DirectoryInfo directoryInfo = new(source);

            string sourcePrediction = Path.GetFileNameWithoutExtension(source) + CHUNKED_DATA_FOLDER_SUFFIX;

            if (!directoryInfo.Exists)
            {
                Console.WriteLine($"Source '{directoryInfo}' does not exist.\nTrying to recover with '{sourcePrediction}'");

                directoryInfo = new(sourcePrediction);
                if (!directoryInfo.Exists)
                {
                    Console.WriteLine("Failure...");
                    return;
                }
                else Console.WriteLine("Successfully recovered, locating chunks now.");
            }

            FileInfo[] files = directoryInfo.GetFiles($"*{SECTOR_EXTENSION}", SearchOption.TopDirectoryOnly);

            IEnumerable<FileInfo> filesSorted =

                from file in files
                where file.Exists
                orderby file.CreationTimeUtc
                select file;


            string newFilePath = Path.Combine(directoryInfo.FullName, TryGetFileNameAndExtension(files[0]));
            if (File.Exists(newFilePath))
            {
                Console.WriteLine("A merged file with same name already exists at the path.");
                return;
            }


            using FileStream baseWriteStream = File.Open(newFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);

            Console.WriteLine($"Merging {filesSorted.Count()} chunks....");
            foreach (FileInfo file in filesSorted)
            {
                using FileStream reader = File.OpenRead(file.FullName);

                byte[] sectionReadBuffer = new byte[reader.Length];

                reader.Read(sectionReadBuffer, 0, sectionReadBuffer.Length);
                baseWriteStream.Write(sectionReadBuffer);
            }

            Console.WriteLine($"Done! File ready at {newFilePath}");
        }

        public static string TryGetFileNameAndExtension(FileInfo finfo)
        {

            // commited this ... atrocity

            //  run through the string, if char can be casted to int, drop it. (edit: this was bad bcz .mp4, .mp3  and allat )
            //  if a section of string matches the chunk extension constant, drop it.
            //  return

            // update :  used regex instead.....


            string fileName = finfo.Name;

            // test file pattern : <file>.<extension>_<reader-position-when-chunked>.<chunk> : file.mp4_4522344.chunk
            // regex  :  positive lookbehind after orignal extension for characters , including and after  '_'

            // -- >  asdasd__dfsadwrewr_3434.mp3
            RegexOptions options = RegexOptions.IgnoreCase;
            MatchCollection matches = Regex.Matches(fileName, @"(?<=\.[^.]+)_.*", options);

            StringBuilder stripTarget = new();

            foreach (var match in matches)
                stripTarget.Append(match);

            fileName = fileName.Replace(stripTarget.ToString(), "");

            return fileName;

        }


        public static void ChangeSectorSize(int newSize){
            MAX_SECTOR_SIZE_MIB =  newSize; 
        }
    }
}
