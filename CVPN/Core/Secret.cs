using System.Security.Cryptography;
using System.Text;

namespace CVPN.Core;

/// <summary>
/// Шифрование учётных данных через DPAPI. Ключ принадлежит учётной записи
/// Windows, поэтому расшифровать файл под другим пользователем или на другой
/// машине не выйдет - это осознанная плата за то, что нам не нужно хранить
/// или спрашивать мастер-пароль.
/// </summary>
public static class Secret
{
    /// <summary>
    /// Метка зашифрованного значения. Без неё нельзя отличить шифротекст
    /// от обычной строки - а это нужно, чтобы прочитать файлы,
    /// сохранённые до появления шифрования.
    /// </summary>
    private const string Prefix = "dpapi:";
 
    /// <summary>
    /// Дополнительная соль, привязанная к приложению. DPAPI и без неё
    /// не даст расшифровать чужому пользователю, но с ней значение
    /// не прочитает и другая программа, запущенная от вашего имени.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CVPN.profiles.v1");
 
    /// <summary>Сколько значений не удалось расшифровать с момента запуска.</summary>
    public static int FailureCount { get; private set; }
 
    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
 
        try
        {
            var bytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
 
            return Prefix + Convert.ToBase64String(bytes);
        }
        catch (Exception)
        {
            // Шифрование недоступно - лучше сохранить как есть, чем потерять профиль
            return plain;
        }
    }
 
    /// <summary>
    /// Расшифровывает значение. Строку без метки возвращает как есть - так
    /// читаются файлы, созданные до появления шифрования.
    /// </summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
 
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(stored[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
 
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            // Файл перенесли с другой машины или из другой учётной записи.
            // Пустое значение честнее исключения: профиль останется в списке,
            // а пользователь увидит, что данные нужно ввести заново.
            FailureCount++;
            return "";
        }
    }
 
    /// <summary>Уже зашифровано - чтобы не шифровать дважды.</summary>
    public static bool IsProtected(string? value) =>
        value?.StartsWith(Prefix, StringComparison.Ordinal) == true;
 
    public static void ResetFailures() => FailureCount = 0;
}
