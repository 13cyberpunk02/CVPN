using System.Text;

namespace CVPN.Services;

/// <summary>
/// Разбор и нормализация аргументов ciadpi.
///
/// Человек вводит аргументы строкой, как в byedpi.bat. Приложение обязано
/// контролировать две вещи:
///
///   • адрес - ciadpi по умолчанию слушает 0.0.0.0, то есть открывает SOCKS-прокси
///     всей локальной сети. Подставляем 127.0.0.1 всегда;
///   • порт - на него настроен outbound в конфиге sing-box, расхождение
///     молча сломает маршрут.
///
/// Свои --ip и --port из строки выбрасываются, а не побеждают: иначе конфиг
/// и процесс разойдутся. О выброшенном сообщаем, а не молчим.
/// </summary>
public static class ByeDpiArguments
{
    public const string LoopbackAddress = "127.0.0.1";

    public sealed record Result(IReadOnlyList<string> Arguments, IReadOnlyList<string> Dropped)
    {
        /// <summary>Строка для показа человеку; запуск идёт по списку, без повторной склейки.</summary>
        public string CommandLine => string.Join(' ', Arguments.Select(Quote));
    }

    /// <param name="userArguments">Аргументы для ByeDPI</param>
    /// <param name="port">Номер порта</param>
    /// <param name="connIp">
    /// Адрес, к которому привязываются исходящие соединения (--conn-ip). null - не задавать.
    /// Если человек указал свой --conn-ip / -I, побеждает его выбор.
    /// </param>
    public static Result Build(string userArguments, int port, string? connIp = null)
    {
        var tokens = Tokenize(userArguments);
        var kept = new List<string>();
        var dropped = new List<string>();

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (IsNetworkOption(token, out var takesNext))
            {
                var text = token;

                if (takesNext && i + 1 < tokens.Count)
                {
                    text += " " + tokens[i + 1];
                    i++;
                }

                dropped.Add(text);
                continue;
            }

            kept.Add(token);
        }

        var arguments = new List<string> { "--ip", LoopbackAddress, "--port", port.ToString() };

        if (!string.IsNullOrWhiteSpace(connIp) && !kept.Any(IsConnIpOption))
        {
            arguments.Add("--conn-ip");
            arguments.Add(connIp);
        }

        arguments.AddRange(kept);

        return new Result(arguments, dropped);
    }

    private static bool IsConnIpOption(string token) =>
        token is "--conn-ip" or "-I" ||
        token.StartsWith("--conn-ip=", StringComparison.Ordinal) ||
        (token.Length > 2 && token[0] == '-' && token[1] == 'I');

    /// <summary>--ip, -i, --port, -p в любой из форм: «--ip X», «--ip=X», «-iX».</summary>
    private static bool IsNetworkOption(string token, out bool takesNext)
    {
        takesNext = false;

        if (token is "--ip" or "-i" or "--port" or "-p")
        {
            takesNext = true;
            return true;
        }

        if (token.StartsWith("--ip=", StringComparison.Ordinal) ||
            token.StartsWith("--port=", StringComparison.Ordinal))
            return true;

        return token.Length > 2 &&
               token[0] == '-' && token[1] != '-' &&
               token[1] is 'i' or 'p';
    }

    /// <summary>
    /// Делит строку по пробелам с учётом двойных кавычек - так пути с пробелами
    /// (списки доменов, например) остаются одним аргументом.
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken) result.Add(current.ToString());
                current.Clear();
                hasToken = false;
                continue;
            }

            current.Append(c);
            hasToken = true;
        }

        if (hasToken) result.Add(current.ToString());

        return result;
    }

    private static string Quote(string argument) =>
        argument.Length == 0 || argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;
}