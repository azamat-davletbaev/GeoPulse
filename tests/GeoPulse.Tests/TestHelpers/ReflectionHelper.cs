using System.Reflection;

namespace GeoPulse.Tests.TestHelpers;

/// <summary>
/// Доступ к приватным статическим методам продового кода без изменения самого кода.
/// Используется только для чистых функций (парсеры, классификация событий), у которых нет
/// публичного API — рефакторинг продового кода под тесты вне рамок задачи.
/// </summary>
internal static class ReflectionHelper
{
    public static object InvokePrivateStatic(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Приватный статический метод '{methodName}' не найден в {type.FullName}.");

        return method.Invoke(null, args)!;
    }

    public static object? GetProperty(object target, string name)
    {
        var property = target.GetType().GetProperty(name)
            ?? throw new InvalidOperationException($"Свойство '{name}' не найдено в {target.GetType().FullName}.");

        return property.GetValue(target);
    }
}
