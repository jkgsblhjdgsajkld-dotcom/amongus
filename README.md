# HostOnlyMod (Among Us + Reactor)

Mod só para o anfitrião: cooldown 0 e tasks além do limite.

## Compilar (precisa do .NET 6 SDK ou superior)
    dotnet build -c Release
O arquivo sai em `bin/Release/net6.0/HostOnlyMod.dll`.

## Instalar
1. Instale o BepInEx 6 (IL2CPP) no Among Us.
2. Coloque o Reactor (`Reactor.dll`) em `BepInEx/plugins/`.
3. Coloque `HostOnlyMod.dll` em `BepInEx/plugins/`.
4. Ajuste a versão do jogo em `<GameVersion>` no .csproj antes de compilar.

## Config
`BepInEx/config/` -> `ExtraTasks` (padrão 10) e `ZeroCooldowns`.

Só o anfitrião precisa do mod (`ModFlags.None`). Se um patch quebrar após
atualização do jogo, os nomes de métodos/campos mudaram: atualize o GameLibs.
