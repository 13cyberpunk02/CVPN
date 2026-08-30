using System.Security.AccessControl;
using System.Security.Principal;

namespace CVPN.Service;

/// <summary>
/// Ограничение доступа к каталогу службы.
///
/// В ProgramData по умолчанию у группы «Пользователи» есть право чтения.
/// А служба хранит там config.json, где учётные данные прокси записаны
/// открытым текстом - иначе ядро их не прочитает. Без этой правки любой
/// локальный пользователь мог бы их посмотреть.
/// </summary>
public static class DataDirectory
{
    public static void Prepare(string path)
    {
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "rules"));

        try
        {
            Restrict(path);
        }
        catch (Exception)
        {
            // Не удалось выставить права - служба всё равно должна работать.
            // Разбор проблемы уйдёт в журнал через ILogger вызывающего кода.
        }
    }

    private static void Restrict(string path)
    {
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl();

        // Рвём наследование и выбрасываем унаследованные разрешения:
        // именно среди них лежит чтение для группы «Пользователи»
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            security.RemoveAccessRule(rule);

        Allow(security, WellKnownSidType.LocalSystemSid);
        Allow(security, WellKnownSidType.BuiltinAdministratorsSid);

        info.SetAccessControl(security);
    }

    private static void Allow(DirectorySecurity security, WellKnownSidType sid)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(sid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }
}