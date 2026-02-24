# AC Screenshot Client (C#) — Windows 11

Клиент на C#: один exe, без установки Python. Захват экрана через GDI, отправка на приёмник.

## Требования

- Windows 10/11 (x64 или x86)
- .NET 8.0 Runtime (или публикуем self-contained — тогда не нужен)

## Настройка

1. Скопируйте `config.example.ini` в `config.ini`.
2. В `config.ini` укажите:
   - `receiver_url` — URL приёмника (например `http://your-server:8765`)
   - `steam_id` — ваш Steam ID в формате `STEAM_0:1:12345`
   - `token` — опционально, если на приёмнике задан `AC_SCREENSHOT_API_KEY`
   - `interval_sec` — 0 = только по запросу; >0 = отправка раз в N секунд
   - `jpeg_quality` — 1–95
   - `max_width` — ширина в пикселях (0 = без ресайза)

## Сборка

```bat
cd ac_screenshot\client_cs
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Exe будет в `bin\Release\net8.0\win-x64\publish\ac_screenshot_client.exe`. Рядом положите `config.ini`.

## Запуск

```bat
ac_screenshot_client.exe
```

- При `interval_sec > 0` — скриншот отправляется каждые N секунд.
- При `interval_sec = 0` — раз в 10 секунд опрашивается список запросов; при запросе админом (`sm_screenshot`) делается снимок и отправка.

## Формат config.ini

Такой же, как у Python-клиента в `client/` — можно использовать один и тот же конфиг.
