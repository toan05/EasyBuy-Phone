using System;
using System.Collections.Concurrent;
using System.IO;

namespace EasyBuy.Services.Search
{
    // Class được đánh dấu sealed để ngăn chặn việc kế thừa, đảm bảo tính duy nhất của Singleton
    public sealed class SearchCacheSingleton
    {
        // Khởi tạo an toàn trong môi trường đa luồng (Thread-Safe) bằng Lazy<T>
        private static readonly Lazy<SearchCacheSingleton> _instance =
            new Lazy<SearchCacheSingleton>(() => new SearchCacheSingleton());

        public static SearchCacheSingleton Instance => _instance.Value;

        // Sử dụng ConcurrentDictionary để an toàn khi có nhiều request AJAX tìm kiếm cùng lúc
        private readonly ConcurrentDictionary<string, object> _cache;
        private readonly string _logFilePath = "search_history.log";
        private readonly object _fileLock = new object();

        private SearchCacheSingleton()
        {
            _cache = new ConcurrentDictionary<string, object>();

            // Đọc và in lịch sử ra Terminal khi khởi động lại project
            if (File.Exists(_logFilePath))
            {
                Console.WriteLine("\n=== LỊCH SỬ HOẠT ĐỘNG TÌM KIẾM TRƯỚC ĐÓ ===");
                Console.WriteLine(File.ReadAllText(_logFilePath));
                Console.WriteLine("===========================================\n");
            }
        }

        public bool TryGet(string keyword, out object result)
        {
            bool isFound = _cache.TryGetValue(keyword.ToLower().Trim(), out result);
            if (isFound)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[CACHE HIT] Đã tìm thấy '{keyword}' trong Singleton Cache! Trả về kết quả ngay lập tức mà không cần chọc xuống Database.");
                Console.ResetColor();
            }
            return isFound;
        }

        public void Add(string keyword, object result)
        {
            // Giới hạn số lượng cache để tránh tràn RAM (nếu đạt mốc thì xóa đi làm lại)
            if (_cache.Count > 1000)
            {
                _cache.Clear();
            }
            
            if (_cache.TryAdd(keyword.ToLower().Trim(), result))
            {
                string logLine = $"[{DateTime.Now:dd/MM/yyyy HH:mm:ss}] Đã truy vấn Database và lưu Cache cho từ khóa: '{keyword}'";
                
                // Ghi vào file an toàn trong môi trường đa luồng (tránh lỗi file bị chiếm dụng)
                lock (_fileLock)
                {
                    File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
                }
                
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(logLine);
                Console.ResetColor();
            }
        }

        public void Clear()
        {
            _cache.Clear();
        }
    }
}