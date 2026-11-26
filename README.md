# Структура misc64.demo

Это каркас проекта из спецификации

Из папки проекта:

```powershell
dotnet run -- --run C:\Desktop\misc64.demo\tests\ops.asm

dotnet run -- --watch registers
dotnet run -- --watch code
dotnet run -- --watch data
dotnet run -- --watch history
```

Собрать без запуска:

```powershell
dotnet build
```

Открыть в Visual Studio можно файл `VmProject.csproj` или папку `C:\Desktop\misc64.demo`.
