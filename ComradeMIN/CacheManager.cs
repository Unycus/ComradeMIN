using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ComradeMIN
{
    public class CacheManager
    {
        private readonly string _cacheFolder;
        private readonly int _userId;
        private readonly string _connectionString;

        public CacheManager(int userId, string connectionString)
        {
            _userId = userId;
            _connectionString = connectionString;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _cacheFolder = Path.Combine(appData, "ComradeMIN", "Cache", userId.ToString());

            if (!Directory.Exists(_cacheFolder))
                Directory.CreateDirectory(_cacheFolder);
        }

        private string GetChatCacheFile(int chatId)
        {
            return Path.Combine(_cacheFolder, $"chat_{chatId}.json");
        }

        public async Task<List<CachedMessage>> LoadChatMessagesFromCache(int chatId)
        {
            string cacheFile = GetChatCacheFile(chatId);

            if (!File.Exists(cacheFile))
                return new List<CachedMessage>();

            try
            {
                string json = await File.ReadAllTextAsync(cacheFile);
                return JsonSerializer.Deserialize<List<CachedMessage>>(json) ?? new List<CachedMessage>();
            }
            catch (Exception)
            {
                return new List<CachedMessage>();
            }
        }

        public async Task SaveChatMessagesToCache(int chatId, List<CachedMessage> messages)
        {
            string cacheFile = GetChatCacheFile(chatId);

            try
            {
                var messagesToCache = messages
                    .OrderByDescending(m => m.SendDate)
                    .Take(100)
                    .ToList();

                string json = JsonSerializer.Serialize(messagesToCache, new JsonSerializerOptions { WriteIndented = false });
                await File.WriteAllTextAsync(cacheFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения кэша: {ex.Message}");
            }
        }

        public async Task UpdateCacheWithNewMessages(int chatId, List<CachedMessage> newMessages)
        {
            var cachedMessages = await LoadChatMessagesFromCache(chatId);

            foreach (var newMsg in newMessages)
            {
                if (!cachedMessages.Any(m => m.MessageId == newMsg.MessageId))
                {
                    cachedMessages.Add(newMsg);
                }
            }

            await SaveChatMessagesToCache(chatId, cachedMessages);
        }

        public async Task<int?> GetLastCachedMessageId(int chatId)
        {
            var cachedMessages = await LoadChatMessagesFromCache(chatId);
            return cachedMessages.OrderByDescending(m => m.MessageId).FirstOrDefault()?.MessageId;
        }

        public void ClearChatCache(int chatId)
        {
            string cacheFile = GetChatCacheFile(chatId);
            if (File.Exists(cacheFile))
                File.Delete(cacheFile);
        }

        public void ClearAllCache()
        {
            if (Directory.Exists(_cacheFolder))
                Directory.Delete(_cacheFolder, true);
        }

        public long GetCacheSize()
        {
            if (!Directory.Exists(_cacheFolder))
                return 0;

            var files = Directory.GetFiles(_cacheFolder, "*.json", SearchOption.AllDirectories);
            return files.Sum(file => new FileInfo(file).Length);
        }
    }

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

    public class CachedFile
    {
        public int FileId { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
        public byte[] FileData { get; set; }
        public int FileSize { get; set; }

        public bool ShouldCache => FileSize <= 5 * 1024 * 1024;
    }
}