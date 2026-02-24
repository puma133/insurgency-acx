# Автообновление клиента AC-X через GitHub Releases

Клиент при запуске запрашивает **GitHub API** и сравнивает версию из последнего Release с `CLIENT_VERSION` в коде. Если на GitHub версия новее — скачивает `insurgency_acx.exe` и перезапускается с обновлением.

Репозиторий в коде задан как **`puma133/insurgency-acx`** — отдельный репозиторий для античита на вашем GitHub: [github.com/puma133](https://github.com/puma133).

---

## 0. Создание репозитория на GitHub (один раз)

1. Откройте [github.com/new](https://github.com/new) (или **Your repositories** → **New**).
2. **Repository name:** введите `insurgency-acx` (или другое имя — тогда поменяйте `GITHUB_REPO` в `Program.cs`).
3. **Description:** например: `AC-X client for Insurgency 2014 — screenshot anti-cheat, auto-update via Releases`.
4. Выберите **Public**.
5. **Не** ставьте галочки «Add a README», «Add .gitignore» — репозиторий создайте пустым (код добавите с локальной машины).
6. Нажмите **Create repository**.

После создания GitHub покажет команды для первого пуша. Используйте один из вариантов ниже.

### Вариант A: новый репозиторий только с клиентом (отдельная папка)

Если хотите, чтобы в `insurgency-acx` лежал только код клиента:

```powershell
cd d:\Vscode\insurgency-scripting\ac_screenshot\client_cs
git init
git add .
git commit -m "AC-X client 1.0.0"
git branch -M main
git remote add origin https://github.com/puma133/insurgency-acx.git
git push -u origin main
```

(Если у вас уже есть другие файлы в `client_cs`, добавьте `.gitignore` для `bin/`, `obj/`, `*.user` и т.д., чтобы не пушить сборку.)

### Вариант B: весь проект insurgency-scripting, но релизы из другого репозитория

Оставляете основной код в текущем репозитории. Репозиторий `insurgency-acx` тогда можно использовать **только для Releases**: создаёте его пустым, при первом релизе загружаете туда только собранный `insurgency_acx.exe` через веб-интерфейс (Releases → Create release → прикрепить exe). Код клиента может по-прежнему жить в `insurgency-scripting`, а в `Program.cs` уже указано `puma133/insurgency-acx` — клиенты будут качать обновления из этого репо.

---

## 1. Репозиторий в коде

В `Program.cs` уже задано:

```csharp
const string GITHUB_REPO = "puma133/insurgency-acx";
```

Клиент обращается к:  
`https://api.github.com/repos/puma133/insurgency-acx/releases/latest`

---

## 2. Что ожидает клиент от Release

- **Тег (tag):** версия в формате `v1.0.0` или `1.0.0` (рекомендуется `v1.0.0`).
- **Файл в Assets:** ровно одно прикреплённое к релизу имя файла: **`insurgency_acx.exe`**.

Если в последнем Release нет файла `insurgency_acx.exe`, обновление не выполнится.

---

## 3. Как выложить новую версию (вручную)

### Шаг 1: Поднять версию в коде

В `Program.cs` измените:

```csharp
const string CLIENT_VERSION = "1.0.1";   // новая версия
```

Сохраните, закоммитьте и запушьте в GitHub (чтобы в репозитории была актуальная версия).

### Шаг 2: Собрать exe

В папке `ac_screenshot/client_cs` выполните:

```powershell
.\build.ps1
```

Или вручную:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Готовый файл будет здесь:  
`bin\Release\net8.0\win-x64\publish\insurgency_acx.exe`

### Шаг 3: Переименовать exe (если нужно)

Клиент ищет в Release файл **`insurgency_acx.exe`**.  
В сборке он уже выходит с таким именем (задаётся в проекте как `AssemblyName`). Если у вас по какой-то причине другое имя — переименуйте в `insurgency_acx.exe` перед загрузкой.

### Шаг 4: Создать Release на GitHub

1. Откройте репозиторий на GitHub.
2. Справа: **Releases** → **Create a new release**.
3. **Choose a tag:** нажмите **Find or create a new tag**, введите тег версии, например `v1.0.0` (или `v1.0.1` для следующего релиза) и нажмите **Create new tag**.
4. **Release title:** можно оставить как тег, например `v1.0.0`, или написать «AC-X 1.0.0».
5. В **Description** при желании укажите список изменений.
6. В блок **Attach binaries** перетащите файл **`insurgency_acx.exe`** (или нажмите и выберите его). Имя файла должно быть именно **`insurgency_acx.exe`**.
7. Нажмите **Publish release**.

После этого клиенты с версией ниже указанной в теге при следующем запуске получат обновление.

---

## 4. Важные моменты

- **Версия в теге и в коде:** тег релиза (например `v1.0.1`) должен быть **новее** текущей `CLIENT_VERSION` у пользователя. Имеет смысл сначала поднять `CLIENT_VERSION` в коде, собрать exe из этого кода и выложить его под тегом с той же версией.
- **Один exe в релизе:** клиент скачивает первый найденный ассет с именем `insurgency_acx.exe`. Лучше прикреплять к каждому Release только один такой файл.
- **Публичный репозиторий:** для анонимного доступа к Releases репозиторий должен быть публичным (или использовать токен, если позже добавите авторизацию).

---

## 5. Краткий чеклист перед каждым релизом

1. В `Program.cs`: обновить `CLIENT_VERSION` (например на `1.0.2`).
2. Закоммитить и запушить изменения.
3. Собрать: `.\build.ps1`.
4. На GitHub: **Releases** → **Draft a new release** (или **Create a new release**).
5. Указать тег `v1.0.2` (или вашу версию).
6. Прикрепить `bin\Release\net8.0\win-x64\publish\insurgency_acx.exe` (имя в релизе должно быть **insurgency_acx.exe**).
7. Опубликовать Release.

После этого все установленные клиенты с версией ниже `1.0.2` при следующем запуске обновятся автоматически.
