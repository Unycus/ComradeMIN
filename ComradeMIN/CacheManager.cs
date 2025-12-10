using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace ComradeMIN
{
    public class CacheManager
    {
        private readonly string _cacheFolder;
        private readonly int _userId;
        private readonly Dictionary<string, CachedFileInfo> _fileCache = new();

        // Класс для хранения информации о кэшированном файле
        public class CachedFileInfo
        {
            public string FileName { get; set; }
            public string FilePath { get; set; }
            public string FileType { get; set; }
            public long FileSize { get; set; }
            public DateTime CacheDate { get; set; }
        }

        // Класс для сериализации кэша
        [Serializable]
        private class CacheData
        {
            public Dictionary<string, CachedFileInfo> FileCache { get; set; } = new();
        }

        public CacheManager(int userId)
        {
            _userId = userId;

            // Папка кэша в AppData
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _cacheFolder = Path.Combine(appData, "ComradeMIN", "Cache", userId.ToString());

            if (!Directory.Exists(_cacheFolder))
                Directory.CreateDirectory(_cacheFolder);

            LoadCache();
        }

        // Получение пути к папке кэша (публичный метод)
        public string GetCacheFolder()
        {
            return _cacheFolder;
        }

        private string GetCacheFilePath() => Path.Combine(_cacheFolder, "cache.dat");

        private void LoadCache()
        {
            try
            {
                string cacheFile = GetCacheFilePath();
                if (File.Exists(cacheFile))
                {
                    string json = File.ReadAllText(cacheFile);
                    var cacheData = JsonSerializer.Deserialize<CacheData>(json);
                    if (cacheData?.FileCache != null)
                    {
                        // Копируем данные, а не присваиваем ссылку
                        foreach (var item in cacheData.FileCache)
                        {
                            _fileCache[item.Key] = item.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки кэша: {ex.Message}");
            }
        }

        private void SaveCache()
        {
            try
            {
                var cacheData = new CacheData { FileCache = _fileCache };
                string json = JsonSerializer.Serialize(cacheData);
                File.WriteAllText(GetCacheFilePath(), json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка сохранения кэша: {ex.Message}");
            }
        }

        // Проверка наличия файла в кэше
        public bool IsFileCached(string fileHash)
        {
            return _fileCache.ContainsKey(fileHash);
        }

        // Получение кэшированного файла
        public CachedFileInfo GetCachedFile(string fileHash)
        {
            return _fileCache.TryGetValue(fileHash, out var info) ? info : null;
        }

        // Кэширование файла
        public async Task CacheFileAsync(string fileHash, string fileName, string fileType, byte[] fileData)
        {
            try
            {
                string filePath = Path.Combine(_cacheFolder, fileHash + Path.GetExtension(fileName));
                await File.WriteAllBytesAsync(filePath, fileData);

                _fileCache[fileHash] = new CachedFileInfo
                {
                    FileName = fileName,
                    FilePath = filePath,
                    FileType = fileType,
                    FileSize = fileData.Length,
                    CacheDate = DateTime.Now
                };

                SaveCache();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка кэширования файла: {ex.Message}");
            }
        }

        // Очистка старых файлов (старше 7 дней)
        public void CleanupOldFiles()
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-7);
                var toRemove = _fileCache
                    .Where(kv => kv.Value.CacheDate < cutoffDate)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var key in toRemove)
                {
                    if (File.Exists(_fileCache[key].FilePath))
                        File.Delete(_fileCache[key].FilePath);
                    _fileCache.Remove(key);
                }

                SaveCache();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка очистки кэша: {ex.Message}");
            }
        }

        // Получение размера кэша
        public long GetCacheSize()
        {
            long totalSize = 0;
            foreach (var info in _fileCache.Values)
            {
                if (File.Exists(info.FilePath))
                {
                    totalSize += new FileInfo(info.FilePath).Length;
                }
            }
            return totalSize;
        }
    }
}