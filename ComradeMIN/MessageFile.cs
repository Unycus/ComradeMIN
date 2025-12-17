using System;

namespace ComradeMIN
{
    public class MessageFile
    {
        public int FileId { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
        public byte[] FileData { get; set; }
        public int FileSize { get; set; }

        public string FileHash { get; set; }

        public FileStatus Status { get; set; } = FileStatus.NotDownloaded;

        public string CachedFilePath { get; set; }

        public enum FileStatus
        {
            NotDownloaded,
            Downloading,
            Downloaded,
            Error
        }
    }
}