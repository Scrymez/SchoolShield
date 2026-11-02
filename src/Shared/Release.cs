using System;
using System.IO;

namespace SchoolShield
{
    /// <summary>Constants shared by the launcher and the installer.</summary>
    internal static class Release
    {
        public const string Author = "Латипов С.А.";
        public const string Repo = "Scrymez/SchoolShield";
        public const string SiteUrl = "https://github.com/Scrymez/SchoolShield";

        // Local, free NSFW classifier: Yahoo open_nsfw (BSD-2-Clause) converted to ONNX. 23.5 MB.
        public const string ModelFile = "open-nsfw.onnx";
        public const string ModelSha256 = "864bb37bf8863564b87eb330ab8c785a79a773f4e7c43cb96db52ed8611305fa";
        public const long ModelSize = 23590724;
        public const string ModelUrl = "https://github.com/Scrymez/SchoolShield/releases/download/ai-model-1/open-nsfw.onnx";

        public static string DataDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SchoolShield");
            }
        }

        public static string ModelDir { get { return Path.Combine(DataDir, "models"); } }
        public static string ModelPath { get { return Path.Combine(ModelDir, ModelFile); } }

        public static string InstallDirDefault
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SchoolShield"); }
        }
    }
}
