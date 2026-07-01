using System;
using System.IO;

namespace EasyBuy.Library // Đảm bảo đúng namespace của bạn
{
    public sealed class MyLogger
    {
        // 1. Singleton Instance (Dùng Lazy để an toàn đa luồng)
        private static readonly Lazy<MyLogger> _instance =
            new Lazy<MyLogger>(() => new MyLogger());

        public static MyLogger Instance => _instance.Value;

        private readonly string _logFilePath;

        // 2. Private Constructor (Ngăn chặn việc tạo mới bên ngoài)
        private MyLogger()
        {
            // Tự động tạo folder "Logs" trong thư mục chạy của project
            string logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);

            _logFilePath = Path.Combine(logFolder, $"log_{DateTime.Now:yyyyMMdd}.txt");
        }

        // 3. Hàm ghi Log
        public void Log(string message)
        {
            lock (_instance) // Đảm bảo không bị lỗi khi nhiều người cùng ghi log 1 lúc
            {
                string logEntry = $"[{DateTime.Now:HH:mm:ss}] - {message}";

                // Ghi ra Terminal để có thể xem được khi chạy lệnh dotnet run
                Console.WriteLine(logEntry);

                // Ghi vào file .txt
                File.AppendAllLines(_logFilePath, new[] { logEntry });
            }
        }
    }
}