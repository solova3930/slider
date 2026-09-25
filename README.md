# Slider

Приложение для показа плейлистов из изображений и видео на Windows.

## Возможности

- Добавление фото и видео в плейлист.
- Визуальная подсказка при перетаскивании файлов.
- Периоды показа с датой и временем начала и окончания.
- Повтор периода от 1 до 99 раз; значение сохраняется в плейлисте.
- Настройка дней недели и порядка периодов.
- Трансляция окна показа через FFmpeg.

## Скачать

Текущая версия: **v1.3.0**.

[Скачать релиз для Windows x64](https://github.com/solova3930/slider/releases/tag/v1.3.0).

Распакуйте архив и запустите `slider.exe`. Устанавливать .NET Runtime отдельно не нужно.
Для трансляции оставьте `ffmpeg.exe` рядом с приложением.

## Запуск из исходников

Требуется Windows и .NET 8 SDK или новее.

```powershell
git clone https://github.com/solova3930/slider.git
cd slider
dotnet run --project slider/slider.csproj
```

Для трансляции добавьте `ffmpeg.exe` в папку приложения или PATH.
Бинарный файл FFmpeg не хранится в Git.

## Проверки

```powershell
dotnet build slider.sln -c Release
dotnet run --project tests/PeriodRepeatChecks -c Release
```

[Описание проверок](tests/PeriodRepeatChecks/README.md).
