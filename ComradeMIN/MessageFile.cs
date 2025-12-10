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

        // Хеш для идентификации файла в кэше
        public string FileHash { get; set; }

        // Статус файла
        public FileStatus Status { get; set; } = FileStatus.NotDownloaded;

        // Путь к кэшированному файлу
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