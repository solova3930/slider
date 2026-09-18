\# Slider



Приложение для показа плейлистов из изображений и видео на Windows.



\## Возможности



\- Добавление фото и видео в плейлист.

\- Визуальный overlay при перетаскивании файлов.

\- Разделение плейлиста на периоды с датой и временем начала и окончания.


\## Скачать



Готовая сборка для Windows x64 доступна в разделе Releases:



https://github.com/solova3930/slider/releases



Текущая версия: \*\*v1.1.1\*\*



Сборка self-contained и распространяется одним `slider.exe` — отдельно устанавливать .NET Runtime не требуется.



\## Запуск из исходников



Требуется Windows и .NET 8 SDK.



```powershell

git clone https://github.com/solova3930/slider.git

cd slider

dotnet run --project .\\slider\\slider.csproj

