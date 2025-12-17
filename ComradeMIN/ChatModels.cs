using System;
using System.Collections.Generic;

namespace ComradeMIN
{
    // Класс для кэшированных сообщений
    public class CachedMessage
    {
        public int MessageId { get; set; }
        public int ChatId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string MessageText { get; set; }
        public DateTime SendDate { get; set; }
        public bool IsRead { get; set; }
        public List<CachedFile> Files { get; set; } = new List<CachedFile>();
    }

    // Класс для кэшированных файлов
    public class CachedFile
    {
        public int FileId { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
        public byte[] FileData { get; set; }
        public int FileSize { get; set; }
        public string FileHash { get; set; }
        public FileCacheStatus CacheStatus { get; set; } = FileCacheStatus.NotCached;
        public string CachedFilePath { get; set; }

        public bool ShouldCache => FileSize <= 5 * 1024 * 1024;
        public CachedFile Clone()
        {
            return new CachedFile
            {
                FileId = this.FileId,
                FileName = this.FileName,
                FileType = this.FileType,
                FileData = this.FileData,
                FileSize = this.FileSize,
                FileHash = this.FileHash,
                CacheStatus = this.CacheStatus,
                CachedFilePath = this.CachedFilePath
            };
        }
    }
    public enum FileCacheStatus
    {
        NotCached,
        Downloading,
        Cached,
        Error
    }
}