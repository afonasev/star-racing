using System.ComponentModel;

namespace StarRacingPrototype.Distribution;

public static class LauncherErrors {
    public static string Describe(Exception error) {
        string reason = error switch {
            FileNotFoundException => "Не найден один из файлов игры. Выполните восстановление через установщик.",
            UnauthorizedAccessException => "Нет доступа к файлам игры или её настройкам. Проверьте права доступа и уведомления антивируса.",
            Win32Exception { NativeErrorCode: 2 or 3 or 126 } => "Не найден исполняемый файл игры или нужная библиотека. Выполните восстановление через установщик и проверьте карантин антивируса.",
            Win32Exception { NativeErrorCode: 5 } => "Windows запретила запуск игры. Проверьте права доступа и уведомления антивируса.",
            Win32Exception { NativeErrorCode: 193 or 216 } => "Windows не может запустить этот файл. Нужна 64-разрядная Windows и установщик для Windows x64.",
            InvalidDataException => "Конфигурация установки повреждена или не соответствует версии игры. Выполните восстановление через установщик.",
            _ => "Не удалось подготовить запуск игры. Закройте установщик, если он работает, и повторите запуск. Если ошибка повторяется, выполните восстановление через установщик."
        };
        var code = error is Win32Exception native ? native.NativeErrorCode : error.HResult;
        return "Не удалось запустить Star Racing.\n\n" + reason +
            $"\n\nПользовательские данные сохранятся.\nКод: SR-LAUNCH (0x{code:X8})";
    }
}
